using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;
using Sinedu.Tenant.Notifications;
using Sinedu.Tenant.Occurrences;
using Sinedu.Tenant.ProtectedRecords;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;
using OccurrenceActor = Sinedu.Tenant.Occurrences.Actor;
using ProtectedActor = Sinedu.Tenant.ProtectedRecords.Actor;

namespace Sinedu.Tenant.Host.Dev;

/// <summary>
/// <c>seed-demo {escola} {ano}</c>: popula uma escola de DEMONSTRAÇÃO/HOMOLOGAÇÃO com dados 100% FICTÍCIOS,
/// usando os serviços reais (ocorrências com revisões, Saúde/Proteção cifrados com a chave da escola, convites pelo
/// login único, comunicações pela fila). Nunca rodar em escola real. Pode ser executado de novo: pula o que já existe.
///
/// 1. Ano corrente ativo (se a escola só tem o ano seguinte ativo, sem ocorrências, ele é corrigido para o corrente).
/// 2. Equipe fictícia e professores vinculados às turmas.
/// 3. Contas de teste por papel (convites no Mailpit), professor ligado à equipe e Consulta com duas turmas.
/// 4. Ocorrências do ano corrente até hoje, com acompanhamento, conclusão e cancelamento.
/// 5. Fichas de Saúde e registros de Proteção/Sigilo fictícios.
/// 6. Comunicações a responsáveis (endereços .invalid, que nunca são entregues fora do Mailpit).
/// 7. Próximo ano planejado: calendário, turmas da série seguinte e rematrícula prevista.
/// </summary>
internal static class DemoSeeder
{
    private const string Marker = "Dados fictícios de demonstração.";

    private static readonly string[] BloodTypes = ["O+", "A+", "B+", "AB+", "O-"];

    public static async Task RunAsync(IServiceProvider root, string slug, int year)
    {
        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var resolution = await sp.GetRequiredService<ITenantResolver>().ResolveAsync(slug);
        if (resolution.Status != TenantResolutionStatus.Ready) throw new InvalidOperationException($"Escola '{slug}' indisponível: {resolution.Status}.");
        sp.GetRequiredService<TenantScope>().Set(resolution.Tenant!);

        var school = sp.GetRequiredService<SchoolDbContext>();
        var students = sp.GetRequiredService<StudentsDbContext>();
        var access = sp.GetRequiredService<AccessDbContext>();
        var occurrences = sp.GetRequiredService<OccurrencesDbContext>();
        var random = new Random(20_26);
        void Log(string text) => Console.WriteLine($"[seed-demo] {text}");

        var current = await EnsureCurrentYearAsync(school, students, occurrences, year, Log);
        var director = await access.Members.AsNoTracking()
            .Where(m => m.Status == MemberStatus.Active && m.Roles.Any(r => r.Role == Roles.Director && r.RevokedAt == null))
            .OrderBy(m => m.CreatedAt).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("A escola precisa de um Diretor ativo (o primeiro diretor convidado pelo painel).");

        var staff = await SeedStaffAsync(school, current, random, Log);
        var accounts = await SeedAccountsAsync(sp, school, access, director, current, staff, Log);
        var authors = new[] { new OccurrenceActor(director.UserId, director.DisplayName) }
            .Concat(accounts.Pedagogue is { } p ? [new OccurrenceActor(p.UserId, p.DisplayName)] : Array.Empty<OccurrenceActor>()).ToArray();

        var formal = await SeedOccurrencesAsync(sp, occurrences, students, current, authors, random, Log);
        await SeedProtectedAsync(sp, students, current, new ProtectedActor(authors[^1].Id, authors[^1].Name), random, Log);
        await SeedCommunicationsAsync(sp, students, formal, authors[0], random, Log);
        await SeedNextYearAsync(school, students, current, year + 1, Log);
        Log("concluído.");
    }

    // ---------- 1. Ano corrente ----------

