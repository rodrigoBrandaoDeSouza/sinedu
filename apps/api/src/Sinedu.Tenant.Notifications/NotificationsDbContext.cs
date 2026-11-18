using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.Tenant.Notifications;

public enum NotificationChannel
{
    Email,

    /// <summary>Reservado para a API oficial do WhatsApp Business. Desabilitado até configuração, conformidade e testes (spec 6.2).</summary>
    WhatsApp,
}

public enum NotificationStatus
{
    Pendente,
    Enviado,
    Falhou,
    Cancelado,
}

/// <summary>Modelo de mensagem. Só aceita marcadores neutros (sem descrição, categoria ou qualquer dado do fato).</summary>
public sealed class NotificationTemplate
{
    public Guid Id { get; set; }

    public NotificationChannel Channel { get; set; }

    public required string Name { get; set; }

    public required string Subject { get; set; }

    public required string Body { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Uma comunicação para um responsável. O texto enviado fica gravado como foi renderizado.</summary>
public sealed class Notification
{
    public Guid Id { get; set; }

    public Guid OccurrenceId { get; set; }

    public Guid StudentId { get; set; }

    public Guid GuardianId { get; set; }

    public required string RecipientName { get; set; }

    public NotificationChannel Channel { get; set; }

    public required string RecipientAddress { get; set; }

    public Guid TemplateId { get; set; }

    public required string RenderedSubject { get; set; }

    public required string RenderedBody { get; set; }

    public Guid AuthorizedBy { get; set; }

    public required string AuthorizedByName { get; set; }

    public DateTimeOffset AuthorizedAt { get; set; }

    public NotificationStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public string? ProviderMessageId { get; set; }

    /// <summary>Erro técnico sanitizado: tipo da falha, nunca endereço, conteúdo ou resposta crua do servidor.</summary>
    public string? LastError { get; set; }

    /// <summary>Chave de idempotência (única): o mesmo pedido de envio nunca gera duas mensagens.</summary>
    public required string IdempotencyKey { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }
}

public sealed class NotificationAttempt
{
    public Guid Id { get; set; }

    public Guid NotificationId { get; set; }

    public int AttemptNo { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public bool Succeeded { get; set; }

    public string? ProviderMessageId { get; set; }

    public string? ErrorSanitized { get; set; }

    /// <summary>Quem pediu esta tentativa: envio inicial, reenvio automático ou reenvio manual.</summary>
    public required string Trigger { get; set; }
}

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    public static readonly Guid ContactInviteTemplate = new("0193a000-0000-7000-8000-000000000201");
    public static readonly Guid OccurrenceNoticeTemplate = new("0193a000-0000-7000-8000-000000000202");

    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationAttempt> Attempts => Set<NotificationAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<NotificationTemplate>(e =>
        {
            e.ToTable("templates");
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.HasData(
                new NotificationTemplate
                {
                    Id = ContactInviteTemplate, Channel = NotificationChannel.Email, SortOrder = 1, IsActive = true,
                    Name = "Convite para conversar com a escola",
                    Subject = "{escola}: pedido de contato",
                    Body = "Olá, {responsavel}.\n\nA equipe pedagógica do(a) {escola} gostaria de conversar com você sobre {aluno}. "
                        + "Por favor, entre em contato com a escola{contato_escola} ou compareça à secretaria no horário de atendimento.\n\n"
                        + "Esta é uma mensagem automática. Não responda a este e-mail.\n\nAtenciosamente,\nEquipe pedagógica — {escola}",
                },
                new NotificationTemplate
                {
                    Id = OccurrenceNoticeTemplate, Channel = NotificationChannel.Email, SortOrder = 2, IsActive = true,
                    Name = "Aviso de registro escolar",
                    Subject = "{escola}: registro escolar de {aluno}",
                    Body = "Olá, {responsavel}.\n\nInformamos que, em {data}, a escola fez um registro envolvendo {aluno}. "
                        + "Para saber mais, entre em contato com a escola{contato_escola}.\n\n"
                        + "Esta é uma mensagem automática. Não responda a este e-mail.\n\nAtenciosamente,\nEquipe pedagógica — {escola}",
                });
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RecipientName).HasMaxLength(200);
            e.Property(x => x.RecipientAddress).HasMaxLength(200);
            e.Property(x => x.RenderedSubject).HasMaxLength(300);
            e.Property(x => x.RenderedBody).HasMaxLength(6000);
            e.Property(x => x.AuthorizedByName).HasMaxLength(200);
            e.Property(x => x.ProviderMessageId).HasMaxLength(200);
            e.Property(x => x.LastError).HasMaxLength(300);
            e.Property(x => x.IdempotencyKey).HasMaxLength(200);
            e.Property<uint>("xmin").IsRowVersion();
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.OccurrenceId);
            e.HasIndex(x => x.StudentId);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });

        modelBuilder.Entity<NotificationAttempt>(e =>
        {
            e.ToTable("attempts");
            e.Property(x => x.ProviderMessageId).HasMaxLength(200);
            e.Property(x => x.ErrorSanitized).HasMaxLength(300);
            e.Property(x => x.Trigger).HasMaxLength(20);
            e.HasIndex(x => x.NotificationId);
        });
    }
}

public sealed class NotificationsDbContextDesignFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>().UseSineduPostgres("Host=localhost;Database=design", NotificationsDbContext.Schema).Options);
}
