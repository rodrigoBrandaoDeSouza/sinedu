using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Occurrences;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.Host.Operations;

/// <summary>
/// Escola em encerramento (P-13): só login/logout, <c>/api/me</c> e a exportação de encerramento. O resto → 503.
/// Fica depois da resolução da escola e antes da autenticação (decisão só pelo caminho).
/// </summary>
internal sealed class ClosingModeMiddleware(RequestDelegate next)
{
    private static readonly string[] Allowed = ["/auth", OidcContract.CallbackPath, OidcContract.SignedOutPath, "/api/me", "/api/health", "/api/closing"];

    public async Task InvokeAsync(HttpContext context, TenantScope scope)
    {
        if (scope.HasTenant && scope.Current.Closing && !Allowed.Any(p => context.Request.Path.StartsWithSegments(p)))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Results.Problem(
                title: "Esta escola está em encerramento. A Direção pode baixar a exportação dos dados.",
                statusCode: 503,
                extensions: new Dictionary<string, object?> { ["code"] = "tenant_closing" }).ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}

/// <summary>
/// Exportação de encerramento (P-13, decisão P11): dados comuns da escola em CSV, num ZIP, para a Direção guardar.
/// NUNCA inclui Saúde nem Proteção/Sigilo (o módulo protegido nem é consultado), auditoria ou anexos cifrados.
/// Disponível só com a escola em encerramento.
/// </summary>
internal static class ClosingExport
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/closing/export", async (HttpContext http, TenantScope scope, SchoolDbContext school, StudentsDbContext students,
            OccurrencesDbContext occurrences, IAuditLog audit, CancellationToken ct) =>
        {
            if (!scope.Current.Closing) return Results.NotFound();

            var zip = await BuildAsync(school, students, occurrences, ct);
            await audit.WriteAsync(new AuditEntry("closing_export", ResourceType: "tenant", ResourceId: scope.Current.Slug), ct);
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(zip, "application/zip", $"sinedu-{scope.Current.Slug}-dados-comuns.zip");
        }).RequireAuthorization(Policies.SchoolManager);
    }

    public static readonly string[] Files = ["LEIA-ME.txt", "turmas.csv", "alunos.csv", "matriculas.csv", "responsaveis.csv", "enderecos.csv", "equipe.csv", "ocorrencias.csv"];

    internal static async Task<byte[]> BuildAsync(SchoolDbContext school, StudentsDbContext students, OccurrencesDbContext occurrences, CancellationToken ct)
    {
        var years = await school.Years.AsNoTracking().ToDictionaryAsync(y => y.Id, y => y.Year, ct);
        var classrooms = await school.Classrooms.AsNoTracking().Include(c => c.Grade).OrderBy(c => c.SchoolYearId).ThenBy(c => c.Grade!.SortOrder).ThenBy(c => c.Section).ToListAsync(ct);
        var classroomName = classrooms.ToDictionary(c => c.Id, c => Classroom.DisplayName(c.Grade!.ShortName, c.Section, c.Shift));
        var studentRows = await students.Students.AsNoTracking().OrderBy(s => s.SearchName).ToListAsync(ct);
        var studentName = studentRows.ToDictionary(s => s.Id, s => (s.FullName, s.InstitutionalCode));
        var enrollments = await students.Enrollments.AsNoTracking().OrderBy(e => e.StartsOn).ToListAsync(ct);
        var active = enrollments.Where(e => e.Status == EnrollmentStatus.Ativa).GroupBy(e => e.StudentId).ToDictionary(g => g.Key, g => g.First().ClassroomId);
        var links = await students.StudentGuardians.AsNoTracking().Include(g => g.Guardian).ToListAsync(ct);
        var addresses = await students.Addresses.AsNoTracking().OrderBy(a => a.ValidFrom).ToListAsync(ct);
        var staff = await school.Staff.AsNoTracking().OrderBy(s => s.FullName).ToListAsync(ct);
        var categories = await occurrences.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var formal = await occurrences.Occurrences.AsNoTracking()
            .Where(o => o.Status != OccurrenceStatus.Rascunho)
            .OrderBy(o => o.OccurredAt).ToListAsync(ct);

        string D(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
        string T(DateTimeOffset? d) => d?.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "";
        string Room(Guid id) => classroomName.GetValueOrDefault(id, "");
        (string Name, string? Code) Who(Guid id) => studentName.GetValueOrDefault(id, ("", null));

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] content)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(content);
            }

            Add("LEIA-ME.txt", System.Text.Encoding.UTF8.GetBytes(
                "Exportação de encerramento do Sinedu — dados comuns da escola.\r\n\r\n" +
                "Arquivos CSV (separados por ponto e vírgula, abrem no Excel). Datas no horário de Brasília.\r\n" +
                "Não inclui Saúde nem Proteção/Sigilo: esses registros são cifrados e serão destruídos com a chave da escola.\r\n" +
                "Guarde este arquivo em local seguro: ele contém dados pessoais de estudantes (LGPD).\r\n"));

            Add("turmas.csv", Csv.Build(["Ano", "Turma", "Série", "Turno", "Situação", "Código externo"],
                classrooms.Select(c => new[] { years.GetValueOrDefault(c.SchoolYearId).ToString(CultureInfo.InvariantCulture), Room(c.Id), c.Grade!.Name, c.Shift.ToString(), c.Status.ToString(), c.ExternalCode })));

            Add("alunos.csv", Csv.Build(["Nome", "Nome social", "Nascimento", "CGM", "E-mail institucional", "Situação", "Turma atual"],
                studentRows.Select(s => new[] { s.FullName, s.SocialName, D(s.BirthDate), s.InstitutionalCode, s.InstitutionalEmail, s.Status.ToString(), active.TryGetValue(s.Id, out var c) ? Room(c) : "" })));

            Add("matriculas.csv", Csv.Build(["Aluno", "CGM", "Turma", "Ano", "Situação", "Início", "Fim", "Nº de chamada"],
                enrollments.Select(e => new[] { Who(e.StudentId).Name, Who(e.StudentId).Code, Room(e.ClassroomId), years.GetValueOrDefault(e.SchoolYearId).ToString(CultureInfo.InvariantCulture), e.Status.ToString(), D(e.StartsOn), D(e.EndsOn), e.CallNumber?.ToString(CultureInfo.InvariantCulture) })));

            Add("responsaveis.csv", Csv.Build(["Aluno", "CGM", "Responsável", "Parentesco", "Telefone", "E-mail", "Contato principal", "Contato autorizado", "Fundamento da autorização"],
                links.OrderBy(l => Who(l.StudentId).Name).Select(l => new[]
                {
                    Who(l.StudentId).Name, Who(l.StudentId).Code, l.Guardian!.FullName, l.Relationship.ToString(), l.Guardian.Phone, l.Guardian.Email,
                    l.IsPrimaryContact ? "sim" : "não", l.ContactAuthorized ? "sim" : "não", l.ContactBasis,
                })));

            Add("enderecos.csv", Csv.Build(["Aluno", "CGM", "Endereço", "Desde", "Até"],
                addresses.Select(a => new[] { Who(a.StudentId).Name, Who(a.StudentId).Code, a.Line, D(a.ValidFrom), D(a.ValidTo) })));

            Add("equipe.csv", Csv.Build(["Nome", "Função", "E-mail profissional", "Telefone profissional", "Ativo"],
                staff.Select(s => new[] { s.FullName, s.Function.ToString(), s.ProfessionalEmail, s.ProfessionalPhone, s.Active ? "sim" : "não" })));

            Add("ocorrencias.csv", Csv.Build(["Data do fato", "Aluno", "CGM", "Turma na data", "Categoria", "Situação", "Local", "Descrição", "Envolvidos", "Providências", "Encaminhamentos", "Observações", "Registrada em"],
                formal.Select(o => new[]
                {
                    T(o.OccurredAt), Who(o.StudentId).Name, Who(o.StudentId).Code, Room(o.ClassroomId), categories.GetValueOrDefault(o.CategoryId, ""), o.Status.ToString(),
                    o.Location, o.Description, o.Involved, o.ActionsTaken, o.Referrals, o.Notes, T(o.RegisteredAt),
                })));
        }

        return buffer.ToArray();
    }
}
