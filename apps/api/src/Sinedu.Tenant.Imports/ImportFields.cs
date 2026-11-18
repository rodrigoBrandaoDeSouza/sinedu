using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sinedu.Tenant.Imports;

public enum ImportField
{
    Code,
    Name,
    Classroom,
    BirthDate,
    Email,
    Address,
    CallNumber,
    GuardianName,
    GuardianDocument,
    GuardianPhone,
    GuardianRelationship,
}

public sealed record FieldInfo(ImportField Field, string Label, bool Required, string Hint);

public sealed record SheetInfo(string Name, int RowCount, int HeaderRow, IReadOnlyList<string?> Headers);

/// <summary>Campos que a importação de alunos entende, com sinônimos de cabeçalho vistos nos relatórios das escolas.</summary>
public static partial class ImportFields
{
    public static readonly IReadOnlyList<FieldInfo> All =
    [
        new(ImportField.Code, "CGM / código do aluno", false, "Recomendado: é o que evita duplicar alunos."),
        new(ImportField.Name, "Nome do aluno", true, ""),
        new(ImportField.Classroom, "Turma", true, "Ex.: 6º A Manhã, 1ª D Noite, 9º Série A Manhã - 1234567"),
        new(ImportField.BirthDate, "Data de nascimento", false, ""),
        new(ImportField.Email, "E-mail institucional", false, ""),
        new(ImportField.Address, "Endereço", false, ""),
        new(ImportField.CallNumber, "Nº de chamada", false, ""),
        new(ImportField.GuardianName, "Responsável — nome", false, ""),
        new(ImportField.GuardianDocument, "Responsável — CPF/RG", false, ""),
        new(ImportField.GuardianPhone, "Responsável — telefone", false, ""),
        new(ImportField.GuardianRelationship, "Responsável — vínculo", false, "Mãe, pai, avós…"),
    ];

    private static readonly Dictionary<ImportField, string[]> Synonyms = new()
    {
        [ImportField.Code] = ["CGM", "CODIGO", "CODIGO DO ALUNO", "MATRICULA", "N MATRICULA", "RA"],
        [ImportField.Name] = ["NOME DO ALUNO", "ALUNO", "NOME", "NOME COMPLETO", "ESTUDANTE", "NOME DO ESTUDANTE"],
        [ImportField.Classroom] = ["TURMA", "CLASSE", "SERIE/TURMA"],
        [ImportField.BirthDate] = ["DATA NASCIMENTO", "DATA DE NASCIMENTO", "NASCIMENTO", "DT NASC", "DATA NASC"],
        [ImportField.Email] = ["E-MAIL INSTITUCIONAL", "EMAIL INSTITUCIONAL", "E-MAIL", "EMAIL"],
        [ImportField.Address] = ["ENDERECO", "ENDERECO COMPLETO", "LOGRADOURO"],
        [ImportField.CallNumber] = ["N", "NO", "NUMERO", "N CHAMADA", "NUMERO DE CHAMADA"],
        [ImportField.GuardianName] = ["RESPONSAVEL", "NOME DO RESPONSAVEL", "RESPONSAVEL LEGAL", "FILIACAO", "MAE"],
        [ImportField.GuardianDocument] = ["CPF / RG RESPONSAVEL", "CPF/RG RESPONSAVEL", "DOCUMENTO DO RESPONSAVEL", "CPF DO RESPONSAVEL", "CPF RESPONSAVEL"],
        [ImportField.GuardianPhone] = ["TEL. DO RESP.", "TEL DO RESP", "TELEFONE", "TELEFONE DO RESPONSAVEL", "CELULAR", "FONE", "CONTATO"],
        [ImportField.GuardianRelationship] = ["RESPONS. P/ MATRICULA ALUNO", "PARENTESCO", "VINCULO", "GRAU DE PARENTESCO"],
    };

