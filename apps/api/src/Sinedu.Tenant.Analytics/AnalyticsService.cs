using Sinedu.Tenant.Notifications;
using Sinedu.Tenant.Occurrences;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.Analytics;

public sealed record AnalyticsQuery(Guid? YearId, int? Term, DateOnly? From, DateOnly? To, Guid? ClassroomId, Guid? CategoryId);

public sealed record Period(string Kind, string Label, DateOnly Start, DateOnly End, string PreviousLabel, DateOnly PreviousStart, DateOnly PreviousEnd, Guid YearId, int? Term);

public sealed record LabelValue(string Label, int Value);

public sealed record ClassroomRow(Guid ClassroomId, string Classroom, string Shift, int Enrolled, int Occurrences, double? RatePer100);

public sealed record WeekRow(string Week, int? Current, int? Previous);

public sealed record StatusTotals(int Registered, int FollowUp, int Concluded);

public sealed record FollowUpRow(Guid Id, string Student, string Classroom, string Category, DateTimeOffset OccurredAt, OccurrenceStatus Status, string Owner);

public sealed record Dashboard(
    Period Period, int Enrolled, int EnrolledPrevious, int Occurrences, int OccurrencesPrevious, int StudentsWithOccurrence,
    int StudentsWithOccurrencePrevious, StatusTotals Status, NotificationTotals Notifications, IReadOnlyList<WeekRow> Weekly,
    IReadOnlyList<LabelValue> ByCategory, IReadOnlyList<ClassroomRow> ByClassroom, IReadOnlyList<FollowUpRow> FollowUps, string Method);

public sealed record StudentRow(Guid StudentId, string Name, string? Code, string Classroom, int Occurrences, int Open);

public sealed record FilterOptions(
    IReadOnlyList<YearInfo> Years, Guid? YearId, int? CurrentTerm, IReadOnlyList<ClassroomInfo> Classrooms, IReadOnlyList<LabelId> Categories);

public sealed record LabelId(Guid Id, string Name);

public sealed class AnalyticsValidationException(string message) : Exception(message);

