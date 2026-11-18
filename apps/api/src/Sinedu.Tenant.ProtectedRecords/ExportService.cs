using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.ProtectedRecords;

/// <summary>
/// Exportação excepcional (F4-06): só com justificativa, gera um PDF com marca d'água identificando quem exportou,
/// quando e o número da exportação. A justificativa fica cifrada; a auditoria registra o evento sem conteúdo.
/// </summary>
internal sealed class ExportService(
    ProtectedDbContext db, ProtectedService records, ProtectedCrypto crypto, IStudentRegistry students, TenantScope tenant, IAuditLog audit, TimeProvider clock)
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    static ExportService() => QuestPDF.Settings.License = LicenseType.Community;

    public async Task<(byte[] Pdf, Guid ExportId)> ExportAsync(
        Guid studentId, bool health, bool protection, string? justification, Actor actor, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var why = PayloadRules.Clean(justification, 1000, "justification", errors);
        if (why is null || why.Length < 20)
        {
            errors["justification"] = ["Explique em pelo menos 20 caracteres por que a exportação é necessária."];
        }

        if (!health && !protection)
        {
            errors["sections"] = ["Escolha ao menos uma seção."];
        }

        if (errors.Count > 0)
        {
            throw new ProtectedValidationException(errors);
        }

        var student = (await students.GetAsync([studentId], ct)).Single();
        var healthView = health ? await records.GetHealthAsync(studentId, ct) : null;
        var protectionViews = protection ? await records.ListProtectionAsync(studentId, ct) : [];

        var now = clock.GetUtcNow();
        var export = new ProtectedExport
        {
            Id = Guid.CreateVersion7(), StudentId = studentId,
            Sections = string.Join(',', new[] { health ? "health" : null, protection ? "protection" : null }.OfType<string>()),
            JustificationCiphertext = [], JustificationNonce = [], JustificationTag = [], JustificationWrappedKey = "",
            CreatedBy = actor.Id, CreatedByName = actor.Name, CreatedAt = now,
        };
        var envelope = await crypto.EncryptAsync(why!, crypto.ExportAad(studentId, export.Id), ct);
        export.JustificationCiphertext = envelope.Ciphertext;
        export.JustificationNonce = envelope.Nonce;
        export.JustificationTag = envelope.Tag;
        export.JustificationWrappedKey = envelope.WrappedKey;
        db.Exports.Add(export);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("protected_exported", ResourceType: "export", ResourceId: export.Id.ToString(), StudentId: studentId), ct);

        var local = TimeZoneInfo.ConvertTime(now, SaoPaulo);
        var stamp = $"USO RESTRITO · {actor.Name} · {local.ToString("dd/MM/yyyy HH:mm", PtBr)} · nº {export.Id.ToString("N")[..8]}";
        var pdf = Render(tenant.Current.DisplayName, student, healthView, health, protectionViews, protection, stamp);
        return (pdf, export.Id);
    }

    private static byte[] Render(
        string school, StudentSnapshot student, HealthView? health, bool includeHealth, IReadOnlyList<ProtectionView> protection, bool includeProtection, string stamp) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Background().AlignCenter().AlignMiddle().Rotate(-35).Text(stamp).FontSize(18).FontColor(Colors.Red.Lighten3).Bold();

            page.Header().Column(c =>
            {
                c.Item().Text(school).FontSize(9).FontColor(Colors.Grey.Darken1);
                c.Item().Text("Exportação excepcional — dados protegidos").FontSize(15).Bold();
                c.Item().Text($"{student.FullName}{(student.InstitutionalCode is { } code ? $" · CGM {code}" : "")}").FontSize(11);
                c.Item().PaddingTop(4).Text(stamp).FontSize(8).FontColor(Colors.Red.Darken2);
                c.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingTop(10).Column(c =>
            {
                c.Spacing(6);
                if (includeHealth)
                {
                    c.Item().Text("Saúde").FontSize(13).Bold();
                    if (health is null)
                    {
                        c.Item().Text("Nenhuma ficha de saúde registrada.").Italic();
                    }
                    else
                    {
                        var d = health.Data;
                        Row(c, "Tipo sanguíneo", d.BloodType);
                        Row(c, "Alergias e cuidados", d.Allergies);
                        Row(c, "Condições relevantes", d.Conditions);
                        Row(c, "Medicamentos informados", d.Medications);
                        Row(c, "Restrições e adaptações", d.Restrictions);
                        Row(c, "Primeiros cuidados autorizados", d.FirstAidInstructions);
                        Row(c, "Contatos de emergência", d.EmergencyContacts is { Count: > 0 } ec
                            ? string.Join("\n", ec.Select(e => $"{e.Name}{(e.Relationship is null ? "" : $" ({e.Relationship})")}{(e.Phone is null ? "" : $" · {e.Phone}")}"))
                            : null);
                        Row(c, "Origem da informação", d.Source);
                        Row(c, "Observações", d.Notes);
                        Row(c, "Revisão prevista", health.ReviewDueOn?.ToString("dd/MM/yyyy", PtBr));
                        Row(c, "Versão", $"{health.Version} · por {health.UpdatedByName}");
                    }
                }

                if (includeProtection)
                {
                    c.Item().PaddingTop(10).Text("Proteção / Sigilo").FontSize(13).Bold();
                    if (protection.Count == 0)
                    {
                        c.Item().Text("Nenhum registro de proteção.").Italic();
                    }

                    foreach (var p in protection)
                    {
                        var d = p.Data;
                        c.Item().PaddingTop(6).Text($"{d.Title} — {p.Status}").Bold();
                        Row(c, "Tipo", p.Kind.ToString());
                        Row(c, "Validade", p.ValidUntil?.ToString("dd/MM/yyyy", PtBr));
                        Row(c, "Descrição", d.Description);
                        Row(c, "Autoridade / origem", d.Authority);
                        Row(c, "Documento", d.DocumentNumber);
                        Row(c, "Fundamentação", d.Basis);
                        Row(c, "Verificação", d.Verification.ToString());
                        Row(c, "Autorizados a retirar", People(d.AuthorizedPeople));
                        Row(c, "Impedidos de retirar", People(d.UnauthorizedPeople));
                        Row(c, "Observações", d.Notes);
                    }
                }
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Documento sigiloso. Proibida a reprodução ou o compartilhamento fora da finalidade justificada. Página ");
                t.CurrentPageNumber();
                t.Span(" de ");
                t.TotalPages();
            });
        })).GeneratePdf();

    private static void Row(ColumnDescriptor c, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        c.Item().Row(r =>
        {
            r.ConstantItem(150).Text(label).FontColor(Colors.Grey.Darken2);
            r.RelativeItem().Text(value);
        });
    }

    private static string? People(IReadOnlyList<PickupPerson>? people) =>
        people is { Count: > 0 }
            ? string.Join("\n", people.Select(p => $"{p.Name}{(p.Relationship is null ? "" : $" ({p.Relationship})")}{(p.Document is null ? "" : $" · doc. {p.Document}")}{(p.Note is null ? "" : $" — {p.Note}")}"))
            : null;
}
