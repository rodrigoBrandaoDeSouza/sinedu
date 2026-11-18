using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Email;
using Sinedu.Tenant.Occurrences;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.Notifications;

/// <summary>Canal de envio plugável (spec 6.2). E-mail agora; WhatsApp Business oficial no futuro.</summary>
public interface INotificationProvider
{
    NotificationChannel Channel { get; }

    bool Enabled { get; }

    /// <summary>Envia e devolve o identificador do provedor, se houver. Lança exceção em falha.</summary>
    Task<string?> SendAsync(string to, string subject, string body, CancellationToken ct);
}

internal sealed class EmailNotificationProvider(IEmailSender email) : INotificationProvider
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public bool Enabled => true;

    public async Task<string?> SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        await email.SendAsync(new EmailMessage(to, subject, body), ct);
        return null;
    }
}

/// <summary>
/// WhatsApp fica DESABILITADO: só a API oficial do WhatsApp Business poderá ser ligada aqui, depois de configuração,
/// conformidade e testes. Nunca automação do WhatsApp Web.
/// </summary>
internal sealed class WhatsAppNotificationProvider : INotificationProvider
{
    public NotificationChannel Channel => NotificationChannel.WhatsApp;

    public bool Enabled => false;

    public Task<string?> SendAsync(string to, string subject, string body, CancellationToken ct) =>
        throw new InvalidOperationException("Canal WhatsApp desabilitado.");
}

public sealed record ChannelInfo(NotificationChannel Channel, bool Enabled, string? Reason);

public sealed record TemplateView(Guid Id, NotificationChannel Channel, string Name, string Subject, string Body, bool IsActive);

public sealed record RecipientPreview(
    Guid GuardianId, string Name, GuardianRelationship Relationship, string? Email, bool Eligible, string? Reason, string? Subject, string? Body);

public sealed record CommunicationPreview(
    Guid OccurrenceId, OccurrenceStatus OccurrenceStatus, IReadOnlyList<ChannelInfo> Channels, IReadOnlyList<TemplateView> Templates,
    Guid TemplateId, IReadOnlyList<RecipientPreview> Recipients);

public sealed record AttemptView(int AttemptNo, DateTimeOffset StartedAt, bool Succeeded, string? Error, string Trigger);

public sealed record NotificationView(
    Guid Id, Guid OccurrenceId, Guid StudentId, string RecipientName, string RecipientAddress, NotificationChannel Channel, string TemplateName,
    string Subject, string Body, NotificationStatus Status, int Attempts, DateTimeOffset? NextAttemptAt, DateTimeOffset? SentAt, string? LastError,
    string AuthorizedByName, DateTimeOffset AuthorizedAt, IReadOnlyList<AttemptView> History);

