using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Modules;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.School;

namespace Sinedu.Tenant.Analytics;

/// <summary>
/// Indicadores bimestrais (Fase 6). Só Direção e Pedagogia. Exportações têm só agregados, com supressão
/// de contagens pequenas (D11) — nunca nomes de alunos, nem dados de Saúde/Proteção (este módulo nem os acessa).
/// </summary>
public sealed class AnalyticsModule : IModule
{
    public string Name => "Analytics";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AnalyticsService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/analytics").RequireAuthorization(Policies.PedagogicalStaff).AddEndpointFilter(async (ctx, next) =>
        {
            try
            {
                return await next(ctx);
            }
            catch (AnalyticsValidationException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        api.MapGet("/options", async (Guid? yearId, AnalyticsService s, CancellationToken ct) => Results.Ok(await s.OptionsAsync(yearId, ct)));

        api.MapGet("/dashboard", async (Guid? yearId, int? term, DateOnly? from, DateOnly? to, Guid? classroomId, Guid? categoryId, AnalyticsService s, CancellationToken ct) =>
            Results.Ok(await s.DashboardAsync(new AnalyticsQuery(yearId, term, from, to, classroomId, categoryId), ct)));

        api.MapGet("/students", async (Guid? yearId, int? term, DateOnly? from, DateOnly? to, Guid? classroomId, Guid? categoryId, AnalyticsService s, CancellationToken ct) =>
        {
            var (period, rows) = await s.ByStudentAsync(new AnalyticsQuery(yearId, term, from, to, classroomId, categoryId), ct);
            return Results.Ok(new { period, rows });
        });

        api.MapGet("/export", async (string? format, Guid? yearId, int? term, DateOnly? from, DateOnly? to, Guid? classroomId, Guid? categoryId,
            AnalyticsService s, ISchoolProfileReader school, IAuditLog audit, HttpContext http, CancellationToken ct) =>
        {
            var data = await s.DashboardAsync(new AnalyticsQuery(yearId, term, from, to, classroomId, categoryId), ct);
            var header = await school.GetAsync(ct);
            http.Response.Headers.CacheControl = "no-store";
            await audit.WriteAsync(new AuditEntry("analytics_exported", ResourceType: "analytics", ResourceId: format ?? "csv"), ct);
            var slug = data.Period.Label.Replace(' ', '-').Replace('/', '-').Replace('º', 'o');
            return string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase)
                ? Results.File(Export.Pdf(header, data), "application/pdf", $"indicadores-{slug}.pdf")
                : Results.File(Export.Csv(header, data), "text/csv; charset=utf-8", $"indicadores-{slug}.csv");
        });
    }
}

