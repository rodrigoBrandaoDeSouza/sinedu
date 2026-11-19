using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.Tenant.Imports;
using Sinedu.Tenant.Notifications;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.Host.Dev;

/// <summary>
/// <c>anonymize-students {escola}</c>: troca, de forma IRREVERSÍVEL, os dados pessoais de alunos e responsáveis de uma
/// escola de homologação/demonstração por dados fictícios — para quando dados reais foram importados por engano.
/// Não guarda nenhuma correspondência com os valores originais e não imprime nenhum valor (só contagens).
///
/// - Alunos: nome, nome social, nascimento (mesmo ano, dia sorteado), CGM e e-mail institucional.
/// - Responsáveis: nome, documento (removido), telefone e e-mail fictícios.
/// - Endereços: endereço fictício.
/// - Importações: apaga as linhas dos lotes (células brutas da planilha e diferenças).
/// - Comunicações: apaga as enviadas (destinatário e texto renderizado com nomes).
/// Depois disso, rode VACUUM FULL nas tabelas afetadas para eliminar as versões antigas das linhas.
/// </summary>
internal static class StudentAnonymizer
{
    private static readonly string[] FirstNames =
    [
        "ANA", "BEATRIZ", "CAMILA", "DANIELA", "ELISA", "FERNANDA", "GABRIELA", "HELENA", "ISADORA", "JULIANA", "LARISSA", "MARIANA",
        "NATALIA", "OLIVIA", "PAULA", "RAFAELA", "SOFIA", "TATIANE", "VALENTINA", "YASMIN", "ARTHUR", "BERNARDO", "CAIO", "DAVI",
        "ENZO", "FELIPE", "GUSTAVO", "HEITOR", "IGOR", "JOAQUIM", "LEONARDO", "MATEUS", "NICOLAS", "OTAVIO", "PEDRO", "RAFAEL",
        "SAMUEL", "THIAGO", "VICENTE", "VITOR",
    ];

    private static readonly string[] Surnames =
    [
        "ALMEIDA", "BARROS", "CARDOSO", "DIAS", "ESTEVES", "FARIAS", "GOMES", "HOLANDA", "IZIDORO", "JARDIM", "LOPES", "MACHADO",
        "NOGUEIRA", "OLIVEIRA", "PEREIRA", "QUEIROZ", "RAMOS", "SANTANA", "TEIXEIRA", "URBANO", "VIEIRA", "XAVIER", "ZANETTI", "MOURA",
        "CASTRO", "FREITAS", "MONTEIRO", "PRADO", "REZENDE", "SIQUEIRA",
    ];

    private static readonly string[] Streets = ["RUA DAS ACÁCIAS", "RUA DOS IPÊS", "AVENIDA DAS PALMEIRAS", "RUA DO SABIÁ", "RUA DAS ORQUÍDEAS", "TRAVESSA DO BEM-TE-VI"];

    public static async Task RunAsync(IServiceProvider root, string slug)
    {
        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var resolution = await sp.GetRequiredService<ITenantResolver>().ResolveAsync(slug);
        if (resolution.Status != TenantResolutionStatus.Ready) throw new InvalidOperationException($"Escola '{slug}' indisponível: {resolution.Status}.");
        sp.GetRequiredService<TenantScope>().Set(resolution.Tenant!);

        var students = sp.GetRequiredService<StudentsDbContext>();
        var imports = sp.GetRequiredService<ImportsDbContext>();
        var notifications = sp.GetRequiredService<NotificationsDbContext>();
        void Log(string text) => Console.WriteLine($"[anonymize] {text}");

        // Sem semente: não existe como reproduzir nem desfazer a troca.
        string Name() =>
            $"{FirstNames[RandomNumberGenerator.GetInt32(FirstNames.Length)]} {Surnames[RandomNumberGenerator.GetInt32(Surnames.Length)]} {Surnames[RandomNumberGenerator.GetInt32(Surnames.Length)]}";
        string Digits(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => (char)('0' + RandomNumberGenerator.GetInt32(10))));

        // Alunos
        var usedCodes = new HashSet<string>(StringComparer.Ordinal);
        var all = await students.Students.ToListAsync();
        foreach (var s in all)
        {
            var name = Name();
            s.FullName = name;
            s.SocialName = null;
            s.SearchName = Student.ToSearch(name);
            if (s.BirthDate is { } b)
            {
                s.BirthDate = new DateOnly(b.Year, 1, 1).AddDays(RandomNumberGenerator.GetInt32(DateTime.IsLeapYear(b.Year) ? 366 : 365));
            }

            if (s.InstitutionalCode is not null)
            {
                string code;
                do code = "9" + Digits(9); while (!usedCodes.Add(code));
                s.InstitutionalCode = code;
            }

            if (s.InstitutionalEmail is not null)
            {
                s.InstitutionalEmail = $"{name.Split(' ')[0].ToLowerInvariant()}.{Digits(5)}@aluno.demo.invalid";
            }
        }

        await students.SaveChangesAsync();
        Log($"alunos: {all.Count} anonimizados.");

        // Responsáveis
        var guardians = await students.Guardians.ToListAsync();
        foreach (var g in guardians)
        {
            g.FullName = Name();
            g.Document = null;
            g.Phone = g.Phone is null ? null : $"(00) 9{Digits(4)}-{Digits(4)}";
            g.Email = g.Email is null ? null : $"responsavel.{Digits(6)}@demo.invalid";
        }

        await students.SaveChangesAsync();
        Log($"responsáveis: {guardians.Count} anonimizados.");

        // Endereços
        var addresses = await students.Addresses.ToListAsync();
        foreach (var a in addresses)
        {
            a.Line = $"{Streets[RandomNumberGenerator.GetInt32(Streets.Length)]}, {RandomNumberGenerator.GetInt32(10, 2000)} - BAIRRO EXEMPLO - CIDADE FICTÍCIA";
        }

        await students.SaveChangesAsync();
        Log($"endereços: {addresses.Count} anonimizados.");

        // Importações: células brutas e diferenças guardavam os valores originais.
        var rows = await imports.Rows.ExecuteDeleteAsync();
        Log($"importações: {rows} linhas brutas apagadas (os lotes ficam só com o resumo).");

        // Comunicações já enviadas guardam nome, e-mail e texto com nomes.
        var attempts = await notifications.Attempts.ExecuteDeleteAsync();
        var sent = await notifications.Notifications.ExecuteDeleteAsync();
        Log($"comunicações: {sent} apagadas ({attempts} tentativas).");
    }

    /// <summary>Tabelas com versões antigas de linhas a eliminar com VACUUM FULL depois da anonimização.</summary>
    public static readonly string[] Tables =
    [
        "students.students", "students.guardians", "students.addresses", "imports.rows",
        "notifications.notifications", "notifications.attempts",
    ];
}