public sealed class CommunicationValidationException(Dictionary<string, string[]> errors) : Exception("Dados inválidos.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class CommunicationConflictException(string message) : Exception(message);

internal sealed partial class NotificationService(
    NotificationsDbContext db, IEnumerable<INotificationProvider> providers, IOccurrenceDirectory occurrences, IStudentRegistry students,
    ISchoolProfileReader school, IAuditLog audit, TimeProvider clock)
{
    public const int MaxAutomaticAttempts = 3;
    public const int PageSize = 50;
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly string[] Placeholders = ["{responsavel}", "{aluno}", "{escola}", "{data}", "{contato_escola}"];

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex PlaceholderPattern();

    public IReadOnlyList<ChannelInfo> Channels() =>
        providers.Select(p => new ChannelInfo(p.Channel, p.Enabled, p.Enabled ? null : "Desabilitado até a integração oficial ser configurada.")).ToList();

    public async Task<IReadOnlyList<TemplateView>> TemplatesAsync(bool activeOnly, CancellationToken ct) =>
        await db.Templates.AsNoTracking().Where(t => !activeOnly || t.IsActive).OrderBy(t => t.SortOrder)
            .Select(t => new TemplateView(t.Id, t.Channel, t.Name, t.Subject, t.Body, t.IsActive)).ToListAsync(ct);

    /// <summary>Modelos só aceitam os marcadores neutros: não há como incluir descrição, categoria ou dado sensível.</summary>
    public async Task<TemplateView?> UpdateTemplateAsync(Guid id, string? name, string? subject, string? body, bool isActive, CancellationToken ct)
    {
        var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null)
        {
            return null;
        }

        var errors = new Dictionary<string, string[]>();
        name = name?.Trim();
        subject = subject?.Trim();
        body = body?.Trim();
        if (name is not { Length: >= 3 and <= 100 }) errors["name"] = ["Use de 3 a 100 caracteres."];
        if (subject is not { Length: >= 3 and <= 200 }) errors["subject"] = ["Use de 3 a 200 caracteres."];
        if (body is not { Length: >= 20 and <= 4000 }) errors["body"] = ["Use de 20 a 4000 caracteres."];
        foreach (var (field, text) in new[] { ("subject", subject), ("body", body) })
        {
            var unknown = PlaceholderPattern().Matches(text ?? "").Select(m => m.Value).Where(m => !Placeholders.Contains(m)).Distinct().ToList();
            if (unknown.Count > 0)
            {
                errors[field] = [$"Marcadores permitidos: {string.Join(", ", Placeholders)}."];
            }
        }

        if (errors.Count > 0)
        {
            throw new CommunicationValidationException(errors);
        }

        t.Name = name!;
        t.Subject = subject!;
        t.Body = body!;
        t.IsActive = isActive;
        t.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("communication_template_updated", ResourceType: "template", ResourceId: id.ToString()), ct);
        return new TemplateView(t.Id, t.Channel, t.Name, t.Subject, t.Body, t.IsActive);
    }

    public async Task<CommunicationPreview?> PreviewAsync(Guid occurrenceId, Guid? templateId, CancellationToken ct)
    {
        var occurrence = await occurrences.FindFormalizedAsync(occurrenceId, ct);
        if (occurrence is null)
        {
            return null;
        }

        var templates = await TemplatesAsync(activeOnly: true, ct);
        var template = templates.FirstOrDefault(t => t.Id == templateId) ?? (templates.Count > 0 ? templates[0] : null)
            ?? throw new CommunicationConflictException("Nenhum modelo de mensagem ativo.");
        var context = await ContextAsync(occurrence, ct);
        var recipients = (await students.GetContactsAsync(occurrence.StudentId, ct)).Select(g =>
        {
            var reason = Ineligible(g);
            return new RecipientPreview(
                g.GuardianId, g.Name, g.Relationship, g.Email, reason is null, reason,
                reason is null ? Render(template.Subject, context, g.Name) : null,
                reason is null ? Render(template.Body, context, g.Name) : null);
        }).ToList();
        return new CommunicationPreview(occurrenceId, occurrence.Status, Channels(), templates, template.Id, recipients);
    }

    /// <summary>
    /// Autoriza e envia. Idempotente por (ocorrência, responsável, modelo, requestId): repetir o mesmo pedido devolve o que
    /// já existe. Um segundo pedido diferente para o mesmo responsável e modelo exige confirmação explícita.
    /// </summary>
    public async Task<IReadOnlyList<NotificationView>?> AuthorizeAsync(
        Guid occurrenceId, Guid templateId, IReadOnlyList<Guid> guardianIds, Guid requestId, bool confirmDuplicate, Occurrences.Actor actor, CancellationToken ct)
    {
        var occurrence = await occurrences.FindFormalizedAsync(occurrenceId, ct);
        if (occurrence is null)
        {
            return null;
        }

        if (occurrence.Status == OccurrenceStatus.Cancelada)
        {
            throw new CommunicationConflictException("Ocorrência cancelada não gera comunicação.");
        }

        var errors = new Dictionary<string, string[]>();
        var template = await db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, ct);
        if (template is null) errors["templateId"] = ["Escolha um modelo."];
        if (guardianIds is not { Count: > 0 }) errors["guardianIds"] = ["Escolha ao menos um responsável."];
        if (requestId == Guid.Empty) errors["requestId"] = ["Pedido sem identificador."];
        var provider = providers.FirstOrDefault(p => p.Channel == (template?.Channel ?? NotificationChannel.Email));
        if (provider is not { Enabled: true }) errors["channel"] = ["Canal desabilitado."];

        var contacts = (await students.GetContactsAsync(occurrence.StudentId, ct)).ToDictionary(g => g.GuardianId);
        foreach (var id in guardianIds ?? [])
        {
            if (!contacts.TryGetValue(id, out var g) || Ineligible(g) is not null)
            {
                errors["guardianIds"] = ["Há responsável sem autorização de contato ou sem e-mail."];
            }
        }

        if (errors.Count > 0)
        {
            throw new CommunicationValidationException(errors);
        }

        var keys = guardianIds!.Distinct().ToDictionary(g => g, g => $"{occurrenceId:N}:{g:N}:{templateId:N}:{requestId:N}");
        var existing = await db.Notifications.Where(n => keys.Values.Contains(n.IdempotencyKey)).ToListAsync(ct);
        if (existing.Count == keys.Count)
        {
            return await ViewsAsync(existing.Select(n => n.Id).ToList(), ct);
        }

        if (!confirmDuplicate)
        {
            var targets = keys.Keys.ToList();
            var previous = await db.Notifications.AsNoTracking()
                .AnyAsync(n => n.OccurrenceId == occurrenceId && n.TemplateId == templateId && targets.Contains(n.GuardianId)
                    && (n.Status == NotificationStatus.Enviado || n.Status == NotificationStatus.Pendente), ct);
            if (previous)
            {
                throw new CommunicationConflictException("Esta mensagem já foi enviada (ou está na fila) para o mesmo responsável. Confirme se quer enviar de novo.");
            }
        }

        var context = await ContextAsync(occurrence, ct);
        var now = clock.GetUtcNow();
        var created = new List<Notification>();
        foreach (var (guardianId, key) in keys)
        {
            if (existing.Any(n => n.IdempotencyKey == key))
            {
                continue;
            }

            var g = contacts[guardianId];
            var n = new Notification
            {
                Id = Guid.CreateVersion7(), OccurrenceId = occurrenceId, StudentId = occurrence.StudentId, GuardianId = guardianId, RecipientName = g.Name,
                Channel = template!.Channel, RecipientAddress = g.Email!, TemplateId = template.Id,
                RenderedSubject = Render(template.Subject, context, g.Name), RenderedBody = Render(template.Body, context, g.Name),
                AuthorizedBy = actor.Id, AuthorizedByName = actor.Name, AuthorizedAt = now, Status = NotificationStatus.Pendente, IdempotencyKey = key,
            };
            db.Notifications.Add(n);
            created.Add(n);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois cliques simultâneos com o mesmo pedido: a chave única segura; devolve o que ficou gravado.
            db.ChangeTracker.Clear();
            var saved = await db.Notifications.AsNoTracking().Where(n => keys.Values.Contains(n.IdempotencyKey)).Select(n => n.Id).ToListAsync(ct);
            return await ViewsAsync(saved, ct);
        }

        foreach (var n in created)
        {
            await audit.WriteAsync(new AuditEntry("communication_authorized", ResourceType: "communication", ResourceId: n.Id.ToString(), StudentId: n.StudentId), ct);
            await DispatchAsync(n, "envio", ct);
        }

        return await ViewsAsync([.. existing.Select(n => n.Id), .. created.Select(n => n.Id)], ct);
    }

    public async Task<NotificationView?> RetryAsync(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n is null)
        {
            return null;
        }

        if (n.Status != NotificationStatus.Falhou)
        {
            throw new CommunicationConflictException("Só é possível reenviar uma comunicação que falhou.");
        }

        await DispatchAsync(n, "reenvio manual", ct);
        return (await ViewsAsync([id], ct))[0];
    }

    public async Task<NotificationView?> CancelAsync(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n is null)
        {
            return null;
        }

        if (n.Status is not (NotificationStatus.Pendente or NotificationStatus.Falhou))
        {
            throw new CommunicationConflictException("Só é possível cancelar uma comunicação pendente ou com falha.");
        }

        n.Status = NotificationStatus.Cancelado;
        n.CancelledAt = clock.GetUtcNow();
        n.NextAttemptAt = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("communication_cancelled", ResourceType: "communication", ResourceId: id.ToString(), StudentId: n.StudentId), ct);
        return (await ViewsAsync([id], ct))[0];
    }

    /// <summary>Reenvio automático (fila): falhas com nova tentativa vencida e pendentes que não chegaram a ser enviados.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var stale = now.AddMinutes(-2);
        var due = await db.Notifications
            .Where(n => (n.Status == NotificationStatus.Falhou && n.NextAttemptAt != null && n.NextAttemptAt <= now)
                || (n.Status == NotificationStatus.Pendente && n.Attempts == 0 && n.AuthorizedAt <= stale))
            .OrderBy(n => n.AuthorizedAt).Take(50).ToListAsync(ct);
        foreach (var n in due)
        {
            await DispatchAsync(n, "reenvio automático", ct);
        }

        return due.Count;
    }

    public async Task<(IReadOnlyList<NotificationView> Items, int Total)> ListAsync(
        Guid? occurrenceId, Guid? studentId, NotificationStatus? status, int page, CancellationToken ct)
    {
        var q = db.Notifications.AsNoTracking();
        if (occurrenceId is { } o) q = q.Where(n => n.OccurrenceId == o);
        if (studentId is { } s) q = q.Where(n => n.StudentId == s);
        if (status is { } st) q = q.Where(n => n.Status == st);
        var total = await q.CountAsync(ct);
        var ids = await q.OrderByDescending(n => n.AuthorizedAt).Skip((Math.Max(page, 1) - 1) * PageSize).Take(PageSize).Select(n => n.Id).ToListAsync(ct);
        return (await ViewsAsync(ids, ct), total);
    }

    // ---------- apoio ----------

    private async Task DispatchAsync(Notification n, string trigger, CancellationToken ct)
    {
        var started = clock.GetUtcNow();
        n.Attempts++;
        var attempt = new NotificationAttempt { Id = Guid.CreateVersion7(), NotificationId = n.Id, AttemptNo = n.Attempts, StartedAt = started, Trigger = trigger };
        var provider = providers.FirstOrDefault(p => p.Channel == n.Channel);
        try
        {
            if (provider is not { Enabled: true })
            {
                throw new InvalidOperationException("Canal desabilitado.");
            }

            var messageId = await provider.SendAsync(n.RecipientAddress, n.RenderedSubject, n.RenderedBody, ct);
            n.Status = NotificationStatus.Enviado;
            n.SentAt = clock.GetUtcNow();
            n.ProviderMessageId = messageId;
            n.LastError = null;
            n.NextAttemptAt = null;
            attempt.Succeeded = true;
            attempt.ProviderMessageId = messageId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            n.Status = NotificationStatus.Falhou;
            n.LastError = Sanitize(ex);
            n.NextAttemptAt = n.Attempts < MaxAutomaticAttempts ? clock.GetUtcNow().Add(n.Attempts == 1 ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5)) : null;
            attempt.ErrorSanitized = n.LastError;
        }

        attempt.FinishedAt = clock.GetUtcNow();
        db.Attempts.Add(attempt);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra instância (ou o reenvio automático) tratou a mesma comunicação ao mesmo tempo.
            db.ChangeTracker.Clear();
            return;
        }

        await audit.WriteAsync(new AuditEntry(attempt.Succeeded ? "communication_sent" : "communication_failed",
            ResourceType: "communication", ResourceId: n.Id.ToString(), StudentId: n.StudentId), ct);
    }

    /// <summary>Nunca guarda a mensagem crua do servidor (pode conter endereço ou conteúdo): só o tipo da falha.</summary>
    private static string Sanitize(Exception ex) => ex switch
    {
        InvalidOperationException { Message: "Canal desabilitado." } => "Canal desabilitado.",
        TimeoutException => "Tempo esgotado ao falar com o servidor de e-mail.",
        System.Net.Sockets.SocketException or System.IO.IOException => "Servidor de e-mail indisponível.",
        _ => $"Falha no envio ({ex.GetType().Name}).",
    };

    private static string? Ineligible(GuardianContact g) =>
        !g.ContactAuthorized ? "Sem autorização de contato registrada."
        : string.IsNullOrWhiteSpace(g.Email) ? "Sem e-mail cadastrado."
        : null;

    private sealed record RenderContext(string School, string StudentFirstName, string Date, string SchoolContact);

    private async Task<RenderContext> ContextAsync(OccurrenceRef occurrence, CancellationToken ct)
    {
        var header = await school.GetAsync(ct);
        var found = await students.GetAsync([occurrence.StudentId], ct);
        var student = found.Count > 0 ? found[0] : null;
        var first = student?.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "o(a) estudante";
        var firstName = CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase(first.ToLowerInvariant());
        var contact = (header.Phone, header.Email) switch
        {
            ({ } p, { } e) => $" pelo telefone {p} ou pelo e-mail {e}",
            ({ } p, null) => $" pelo telefone {p}",
            (null, { } e) => $" pelo e-mail {e}",
            _ => "",
        };
        var date = TimeZoneInfo.ConvertTime(occurrence.OccurredAt, SaoPaulo).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return new RenderContext(header.Name, firstName, date, contact);
    }

    /// <summary>Só substitui marcadores neutros. Primeiro nome do aluno (mínimo necessário); nada do fato.</summary>
    private static string Render(string text, RenderContext c, string guardian) =>
        text.Replace("{responsavel}", guardian, StringComparison.Ordinal)
            .Replace("{aluno}", c.StudentFirstName, StringComparison.Ordinal)
            .Replace("{escola}", c.School, StringComparison.Ordinal)
            .Replace("{data}", c.Date, StringComparison.Ordinal)
            .Replace("{contato_escola}", c.SchoolContact, StringComparison.Ordinal);

    private async Task<IReadOnlyList<NotificationView>> ViewsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var list = await db.Notifications.AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync(ct);
        var attempts = await db.Attempts.AsNoTracking().Where(a => ids.Contains(a.NotificationId)).OrderBy(a => a.AttemptNo).ToListAsync(ct);
        var templates = await db.Templates.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return list.OrderBy(n => ids.ToList().IndexOf(n.Id)).Select(n => new NotificationView(
            n.Id, n.OccurrenceId, n.StudentId, n.RecipientName, n.RecipientAddress, n.Channel, templates.GetValueOrDefault(n.TemplateId, "Modelo"),
            n.RenderedSubject, n.RenderedBody, n.Status, n.Attempts, n.NextAttemptAt, n.SentAt, n.LastError, n.AuthorizedByName, n.AuthorizedAt,
            attempts.Where(a => a.NotificationId == n.Id).Select(a => new AttemptView(a.AttemptNo, a.StartedAt, a.Succeeded, a.ErrorSanitized, a.Trigger)).ToList()))
            .ToList();
    }
}