/// <summary>Exportação dos agregados. D11: contagens de 1 a 4 viram "&lt;5" e a taxa correspondente é omitida.</summary>
internal static class Export
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    static Export() => QuestPDF.Settings.License = LicenseType.Community;

    public static string Suppress(int n) => n is > 0 and < 5 ? "<5" : n.ToString(PtBr);

    public static string Rate(ClassroomRow c) =>
        c.Occurrences is > 0 and < 5 || c.Enrolled is > 0 and < 5 || c.RatePer100 is null ? "—" : c.RatePer100.Value.ToString("0.0", PtBr);

    public static byte[] Csv(SchoolHeader school, Dashboard d)
    {
        var sb = new StringBuilder();
        void Line(params string[] cells) => sb.AppendLine(string.Join(';', cells.Select(Quote)));
        Line("Escola", school.Name);
        Line("Período", d.Period.Label, $"{d.Period.Start:dd/MM/yyyy} a {d.Period.End:dd/MM/yyyy}");
        Line("Método", d.Method);
        Line("Supressão", "Contagens de 1 a 4 aparecem como <5 para não identificar alunos.");
        sb.AppendLine();
        Line("Indicador", d.Period.Label, d.Period.PreviousLabel);
        Line("Alunos matriculados", Suppress(d.Enrolled), Suppress(d.EnrolledPrevious));
        Line("Ocorrências registradas", Suppress(d.Occurrences), Suppress(d.OccurrencesPrevious));
        Line("Alunos com ao menos 1 ocorrência", Suppress(d.StudentsWithOccurrence), Suppress(d.StudentsWithOccurrencePrevious));
        Line("Registradas", Suppress(d.Status.Registered));
        Line("Em acompanhamento", Suppress(d.Status.FollowUp));
        Line("Concluídas", Suppress(d.Status.Concluded));
        Line("Comunicações enviadas", Suppress(d.Notifications.Sent));
        Line("Comunicações pendentes", Suppress(d.Notifications.Pending));
        Line("Comunicações com falha", Suppress(d.Notifications.Failed));
        sb.AppendLine();
        Line("Turma", "Turno", "Matriculados", "Ocorrências", "Por 100 alunos");
        foreach (var c in d.ByClassroom) Line(c.Classroom, c.Shift, Suppress(c.Enrolled), Suppress(c.Occurrences), Rate(c));
        sb.AppendLine();
        Line("Categoria", "Ocorrências");
        foreach (var c in d.ByCategory) Line(c.Label, Suppress(c.Value));
        sb.AppendLine();
        Line("Semana", d.Period.Label, d.Period.PreviousLabel);
        foreach (var w in d.Weekly) Line(w.Week, w.Current is { } cur ? Suppress(cur) : "", w.Previous is { } prev ? Suppress(prev) : "");
        // BOM para o Excel em pt-BR abrir com acentos e ponto e vírgula.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public static byte[] Pdf(SchoolHeader school, Dashboard d) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(9));
            page.Header().Column(c =>
            {
                c.Item().Text(school.Name).FontSize(13).Bold();
                c.Item().Text($"Indicadores pedagógicos — {d.Period.Label} ({d.Period.Start:dd/MM/yyyy} a {d.Period.End:dd/MM/yyyy})").FontSize(11);
                c.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
            });
            page.Content().PaddingTop(8).Column(c =>
            {
                c.Spacing(10);
                Table(c, ["Indicador", d.Period.Label, d.Period.PreviousLabel],
                [
                    ["Alunos matriculados", Suppress(d.Enrolled), Suppress(d.EnrolledPrevious)],
                    ["Ocorrências registradas", Suppress(d.Occurrences), Suppress(d.OccurrencesPrevious)],
                    ["Alunos com ao menos 1 ocorrência", Suppress(d.StudentsWithOccurrence), Suppress(d.StudentsWithOccurrencePrevious)],
                    ["Em acompanhamento", Suppress(d.Status.FollowUp), ""],
                    ["Concluídas", Suppress(d.Status.Concluded), ""],
                    ["Comunicações enviadas / pendentes / falhas", $"{Suppress(d.Notifications.Sent)} / {Suppress(d.Notifications.Pending)} / {Suppress(d.Notifications.Failed)}", ""],
                ]);
                c.Item().Text("Comparação entre turmas").Bold();
                Table(c, ["Turma", "Turno", "Matriculados", "Ocorrências", "Por 100 alunos"],
                    d.ByClassroom.Select(r => new[] { r.Classroom, r.Shift, Suppress(r.Enrolled), Suppress(r.Occurrences), Rate(r) }).ToList());
                c.Item().Text("A taxa acompanha tendências e orienta ações pedagógicas. Não classifica turmas nem alunos.").Italic().FontColor(Colors.Grey.Darken1);
                c.Item().Text("Ocorrências por categoria").Bold();
                Table(c, ["Categoria", "Ocorrências"], d.ByCategory.Select(r => new[] { r.Label, Suppress(r.Value) }).ToList());
                c.Item().Text(d.Method).FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().Text("Contagens de 1 a 4 aparecem como <5 para não identificar alunos. Sem dados de saúde, proteção ou nomes.").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Página ");
                t.CurrentPageNumber();
                t.Span(" de ");
                t.TotalPages();
            });
        })).GeneratePdf();

    private static void Table(ColumnDescriptor c, string[] headers, IReadOnlyList<string[]> rows) =>
        c.Item().Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3);
                for (var i = 1; i < headers.Length; i++) cols.RelativeColumn(1.4f);
            });
            t.Header(h =>
            {
                foreach (var head in headers) h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(head).Bold();
            });
            foreach (var row in rows)
            {
                foreach (var cell in row) t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(cell);
            }
        });

    /// <summary>
    /// Célula CSV segura: aspas quando preciso e neutraliza fórmula (=, +, -, @, tab, CR). Nomes de turma e categoria
    /// são digitados pela escola e não podem virar fórmula ao abrir no Excel.
    /// </summary>
    internal static string Quote(string value) => Sinedu.SharedKernel.Web.Csv.Cell(value);
}
