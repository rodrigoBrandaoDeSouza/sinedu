using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Storage;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.Tenant.School;

namespace Sinedu.Tenant.Occurrences;

public sealed record FileContent(string FileName, string ContentType, byte[] Content);

/// <summary>Anexos de ocorrência (bucket da escola, download só pela API) e ata em PDF.</summary>
internal sealed class OccurrenceDocuments(
    OccurrencesDbContext db, OccurrenceService occurrences, IObjectStorage storage, TenantScope tenant, ISchoolProfileReader school,
    IAuditLog audit, TimeProvider clock)
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    static OccurrenceDocuments() => QuestPDF.Settings.License = LicenseType.Community;

    public async Task<AttachmentInfo?> UploadAsync(Guid occurrenceId, string fileName, byte[] content, Actor actor, CancellationToken ct)
    {
        var o = await occurrences.GetAsync(occurrenceId, actor.Id, ct);
        if (o is null)
        {
            return null;
        }

        if (o.Status == OccurrenceStatus.Cancelada)
        {
            throw new OccurrenceConflictException("Ocorrência cancelada não recebe anexos.");
        }

        var type = DocumentTypes.Detect(content);
        if (type is null || content.Length > DocumentTypes.MaxBytes)
        {
            throw new OccurrenceValidationException(new() { ["file"] = ["Envie PDF, PNG ou JPG de até 10 MB."] });
        }

        var id = Guid.CreateVersion7();
        var key = $"occurrences/{occurrenceId:N}/{id:N}";
        await storage.PutAsync(tenant.Current.BucketName, key, content, ct);
        var now = clock.GetUtcNow();
        var attachment = new OccurrenceAttachment
        {
            Id = id, OccurrenceId = occurrenceId, ObjectKey = key, FileName = DocumentTypes.SafeFileName(fileName, type), ContentType = type.ContentType,
            SizeBytes = content.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), UploadedBy = actor.Id, UploadedByName = actor.Name, UploadedAt = now,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("occurrence_attachment_uploaded", ResourceType: "occurrence", ResourceId: occurrenceId.ToString(), StudentId: o.StudentId), ct);
        return new AttachmentInfo(id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, actor.Name, now);
    }

    public async Task<FileContent?> DownloadAsync(Guid occurrenceId, Guid attachmentId, Guid viewer, CancellationToken ct)
    {
        var o = await occurrences.GetAsync(occurrenceId, viewer, ct);
        var a = o is null ? null : await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId && x.OccurrenceId == occurrenceId && x.RemovedAt == null, ct);
        if (a is null)
        {
            return null;
        }

        var content = await storage.GetAsync(tenant.Current.BucketName, a.ObjectKey, ct);
        if (content is null || Convert.ToHexStringLower(SHA256.HashData(content)) != a.Sha256)
        {
            throw new InvalidOperationException("Anexo indisponível ou alterado no armazenamento.");
        }

        return new FileContent(a.FileName, a.ContentType, content);
    }

    public async Task<bool> RemoveAsync(Guid occurrenceId, Guid attachmentId, Actor actor, CancellationToken ct)
    {
        var o = await occurrences.GetAsync(occurrenceId, actor.Id, ct);
        var a = o is null ? null : await db.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.OccurrenceId == occurrenceId && x.RemovedAt == null, ct);
        if (a is null)
        {
            return false;
        }

        a.RemovedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("occurrence_attachment_removed", ResourceType: "occurrence", ResourceId: occurrenceId.ToString(), StudentId: o!.StudentId), ct);
        return true;
    }

    /// <summary>
    /// Ata para impressão (spec §5.1 item 5; D12): cabeçalho da escola, dados pertinentes, trilha de autoria e campos de
    /// assinatura. Este módulo não tem acesso a Saúde/Proteção, então nada sensível pode entrar aqui.
    /// </summary>
    public async Task<FileContent?> AtaAsync(Guid occurrenceId, Actor actor, CancellationToken ct)
    {
        var o = await occurrences.GetAsync(occurrenceId, actor.Id, ct);
        if (o is null)
        {
            return null;
        }

        if (o.Status == OccurrenceStatus.Rascunho)
        {
            throw new OccurrenceConflictException("Registre a ocorrência antes de gerar a ata.");
        }

        var header = await school.GetAsync(ct);
        var number = o.Id.ToString("N")[..8].ToUpperInvariant();
        var generated = Local(clock.GetUtcNow());
        var pdf = Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Header().Column(c =>
            {
                var line = string.Join(" · ", new[] { header.InstitutionalCode is null ? null : $"Código {header.InstitutionalCode}", header.Address, header.Phone, header.Email }.OfType<string>());
                c.Item().Row(r =>
                {
                    // Logo da escola (F5-05), quando cadastrado na Administração.
                    if (header.Logo is { } logo)
                    {
                        r.ConstantItem(56).Height(48).Image(logo).FitArea();
                        r.ConstantItem(10);
                    }

                    r.RelativeItem().AlignMiddle().Column(t =>
                    {
                        t.Item().Text(header.Name).FontSize(14).Bold();
                        if (line.Length > 0)
                        {
                            t.Item().Text(line).FontSize(8).FontColor(Colors.Grey.Darken1);
                        }
                    });
                });

                c.Item().PaddingTop(8).Text($"Ata de registro de ocorrência nº {number}").FontSize(13).Bold();
                c.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingTop(10).Column(c =>
            {
                c.Spacing(5);
                Row(c, "Aluno(a)", o.StudentCode is null ? o.StudentName : $"{o.StudentName} · CGM {o.StudentCode}");
                Row(c, "Turma na data do fato", o.SchoolYear is { } y ? $"{o.Classroom} · {y}" : o.Classroom);
                Row(c, "Data e hora do fato", Local(o.OccurredAt));
                Row(c, "Local", o.Location);
                Row(c, "Categoria", o.Category);
                Row(c, "Gravidade", o.Severity);
                Row(c, "Situação", Status(o.Status));
                Row(c, "Descrição do fato", o.Description);
                Row(c, "Envolvidos", o.Involved);
                Row(c, "Providências", o.ActionsTaken);
                Row(c, "Encaminhamentos", o.Referrals);
                Row(c, "Observações", o.Notes);
                if (o.Status == OccurrenceStatus.Cancelada)
                {
                    Row(c, "Motivo do cancelamento", o.CancelReason);
                }

                c.Item().PaddingTop(10).Text("Trilha de autoria").Bold();
                Row(c, "Criada por", $"{o.CreatedByName} em {Local(o.CreatedAt)}");
                Row(c, "Registrada por", o.RegisteredByName is null ? null : $"{o.RegisteredByName} em {Local(o.RegisteredAt!.Value)}");
                foreach (var r in o.Revisions.OrderBy(r => r.Version))
                {
                    Row(c, $"Versão {r.Version}", $"{r.Action.Replace('_', ' ')} · {r.ChangedByName} · {Local(r.ChangedAt)} — {r.Reason}");
                }

                c.Item().PaddingTop(40).Row(r =>
                {
                    Signature(r, "Responsável pelo registro");
                    r.ConstantItem(20);
                    Signature(r, "Direção");
                    r.ConstantItem(20);
                    Signature(r, "Responsável legal");
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span($"Gerada em {generated} por {actor.Name}. Registro institucional; não é diagnóstico. Página ");
                t.CurrentPageNumber();
                t.Span(" de ");
                t.TotalPages();
            });
        })).GeneratePdf();

        await audit.WriteAsync(new AuditEntry("occurrence_ata_generated", ResourceType: "occurrence", ResourceId: occurrenceId.ToString(), StudentId: o.StudentId), ct);
        return new FileContent($"ata-ocorrencia-{number}.pdf", "application/pdf", pdf);
    }

    private static string Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, SaoPaulo).ToString("dd/MM/yyyy HH:mm", PtBr);

    private static string Status(OccurrenceStatus s) => s switch
    {
        OccurrenceStatus.Registrada => "Registrada",
        OccurrenceStatus.EmAcompanhamento => "Em acompanhamento",
        OccurrenceStatus.Concluida => "Concluída",
        OccurrenceStatus.Cancelada => "Cancelada",
        _ => "Rascunho",
    };

    private static void Row(ColumnDescriptor c, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        c.Item().Row(r =>
        {
            r.ConstantItem(140).Text(label).FontColor(Colors.Grey.Darken2);
            r.RelativeItem().Text(value);
        });
    }

    private static void Signature(RowDescriptor r, string label) =>
        r.RelativeItem().Column(c =>
        {
            c.Item().LineHorizontal(0.5f);
            c.Item().AlignCenter().Text(label).FontSize(8);
        });
}
