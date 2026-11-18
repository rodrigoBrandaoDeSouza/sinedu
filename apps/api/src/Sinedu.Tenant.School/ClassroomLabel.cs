using System.Globalization;
using System.Text.RegularExpressions;

namespace Sinedu.Tenant.School;

/// <summary>Identifica uma turma pelo que aparece nos relatórios: série + turma + turno.</summary>
public sealed record ClassroomKey(string GradeShort, Stage Stage, string Section, Shift Shift)
{
    public string Display => Classroom.DisplayName(GradeShort, Section, Shift);
}

/// <summary>
/// Interpreta rótulos de turma dos relatórios das escolas. Formatos vistos na planilha real:
/// <c>6º A Manhã</c>, <c>1ª D Noite</c> (cadastro) e <c>9º Série A Manhã - 1234567</c> (frequência, com código da turma).
/// </summary>
public static partial class ClassroomLabel
{
    public static bool TryParse(string? value, out ClassroomKey key, out string? externalCode)
    {
        key = null!;
        externalCode = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Pattern().Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        var number = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        var ordinal = match.Groups["o"].Value;
        var level = match.Groups["level"].Value.ToUpperInvariant();

        // Ensino Médio: ordinal feminino ("1ª") ou "Série" com número até 3 (a frequência escreve "1º Série").
        // Fundamental: "6º", "6º ano", "9º Série" (séries 6 a 9 só existem no Fundamental).
        var medio = number <= 3 && (ordinal is "ª" or "a" || level.StartsWith('S'));

        var shift = Normalize(match.Groups["shift"].Value) switch
        {
            "MANHA" or "MATUTINO" => Shift.Manha,
            "TARDE" or "VESPERTINO" => Shift.Tarde,
            "NOITE" or "NOTURNO" => Shift.Noite,
            _ => Shift.Integral,
        };

        key = new ClassroomKey($"{number}{(medio ? "ª" : "º")}", medio ? Stage.Medio : Stage.Fundamental, match.Groups["section"].Value.ToUpperInvariant(), shift);
        externalCode = match.Groups["code"].Success ? match.Groups["code"].Value : null;
        return true;
    }

    public static string GradeName(ClassroomKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var number = key.GradeShort[..^1];
        return key.Stage == Stage.Medio ? $"{number}ª série do Ensino Médio" : $"{number}º ano";
    }

    public static int GradeOrder(ClassroomKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var number = int.Parse(key.GradeShort[..^1], CultureInfo.InvariantCulture);
        return key.Stage == Stage.Medio ? 100 + number : number;
    }

    private static string Normalize(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .ToUpperInvariant();

    [GeneratedRegex(
        @"^(?<n>\d{1,2})\s*(?<o>[ºªoa°])?\.?\s*(?<level>ano|s[ée]rie|serie)?\s*(?<section>[A-Za-z])\s*[-–]?\s*(?<shift>manh[ãa]|tarde|noite|integral|matutino|vespertino|noturno)(\s*[-–]\s*(?<code>\d{4,}))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