    private static async Task<SchoolYear> EnsureCurrentYearAsync(
        SchoolDbContext school, StudentsDbContext students, OccurrencesDbContext occurrences, int year, Action<string> log)
    {
        var current = await school.Years.Include(y => y.Terms).FirstOrDefaultAsync(y => y.Year == year);
        var next = await school.Years.Include(y => y.Terms).FirstOrDefaultAsync(y => y.Year == year + 1);

        // A escola foi criada com o ano seguinte por engano (sugestão padrão da tela): sem ocorrências, corrige para o corrente.
        if (current is null && next is { Status: SchoolYearStatus.Active } && !await occurrences.Occurrences.AnyAsync())
        {
            next.Year = year;
            next.StartsOn = next.StartsOn.AddYears(-1);
            next.EndsOn = next.EndsOn.AddYears(-1);
            foreach (var t in next.Terms)
            {
                t.StartsOn = t.StartsOn.AddYears(-1);
                t.EndsOn = t.EndsOn.AddYears(-1);
            }

            await school.SaveChangesAsync();
            await students.Enrollments.Where(e => e.SchoolYearId == next.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.StartsOn, e => e.StartsOn.AddYears(-1)));
            log($"ano {year + 1} corrigido para {year} (turmas, bimestres e matrículas).");
            return next;
        }

        if (current is null) throw new InvalidOperationException($"A escola não tem o ano {year}. Crie em Administração e importe os alunos antes.");
        if (current.Status != SchoolYearStatus.Active) throw new InvalidOperationException($"O ano {year} precisa estar ativo.");