/// <summary>
/// Indicadores da seção 9 da spec. Regras:
/// - Contam só ocorrências registradas ou posteriores (rascunho e cancelada ficam fora — P4), pela DATA DO FATO.
/// - Turma é a gravada na ocorrência (turma da época), não a atual do aluno.
/// - Denominador: alunos com matrícula não cancelada na turma em algum dia do período (matrícula variável conta
///   em cada turma por onde o aluno passou; no total da escola, uma vez).
/// - Nada de Saúde/Proteção: este módulo não tem acesso a eles. Nada de ranking de alunos.
/// </summary>
internal sealed class AnalyticsService(
    ISchoolCalendar calendar, IStudentRegistry students, IOccurrenceStatistics occurrences, INotificationStatistics notifications, TimeProvider clock)
{
    public const string Method =
        "Contam as ocorrências registradas, em acompanhamento ou concluídas, pela data do fato e pela turma da época. " +
        "Rascunhos e canceladas ficam fora. Matriculados = alunos com matrícula ativa na turma em algum dia do período; " +
        "quem trocou de turma conta em cada turma, e uma vez no total. Taxa = ocorrências ÷ matriculados × 100.";

    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), SaoPaulo).DateTime);

    public async Task<FilterOptions> OptionsAsync(Guid? yearId, CancellationToken ct)
    {
        var years = await calendar.GetYearsAsync(ct);
        var year = years.FirstOrDefault(y => y.Id == yearId) ?? years.FirstOrDefault(y => y.IsActive) ?? (years.Count > 0 ? years[0] : null);
        var classrooms = year is null ? [] : await calendar.GetClassroomsAsync(year.Id, ct);
        var current = year?.Terms.FirstOrDefault(t => t.StartsOn <= Today && t.EndsOn >= Today)?.Number
            ?? year?.Terms.LastOrDefault(t => t.StartsOn <= Today)?.Number ?? (year is { Terms.Count: > 0 } ? year.Terms[0].Number : null);
        var categories = (await occurrences.CategoriesAsync(ct)).Select(c => new LabelId(c.Id, c.Name)).ToList();
        return new FilterOptions(years, year?.Id, current, classrooms, categories);
    }

    public async Task<Period> ResolveAsync(AnalyticsQuery q, CancellationToken ct)
    {
        var years = await calendar.GetYearsAsync(ct);
        var year = years.FirstOrDefault(y => y.Id == q.YearId) ?? years.FirstOrDefault(y => y.IsActive) ?? (years.Count > 0 ? years[0] : null)
            ?? throw new AnalyticsValidationException("Cadastre um ano letivo para ver os indicadores.");

        if (q.From is { } from && q.To is { } to)
        {
            if (to < from || to.DayNumber - from.DayNumber > 400)
            {
                throw new AnalyticsValidationException("Período inválido (máximo de 400 dias).");
            }

            var days = to.DayNumber - from.DayNumber + 1;
            var prevEnd = from.AddDays(-1);
            var prevStart = prevEnd.AddDays(-(days - 1));
            return new Period("custom", $"{from:dd/MM/yyyy} a {to:dd/MM/yyyy}", from, to, "período anterior", prevStart, prevEnd, year.Id, null);
        }

        if (year.Terms.Count == 0)
        {
            throw new AnalyticsValidationException("O ano letivo não tem bimestres cadastrados.");
        }

        var term = year.Terms.FirstOrDefault(t => t.Number == q.Term)
            ?? year.Terms.FirstOrDefault(t => t.StartsOn <= Today && t.EndsOn >= Today)
            ?? year.Terms.LastOrDefault(t => t.StartsOn <= Today) ?? year.Terms[0];
        var previous = year.Terms.LastOrDefault(t => t.Number < term.Number);
        var previousLabel = previous?.Name;
        if (previous is null)
        {
            var priorYear = years.FirstOrDefault(y => y.Year == year.Year - 1);
            previous = priorYear is { Terms.Count: > 0 } ? priorYear.Terms[^1] : null;
            previousLabel = previous is null ? null : $"{previous.Name} de {priorYear!.Year}";
        }

        // Sem bimestre anterior: compara com o mesmo número de dias antes do início (normalmente férias, quase sempre zero).
        var length = term.EndsOn.DayNumber - term.StartsOn.DayNumber;
        return new Period("term", term.Name, term.StartsOn, term.EndsOn,
            previousLabel ?? "período anterior", previous?.StartsOn ?? term.StartsOn.AddDays(-length - 1), previous?.EndsOn ?? term.StartsOn.AddDays(-1),
            year.Id, term.Number);
    }

    public async Task<Dashboard> DashboardAsync(AnalyticsQuery q, CancellationToken ct)
    {
        var period = await ResolveAsync(q, ct);
        var classrooms = await calendar.GetClassroomsAsync(period.YearId, ct);
        var scope = q.ClassroomId is { } only ? classrooms.Where(c => c.Id == only).ToList() : classrooms.ToList();
        var ids = scope.Select(c => c.Id).ToList();

        var current = Filter(await occurrences.CountableAsync(Utc(period.Start), Utc(period.End.AddDays(1)), ct), q, ids);
        var previous = Filter(await occurrences.CountableAsync(Utc(period.PreviousStart), Utc(period.PreviousEnd.AddDays(1)), ct), q, ids);
        var enrolled = await students.CountEnrolledAsync(period.Start, period.End, ids, ct);
        var enrolledPrevious = await students.CountEnrolledAsync(period.PreviousStart, period.PreviousEnd, ids, ct);
        var notificationTotals = await notifications.TotalsAsync(current.Select(o => o.Id).ToList(), ct);

        var byClassroom = scope.Select(c =>
        {
            var e = enrolled.ByClassroom.GetValueOrDefault(c.Id);
            var n = current.Count(o => o.ClassroomId == c.Id);
            return new ClassroomRow(c.Id, c.Name, c.Shift, e, n, e > 0 ? Math.Round(n * 100.0 / e, 1) : null);
        }).Where(c => c.Enrolled > 0 || c.Occurrences > 0).ToList();

        var open = current.Where(o => o.Status is OccurrenceStatus.Registrada or OccurrenceStatus.EmAcompanhamento)
            .OrderByDescending(o => o.OccurredAt).Take(8).ToList();
        var names = (await students.GetAsync(open.Select(o => o.StudentId).Distinct().ToList(), ct)).ToDictionary(s => s.Id, s => s.FullName);
        var roomNames = classrooms.ToDictionary(c => c.Id, c => c.Name);

        return new Dashboard(
            period, enrolled.DistinctStudents, enrolledPrevious.DistinctStudents, current.Count, previous.Count,
            current.Select(o => o.StudentId).Distinct().Count(), previous.Select(o => o.StudentId).Distinct().Count(),
            new StatusTotals(
                current.Count(o => o.Status == OccurrenceStatus.Registrada),
                current.Count(o => o.Status == OccurrenceStatus.EmAcompanhamento),
                current.Count(o => o.Status == OccurrenceStatus.Concluida)),
            notificationTotals,
            Weekly(period, current, previous),
            current.GroupBy(o => o.Category).Select(g => new LabelValue(g.Key, g.Count())).OrderByDescending(c => c.Value).ThenBy(c => c.Label).ToList(),
            byClassroom,
            open.Select(o => new FollowUpRow(o.Id, names.GetValueOrDefault(o.StudentId, "Aluno"), roomNames.GetValueOrDefault(o.ClassroomId, "—"), o.Category, o.OccurredAt, o.Status, o.CreatedByName)).ToList(),
            Method);
    }

    /// <summary>Ocorrências por aluno (consulta restrita, nunca exportada). Ordem alfabética: não é ranking.</summary>
    public async Task<(Period Period, IReadOnlyList<StudentRow> Rows)> ByStudentAsync(AnalyticsQuery q, CancellationToken ct)
    {
        var period = await ResolveAsync(q, ct);
        var classrooms = await calendar.GetClassroomsAsync(period.YearId, ct);
        var ids = (q.ClassroomId is { } only ? classrooms.Where(c => c.Id == only) : classrooms).Select(c => c.Id).ToList();
        var current = Filter(await occurrences.CountableAsync(Utc(period.Start), Utc(period.End.AddDays(1)), ct), q, ids);
        var people = (await students.GetAsync(current.Select(o => o.StudentId).Distinct().ToList(), ct)).ToDictionary(s => s.Id);
        var rooms = classrooms.ToDictionary(c => c.Id, c => c.Name);
        var rows = current.GroupBy(o => o.StudentId).Select(g =>
        {
            var last = g.OrderByDescending(o => o.OccurredAt).First();
            var p = people.GetValueOrDefault(g.Key);
            return new StudentRow(g.Key, p?.FullName ?? "Aluno", p?.InstitutionalCode, rooms.GetValueOrDefault(last.ClassroomId, "—"), g.Count(),
                g.Count(o => o.Status is OccurrenceStatus.Registrada or OccurrenceStatus.EmAcompanhamento));
        }).OrderBy(r => r.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), true)).ToList();
        return (period, rows);
    }

    private static List<OccurrenceFact> Filter(IReadOnlyList<OccurrenceFact> list, AnalyticsQuery q, List<Guid> classroomIds) =>
        list.Where(o => classroomIds.Contains(o.ClassroomId) && (q.CategoryId is null || o.CategoryId == q.CategoryId)).ToList();

    private List<WeekRow> Weekly(Period p, List<OccurrenceFact> current, List<OccurrenceFact> previous)
    {
        var weeks = (p.End.DayNumber - p.Start.DayNumber) / 7 + 1;
        var previousWeeks = (p.PreviousEnd.DayNumber - p.PreviousStart.DayNumber) / 7 + 1;
        int Week(DateTimeOffset at, DateOnly start) => (Local(at).DayNumber - start.DayNumber) / 7;
        return Enumerable.Range(0, weeks).Select(i =>
        {
            var weekStart = p.Start.AddDays(i * 7);
            int? cur = weekStart > Today ? null : current.Count(o => Week(o.OccurredAt, p.Start) == i);
            int? prev = i < previousWeeks ? previous.Count(o => Week(o.OccurredAt, p.PreviousStart) == i) : null;
            return new WeekRow($"Sem {i + 1}", cur, prev);
        }).ToList();
    }

    private static DateOnly Local(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, SaoPaulo).DateTime);

    private static DateTimeOffset Utc(DateOnly localDate)
    {
        var local = localDate.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, SaoPaulo.GetUtcOffset(local)).ToUniversalTime();
    }
}