    public static string Normalize(string? value) =>
        new string((value ?? "").Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .ToUpperInvariant().Replace("º", "", StringComparison.Ordinal).Replace("°", "", StringComparison.Ordinal)
            .Trim();

    /// <summary>Primeira linha com pelo menos 3 células de texto (pula títulos e linhas em branco).</summary>
    public static int DetectHeaderRow(ParsedSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        foreach (var (number, cells) in sheet.Rows.Take(15))
        {
            if (cells.Count(c => c is not null && c.Any(char.IsLetter)) >= 3) return number;
        }

        return sheet.Rows.Count > 0 ? sheet.Rows[0].RowNumber : 1;
    }

    /// <summary>Sugere o mapeamento pelo cabeçalho e confirma pelo conteúdo da coluna.</summary>
    public static Dictionary<ImportField, int> Suggest(ParsedSheet sheet, int headerRow)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var headers = sheet.Rows.FirstOrDefault(r => r.RowNumber == headerRow).Cells ?? [];
        var data = sheet.Rows.Where(r => r.RowNumber > headerRow).Take(200).Select(r => r.Cells).ToList();
        var profiles = Enumerable.Range(0, headers.Length).Select(i => Profile(data, i)).ToArray();
        var mapping = new Dictionary<ImportField, int>();
        var used = new HashSet<int>();

        void Take(ImportField field, int column)
        {
            mapping[field] = column;
            used.Add(column);
        }

        // 1) Cabeçalho exato, depois "começa com" — ignorando colunas de checklist (OK/FALTA).
        foreach (var exact in new[] { true, false })
        {
            foreach (var (field, names) in Synonyms)
            {
                if (mapping.ContainsKey(field)) continue;
                for (var i = 0; i < headers.Length; i++)
                {
                    if (used.Contains(i) || profiles[i].IsChecklist || profiles[i].Filled == 0) continue;
                    var header = Normalize(headers[i]);
                    var hit = exact ? names.Contains(header) : names.Any(n => n.Length >= 4 && header.StartsWith(n, StringComparison.Ordinal));
                    if (hit && Fits(field, profiles[i]))
                    {
                        Take(field, i);
                        break;
                    }
                }
            }
        }

        // 2) Pelo conteúdo, quando o cabeçalho não ajuda (ex.: CGM sem título, nº de chamada com cabeçalho numérico).
        if (!mapping.ContainsKey(ImportField.Code))
        {
            var best = Enumerable.Range(0, headers.Length).Where(i => !used.Contains(i) && profiles[i].CodeLike >= 0.9).OrderByDescending(i => profiles[i].Distinct).FirstOrDefault(-1);
            if (best >= 0) Take(ImportField.Code, best);
        }

        if (!mapping.ContainsKey(ImportField.CallNumber))
        {
            var best = Enumerable.Range(0, headers.Length).Where(i => !used.Contains(i) && profiles[i].SmallInt >= 0.9).FirstOrDefault(-1);
            if (best >= 0) Take(ImportField.CallNumber, best);
        }

        return mapping;
    }

    private static bool Fits(ImportField field, ColumnProfile p) => field switch
    {
        ImportField.Code => p.CodeLike >= 0.8,
        ImportField.GuardianDocument => p.DocumentLike >= 0.5,
        ImportField.GuardianPhone => p.DocumentLike >= 0.5,
        ImportField.BirthDate => p.DateLike >= 0.5,
        ImportField.CallNumber => p.SmallInt >= 0.8,
        _ => true,
    };

    private sealed record ColumnProfile(int Filled, int Distinct, double CodeLike, double DocumentLike, double DateLike, double SmallInt, bool IsChecklist);

    private static ColumnProfile Profile(List<string?[]> rows, int column)
    {
        var values = rows.Select(r => column < r.Length ? r[column] : null).Where(v => v is not null).Select(v => v!).ToList();
        if (values.Count == 0) return new ColumnProfile(0, 0, 0, 0, 0, 0, false);
        double Share(Func<string, bool> test) => values.Count(test) / (double)values.Count;
        var checklist = Share(v => ChecklistWords.Contains(Normalize(v))) >= 0.6;
        return new ColumnProfile(
            values.Count,
            values.Distinct().Count(),
            CodeLike: Share(v => v.All(char.IsDigit) && v.Length is >= 6 and <= 15),
            DocumentLike: Share(v => v.Count(char.IsDigit) >= 7),
            DateLike: Share(v => TryParseDate(v, out _)),
            SmallInt: Share(v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is > 0 and <= 99),
            IsChecklist: checklist);
    }

    private static readonly HashSet<string> ChecklistWords = ["OK", "0K", "FALTA", "NAO AUTORIZADO", "COPIA", "SIM", "NAO", "X"];

    public static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        if (DateOnly.TryParseExact(v, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "dd.MM.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return date.Year is > 1900 and < 2100;
        }

        return false;
    }

    public static string? Digits(string? value) => value is null ? null : DigitsRegex().Replace(value, "") is { Length: > 0 } d ? d : null;

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitsRegex();
}