        // Matrículas importadas depois do início do ano começam no início do ano (permite ocorrências desde fevereiro).
        await students.Enrollments.Where(e => e.SchoolYearId == current.Id && e.Status == EnrollmentStatus.Ativa && e.StartsOn > current.StartsOn)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.StartsOn, current.StartsOn));
        return current;
    }

    // ---------- 2. Equipe ----------

    private static readonly string[] Subjects = ["Língua Portuguesa", "Matemática", "Ciências", "História", "Geografia", "Inglês", "Arte", "Educação Física"];

    private static async Task<List<StaffMember>> SeedStaffAsync(SchoolDbContext school, SchoolYear year, Random random, Action<string> log)
    {
        if (await school.Staff.CountAsync(s => s.FullName.EndsWith(" (fictício)") || s.FullName.EndsWith(" (fictícia)")) > 0)
        {
            log("equipe fictícia já existe.");
            return await school.Staff.Where(s => s.Function == StaffFunction.Professor).OrderBy(s => s.FullName).ToListAsync();
        }

        var now = DateTimeOffset.UtcNow;
        StaffMember New(string name, StaffFunction f) => new()
        {
            Id = Guid.CreateVersion7(), FullName = name, Function = f, Active = true, CreatedAt = now,
            ProfessionalEmail = $"{Slug(name)}@demo.invalid", ProfessionalPhone = "(00) 3000-0000",
        };

        var team = new List<StaffMember>
        {
            New("Helena Prado Fontes (fictícia)", StaffFunction.Pedagogo),
            New("Rui Antunes Calado (fictício)", StaffFunction.Pedagogo),
            New("Marta Lins Seabra (fictícia)", StaffFunction.Secretaria),
        };
        string[] teacherNames =
        [
            "Ana Beatriz Moura", "Bruno Teixeira Lago", "Carla Mendes Viana", "Diego Farias Neto", "Elisa Rocha Campos", "Fábio Duarte Leme",
            "Gabriela Tavares Sol", "Hugo Pacheco Reis", "Isabela Nunes Prates", "João Vitor Aranha", "Karina Salles Bento", "Lucas Barreto Fiuza",
        ];
        var teachers = teacherNames.Select((n, i) => New($"{n} ({(i % 2 == 0 ? "fictícia" : "fictício")})", StaffFunction.Professor)).ToList();
        school.Staff.AddRange(team);
        school.Staff.AddRange(teachers);

        var classrooms = await school.Classrooms.Where(c => c.SchoolYearId == year.Id && c.Status == ClassroomStatus.Active).ToListAsync();
        foreach (var c in classrooms)
        {
            foreach (var subject in Subjects.OrderBy(_ => random.Next()).Take(4))
            {
                var teacher = teachers[Math.Abs(HashCode.Combine(c.Id, subject)) % teachers.Count];
                school.TeacherAssignments.Add(new TeacherAssignment { Id = Guid.CreateVersion7(), StaffMemberId = teacher.Id, ClassroomId = c.Id, Subject = subject, StartsOn = year.StartsOn });
            }
        }

        await school.SaveChangesAsync();
        log($"equipe: {team.Count + teachers.Count} profissionais e {classrooms.Count * 4} vínculos professor↔turma.");
        return teachers;
    }

    // ---------- 3. Contas de teste ----------

    private sealed record Accounts(Member? Pedagogue);

    private static async Task<Accounts> SeedAccountsAsync(
        IServiceProvider sp, SchoolDbContext school, AccessDbContext access, Member director, SchoolYear year, List<StaffMember> teachers, Action<string> log)
    {
        var service = sp.GetRequiredService<AccessService>();
        var slug = sp.GetRequiredService<TenantScope>().Current.Slug;
        (string Role, string Email, string Name)[] wanted =
        [
            (Roles.Pedagogue, $"pedagogia.{slug}@demo.invalid", "Pedagoga de Teste (fictícia)"),
            (Roles.TechAdmin, $"secretaria.{slug}@demo.invalid", "Admin Técnico de Teste (fictício)"),
            (Roles.Teacher, $"professora.{slug}@demo.invalid", "Professora de Teste (fictícia)"),
            (Roles.ReadOnly, $"consulta.{slug}@demo.invalid", "Consulta de Teste (fictícia)"),
        ];

        Member? pedagogue = null;
        foreach (var (role, email, name) in wanted)
        {
            var existing = await access.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Email == email);
            Guid? userId = existing?.UserId;
            if (existing is null)
            {
                var (result, id) = await service.InviteAsync(director.UserId, [Roles.Director], email, name, role, CancellationToken.None);
                if (result != AccessChangeResult.Done) throw new InvalidOperationException($"Convite de {role} falhou: {result}.");
                userId = id;
                log($"convite enviado: {name} <{email}> ({role}).");
            }

            if (role == Roles.Pedagogue) pedagogue = await access.Members.AsNoTracking().FirstAsync(m => m.UserId == userId);

            // Professora de teste = primeira professora fictícia da equipe (vê só as turmas dela).
            if (role == Roles.Teacher && teachers.Count > 0 && !await school.Staff.AnyAsync(s => s.UserId == userId))
            {
                var teacher = await school.Staff.FirstAsync(s => s.Id == teachers[0].Id);
                teacher.UserId ??= userId;
                await school.SaveChangesAsync();
            }

            // Consulta de teste enxerga só duas turmas (D10).
            if (role == Roles.ReadOnly && !await access.ClassroomGrants.AnyAsync(g => g.UserId == userId && g.RevokedAt == null))
            {
                var two = await school.Classrooms.Where(c => c.SchoolYearId == year.Id).OrderBy(c => c.Id).Take(2).Select(c => c.Id).ToListAsync();
                access.ClassroomGrants.AddRange(two.Select(c => new ClassroomAccessGrant { Id = Guid.CreateVersion7(), UserId = userId!.Value, ClassroomId = c, GrantedBy = director.UserId, GrantedAt = DateTimeOffset.UtcNow }));
                await access.SaveChangesAsync();
            }
        }

        return new Accounts(pedagogue);
    }

    // ---------- 4. Ocorrências ----------

    private static readonly (string Category, string Description, string? Actions)[] Facts =
    [
        ("Indisciplina", "Durante a aula, o estudante conversou repetidamente e não atendeu às orientações do professor.", "Conversa individual com o estudante ao final da aula."),
        ("Indisciplina", "Saiu da sala sem autorização durante a atividade avaliativa.", "Retorno à sala e registro com a coordenação."),
        ("Atraso", "Chegou à escola após o início da primeira aula pela terceira vez na semana.", "Contato com a família para combinar horários."),
        ("Falta de material", "Compareceu sem o material da disciplina em duas aulas seguidas.", "Orientação sobre organização do material."),
        ("Conflito entre estudantes", "Desentendimento verbal com colega no intervalo, sem agressão física.", "Mediação com os dois estudantes na pedagogia."),
        ("Uso de celular", "Uso de celular durante a explicação, em desacordo com o combinado da turma.", "Aparelho guardado até o fim da aula."),
        ("Dano ao patrimônio", "Carteira riscada com caneta durante a aula.", "Estudante limpou a carteira com apoio da equipe."),
    ];

    private static async Task<List<Guid>> SeedOccurrencesAsync(
        IServiceProvider sp, OccurrencesDbContext db, StudentsDbContext students, SchoolYear year, OccurrenceActor[] authors, Random random, Action<string> log)
    {
        if (await db.Occurrences.AnyAsync(o => o.Description.Contains(Marker)))
        {
            log("ocorrências fictícias já existem.");
            return await db.Occurrences.Where(o => o.Description.Contains(Marker) && o.Status != OccurrenceStatus.Rascunho && o.Status != OccurrenceStatus.Cancelada)
                .Select(o => o.Id).ToListAsync();
        }

        var service = sp.GetRequiredService<OccurrenceService>();
        var categories = await db.Categories.AsNoTracking().Where(c => c.IsActive).ToListAsync();
        var severities = await db.Severities.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();
        var pool = await students.Enrollments.AsNoTracking().Where(e => e.SchoolYearId == year.Id && e.Status == EnrollmentStatus.Ativa)
            .Select(e => e.StudentId).OrderBy(id => id).ToListAsync();
        if (pool.Count == 0 || categories.Count == 0) throw new InvalidOperationException("Sem alunos matriculados ou sem categorias.");

        // Alguns alunos concentram mais registros (como na vida real); a maioria não tem nenhum.
        var frequent = pool.OrderBy(_ => random.Next()).Take(Math.Max(10, pool.Count / 40)).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var last = today < year.EndsOn ? today.AddDays(-1) : year.EndsOn;
        var span = last.DayNumber - year.StartsOn.DayNumber;
        var formal = new List<Guid>();
        var total = Math.Clamp(pool.Count / 6, 40, 220);

        for (var i = 0; i < total; i++)
        {
            var studentId = random.NextDouble() < 0.55 ? frequent[random.Next(frequent.Count)] : pool[random.Next(pool.Count)];
            var day = year.StartsOn.AddDays(random.Next(span + 1));
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(day.DayOfWeek == DayOfWeek.Saturday ? 2 : 1);
            if (day > last) day = last;
            var when = new DateTimeOffset(day.ToDateTime(new TimeOnly(7 + random.Next(9), random.Next(60))), TimeSpan.FromHours(-3));
            var fact = Facts[random.Next(Facts.Length)];
            var category = categories.FirstOrDefault(c => c.Name.Contains(fact.Category, StringComparison.OrdinalIgnoreCase)) ?? categories[random.Next(categories.Count)];
            var author = authors[random.Next(authors.Length)];
            var draft = random.NextDouble() < 0.05;

            var input = new OccurrenceInput(studentId, when, category.Id, random.NextDouble() < 0.6 && severities.Count > 0 ? severities[random.Next(severities.Count)].Id : null,
                random.NextDouble() < 0.5 ? "Sala de aula" : "Pátio", $"{fact.Description} {Marker}", null, fact.Actions, null, null);
            Guid id;
            try
            {
                id = await service.CreateAsync(input, register: !draft, author, CancellationToken.None);
            }
            catch (Exception ex) when (ex is OccurrenceConflictException or InvalidOperationException)
            {
                continue; // aluno sem matrícula na data, etc.
            }

            if (draft) continue;
            var roll = random.NextDouble();
            if (roll < 0.30)
            {
                await Step(service, db, id, OccurrenceStatus.EmAcompanhamento, null, author);
                if (random.NextDouble() < 0.6) await Step(service, db, id, OccurrenceStatus.Concluida, "Família compareceu e o combinado foi cumprido.", author);
            }
            else if (roll < 0.36)
            {
                await Step(service, db, id, OccurrenceStatus.Cancelada, "Registro duplicado por engano.", author);
                continue;
            }

            formal.Add(id);
        }

        log($"ocorrências: {formal.Count} formalizadas, além de rascunhos e canceladas, de {year.StartsOn:dd/MM} a {last:dd/MM/yyyy}.");
        return formal;
    }

    private static async Task Step(OccurrenceService service, OccurrencesDbContext db, Guid id, OccurrenceStatus target, string? reason, OccurrenceActor actor)
    {
        var version = await db.Occurrences.AsNoTracking().Where(o => o.Id == id).Select(o => o.Version).FirstAsync();
        await service.ChangeStatusAsync(id, target, reason, version, actor, CancellationToken.None);
    }

    // ---------- 5. Saúde e Proteção ----------

    private static async Task SeedProtectedAsync(IServiceProvider sp, StudentsDbContext students, SchoolYear year, ProtectedActor actor, Random random, Action<string> log)
    {
        var protectedDb = sp.GetRequiredService<ProtectedDbContext>();
        if (await protectedDb.HealthRecords.AnyAsync())
        {
            log("Saúde/Proteção já têm registros: nada a fazer.");
            return;
        }

        var service = sp.GetRequiredService<ProtectedService>();
        var pool = await students.Enrollments.AsNoTracking().Where(e => e.SchoolYearId == year.Id && e.Status == EnrollmentStatus.Ativa)
            .Select(e => e.StudentId).OrderBy(id => id).ToListAsync();
        var chosen = pool.OrderBy(_ => random.Next()).Take(Math.Min(30, pool.Count)).ToList();
        string[] allergies = ["Alergia a amendoim (fictícia).", "Alergia a dipirona (fictícia).", "Intolerância à lactose (fictícia).", "Sem alergias informadas."];
        string[] conditions = ["Asma leve, usa bombinha quando necessário (fictício).", "Usa óculos para leitura (fictício).", "Nenhuma condição informada."];

        foreach (var (studentId, i) in chosen.Select((s, i) => (s, i)))
        {
            var data = new HealthData(
                BloodType: random.NextDouble() < 0.4 ? BloodTypes[random.Next(BloodTypes.Length)] : null,
                Allergies: allergies[random.Next(allergies.Length)],
                Conditions: conditions[random.Next(conditions.Length)],
                Medications: random.NextDouble() < 0.3 ? "Medicação de uso contínuo informada pela família (fictícia)." : null,
                Restrictions: random.NextDouble() < 0.2 ? "Evitar atividade física intensa sem liberação (fictício)." : null,
                FirstAidInstructions: "Em caso de mal-estar, avisar a família e a direção (fictício).",
                EmergencyContacts: [new EmergencyContact("Contato de Emergência Fictício", "(00) 90000-0000", "Responsável")],
                Source: "Ficha preenchida pela família na matrícula (fictícia).",
                Notes: Marker);
            var review = i % 5 == 0 ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(random.Next(5, 40)) : (DateOnly?)null;
            await service.SaveHealthAsync(studentId, data, review, Marker, null, actor, CancellationToken.None, action: "health_created");
        }

        var candidates = pool.Count > 60 ? pool.Except(chosen).ToList() : pool;
        var protectedStudents = candidates.OrderBy(_ => random.Next()).Take(Math.Min(8, candidates.Count)).ToList();
        (ProtectionKind Kind, string Title)[] kinds =
        [
            (ProtectionKind.AutorizacaoRetirada, "Autorização de retirada por terceiro (fictícia)"),
            (ProtectionKind.Guarda, "Guarda unilateral (fictícia)"),
            (ProtectionKind.MedidaProtetiva, "Medida protetiva — restrição de aproximação (fictícia)"),
        ];
        foreach (var (studentId, i) in protectedStudents.Select((s, i) => (s, i)))
        {
            var (kind, title) = kinds[i % kinds.Length];
            var data = new ProtectionData(
                Title: title, Description: Marker, Authority: kind == ProtectionKind.AutorizacaoRetirada ? "Responsável legal" : "Vara da Infância (fictícia)",
                DocumentNumber: $"0000{i:D3}-00.2026.8.00.0000", IssuedOn: year.StartsOn.AddDays(10 + i), ValidFrom: year.StartsOn.AddDays(10 + i),
                Basis: "Documento apresentado à secretaria (fictício).", Verification: VerificationLevel.Documentado,
                AuthorizedPeople: kind == ProtectionKind.AutorizacaoRetirada ? [new PickupPerson("Tia Fictícia", null, "Tia", null)] : null,
                UnauthorizedPeople: kind == ProtectionKind.MedidaProtetiva ? [new PickupPerson("Pessoa Impedida Fictícia", null, null, "Não permitir a retirada")] : null,
                Notes: Marker);
            var validUntil = i % 3 == 0 ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15 + i) : year.EndsOn;
            await service.CreateProtectionAsync(studentId, kind, validUntil, data, Marker, actor, CancellationToken.None);
        }

        log($"Saúde: {chosen.Count} fichas; Proteção/Sigilo: {protectedStudents.Count} registros (cifrados com a chave da escola).");
    }

    // ---------- 6. Comunicações ----------

    private static async Task SeedCommunicationsAsync(IServiceProvider sp, StudentsDbContext students, List<Guid> formal, OccurrenceActor actor, Random random, Action<string> log)
    {
        var notifications = sp.GetRequiredService<NotificationsDbContext>();
        if (await notifications.Notifications.AnyAsync() || formal.Count == 0)
        {
            log("comunicações já existem (ou não há ocorrências): nada a fazer.");
            return;
        }

        var service = sp.GetRequiredService<NotificationService>();
        var occurrences = sp.GetRequiredService<OccurrencesDbContext>();
        var template = await notifications.Templates.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).FirstOrDefaultAsync();
        if (template is null) return;

        var sent = 0;
        foreach (var occurrenceId in formal.OrderBy(_ => random.Next()).Take(15))
        {
            var studentId = await occurrences.Occurrences.AsNoTracking().Where(o => o.Id == occurrenceId).Select(o => o.StudentId).FirstAsync();
            var links = await students.StudentGuardians.Include(g => g.Guardian).Where(g => g.StudentId == studentId).ToListAsync();
            if (links.Count == 0) continue;

            // Responsável fictício com e-mail que nunca é entregue (.invalid) e autorização registrada.
            var link = links[0];
            link.Guardian!.Email ??= $"responsavel.{Guid.NewGuid():N}"[..20] + "@demo.invalid";
            if (!link.ContactAuthorized)
            {
                link.ContactAuthorized = true;
                link.ContactBasis = "Autorização fictícia (dados de demonstração).";
                link.ContactAuthorizedAt = DateTimeOffset.UtcNow;
                link.ContactAuthorizedBy = actor.Id;
            }

            await students.SaveChangesAsync();
            var result = await service.AuthorizeAsync(occurrenceId, template.Id, [link.GuardianId], Guid.NewGuid(), false, actor, CancellationToken.None);
            if (result is { Count: > 0 }) sent++;
        }

        log($"comunicações: {sent} enviadas para responsáveis fictícios (Mailpit).");
    }

    // ---------- 7. Próximo ano ----------

    private static async Task SeedNextYearAsync(SchoolDbContext school, StudentsDbContext students, SchoolYear current, int nextYear, Action<string> log)
    {
        if (await school.Years.AnyAsync(y => y.Year == nextYear))
        {
            log($"ano {nextYear} já existe.");
            return;
        }

        var starts = new DateOnly(nextYear, 2, 1);
        var year = new SchoolYear { Id = Guid.CreateVersion7(), Year = nextYear, StartsOn = starts, EndsOn = new DateOnly(nextYear, 12, 17), Status = SchoolYearStatus.Planned };
        school.Years.Add(year);
        (DateOnly From, DateOnly To)[] terms = [(starts, new(nextYear, 4, 30)), (new(nextYear, 5, 3), new(nextYear, 7, 9)), (new(nextYear, 7, 26), new(nextYear, 9, 30)), (new(nextYear, 10, 1), year.EndsOn)];
        school.Terms.AddRange(terms.Select((t, i) => new AcademicTerm { Id = Guid.CreateVersion7(), SchoolYearId = year.Id, Number = i + 1, Name = $"{i + 1}º bimestre", StartsOn = t.From, EndsOn = t.To }));

        // Série seguinte pela ordem das séries; 3ª série do médio conclui e não é rematriculada.
        var grades = await school.Grades.AsNoTracking().OrderBy(g => g.SortOrder).ToListAsync();
        var nextGrade = grades.Zip(grades.Skip(1)).ToDictionary(p => p.First.Id, p => p.Second.Id);
        var classrooms = await school.Classrooms.AsNoTracking().Where(c => c.SchoolYearId == current.Id && c.Status == ClassroomStatus.Active).ToListAsync();
        var map = new Dictionary<Guid, Guid>();
        foreach (var c in classrooms.Where(c => nextGrade.ContainsKey(c.GradeId)))
        {
            var created = new Classroom { Id = Guid.CreateVersion7(), SchoolYearId = year.Id, GradeId = nextGrade[c.GradeId], Section = c.Section, Shift = c.Shift, Capacity = c.Capacity, Status = ClassroomStatus.Active };
            school.Classrooms.Add(created);
            map[c.Id] = created.Id;
        }

        await school.SaveChangesAsync();

        var active = await students.Enrollments.AsNoTracking().Where(e => e.SchoolYearId == current.Id && e.Status == EnrollmentStatus.Ativa).ToListAsync();
        var planned = active.Where(e => map.ContainsKey(e.ClassroomId)).Select(e => new Enrollment
        {
            Id = Guid.CreateVersion7(), StudentId = e.StudentId, ClassroomId = map[e.ClassroomId], SchoolYearId = year.Id,
            Status = EnrollmentStatus.Prevista, StartsOn = starts, CallNumber = e.CallNumber,
        }).ToList();
        students.Enrollments.AddRange(planned);
        await students.SaveChangesAsync();
        log($"ano {nextYear} planejado: 4 bimestres, {map.Count} turmas e {planned.Count} rematrículas previstas ({active.Count - planned.Count} concluintes).");
    }

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsAsciiLetterLower(c) || c == ' ').ToArray()).Trim().Replace(' ', '.');
}
