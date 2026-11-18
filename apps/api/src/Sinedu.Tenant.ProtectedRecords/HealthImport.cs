using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.Tenant.Imports;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.ProtectedRecords;

public sealed record HealthImportRow(int RowNumber, string Status, Guid? StudentId, string? StudentName, IReadOnlyList<string> Fields);

public sealed record HealthImportResult(
    string Sheet, int Rows, int New, int Update, int Unchanged, int NotFound, int NoCode, int Duplicate, int Applied, bool DryRun, IReadOnlyList<HealthImportRow> Items);

/// <summary>
/// Importação de Saúde (F4-05), separada da importação comum: só DIRETOR/PEDAGOGO com step-up.
/// O arquivo nunca é gravado (nem no banco de importações, nem no S3): a prévia lê, responde sem nenhum valor e descarta;
/// a confirmação reenvia o mesmo arquivo e grava cada ficha como nova versão cifrada. Aluno só por CGM, nunca por nome.
/// </summary>
internal sealed class HealthImport(ProtectedDbContext db, ProtectedService records, IStudentRegistry students, IAuditLog audit)
{
    private enum Column { Code, BloodType, Allergies, Conditions, Medications, Restrictions, FirstAid, EmergencyName, EmergencyPhone, EmergencyRelationship, Source, Notes }

    private static readonly Dictionary<Column, (string Label, string[] Synonyms)> Columns = new()
    {
        [Column.Code] = ("CGM", ["cgm", "codigo", "codigo do aluno", "matricula", "codigo aluno"]),
        [Column.BloodType] = ("Tipo sanguíneo", ["tipo sanguineo", "tipo de sangue", "tipo sangue", "sangue", "fator rh"]),
        [Column.Allergies] = ("Alergias", ["alergia", "alergias", "alergias e cuidados"]),
        [Column.Conditions] = ("Condições de saúde", ["condicoes", "condicao", "condicoes de saude", "doencas", "doenca"]),
        [Column.Medications] = ("Medicamentos", ["medicamento", "medicamentos", "medicacao", "medicacoes", "remedios"]),
        [Column.Restrictions] = ("Restrições", ["restricoes", "restricao", "restricoes e adaptacoes", "adaptacoes"]),
        [Column.FirstAid] = ("Primeiros cuidados", ["primeiros cuidados", "primeiros socorros", "instrucoes"]),
        [Column.EmergencyName] = ("Contato de emergência", ["contato de emergencia", "contato emergencia", "nome do contato", "contato"]),
        [Column.EmergencyPhone] = ("Telefone de emergência", ["telefone de emergencia", "telefone emergencia", "fone emergencia", "telefone do contato"]),
        [Column.EmergencyRelationship] = ("Parentesco do contato", ["parentesco", "parentesco do contato", "grau de parentesco"]),
        [Column.Source] = ("Origem da informação", ["origem", "fonte", "origem da informacao"]),
        [Column.Notes] = ("Observações", ["observacoes", "observacao", "obs"]),
    };

    public static IReadOnlyList<string> ExpectedHeaders => Columns.Values.Select(c => c.Label).ToList();

    public async Task<HealthImportResult> RunAsync(Stream content, string fileName, string? sheetName, bool dryRun, string? reason, Actor actor, CancellationToken ct)
    {
        if (!dryRun)
        {
            var errors = new Dictionary<string, string[]>();
            PayloadRules.Reason(reason, errors);
            if (errors.Count > 0)
            {
                throw new ProtectedValidationException(errors);
            }
        }

        var sheets = SpreadsheetReader.Read(content, fileName);
        var sheet = (sheetName is null ? sheets.FirstOrDefault(s => FindHeader(s) is not null) : sheets.FirstOrDefault(s => s.Name == sheetName))
            ?? throw new SpreadsheetException("Não encontramos uma aba com a coluna CGM e colunas de saúde (alergias, medicamentos...).");
        var header = FindHeader(sheet) ?? throw new SpreadsheetException("A aba escolhida não tem a coluna CGM e colunas de saúde reconhecidas.");

        var parsed = sheet.Rows.Where(r => r.RowNumber > header.Row).Select(r => (r.RowNumber, Data: Parse(r.Cells, header.Map))).ToList();
        var codes = parsed.Select(p => p.Data.Code).OfType<string>().Distinct().ToList();
        var byCode = (await students.FindByCodesAsync(codes, ct)).Where(s => s.InstitutionalCode is not null)
            .GroupBy(s => s.InstitutionalCode!).ToDictionary(g => g.Key, g => g.ToList());
        var studentIds = byCode.Values.SelectMany(v => v).Select(s => s.Id).ToList();
        var existing = await db.HealthRecords.AsNoTracking().Where(h => studentIds.Contains(h.StudentId)).ToDictionaryAsync(h => h.StudentId, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var items = new List<HealthImportRow>();
        var applied = 0;
        foreach (var (rowNumber, data) in parsed)
        {
            var fields = data.Filled();
            if (data.Code is null && fields.Count == 0)
            {
                continue;
            }

            if (data.Code is null)
            {
                items.Add(new HealthImportRow(rowNumber, "no_code", null, null, fields));
                continue;
            }

            if (!seen.Add(data.Code))
            {
                items.Add(new HealthImportRow(rowNumber, "duplicate", null, null, fields));
                continue;
            }

            if (!byCode.TryGetValue(data.Code, out var matches) || matches.Count != 1)
            {
                items.Add(new HealthImportRow(rowNumber, "not_found", null, null, fields));
                continue;
            }

            var student = matches[0];
            HealthData? current = null;
            if (existing.TryGetValue(student.Id, out var record))
            {
                current = (await records.DecryptAsync<HealthData>(
                    await db.Versions.AsNoTracking().FirstAsync(v => v.RecordKind == RecordKind.Health && v.RecordId == record.Id && v.Version == record.CurrentVersion, ct), ct)).Data;
            }

            var merged = Merge(current, data);
            var status = current is null ? "new" : Same(current, merged) ? "unchanged" : "update";
            items.Add(new HealthImportRow(rowNumber, status, student.Id, student.FullName, fields));

            if (!dryRun && status is "new" or "update")
            {
                await records.SaveHealthAsync(student.Id, merged, record?.ReviewDueOn, reason, record?.CurrentVersion, actor, ct, "health_imported");
                applied++;
            }
        }

        await audit.WriteAsync(new AuditEntry(dryRun ? "health_import_previewed" : "health_import_confirmed", ResourceType: "health_import"), ct);
        return new HealthImportResult(
            sheet.Name, items.Count,
            items.Count(i => i.Status == "new"), items.Count(i => i.Status == "update"), items.Count(i => i.Status == "unchanged"),
            items.Count(i => i.Status == "not_found"), items.Count(i => i.Status == "no_code"), items.Count(i => i.Status == "duplicate"),
            applied, dryRun, items);
    }

    private sealed record RowData(
        string? Code, string? BloodType, string? Allergies, string? Conditions, string? Medications, string? Restrictions,
        string? FirstAid, string? EmergencyName, string? EmergencyPhone, string? EmergencyRelationship, string? Source, string? Notes)
    {
        /// <summary>Nomes das colunas preenchidas — a prévia mostra só isso, nunca os valores.</summary>
        public List<string> Filled() =>
            new (Column Col, string? Value)[]
            {
                (Column.BloodType, BloodType), (Column.Allergies, Allergies), (Column.Conditions, Conditions), (Column.Medications, Medications),
                (Column.Restrictions, Restrictions), (Column.FirstAid, FirstAid), (Column.EmergencyName, EmergencyName), (Column.Source, Source), (Column.Notes, Notes),
            }.Where(x => x.Value is not null).Select(x => Columns[x.Col].Label).ToList();
    }

    private static RowData Parse(string?[] cells, IReadOnlyDictionary<Column, int> map)
    {
        string? Get(Column c) => map.TryGetValue(c, out var i) && i < cells.Length && !string.IsNullOrWhiteSpace(cells[i]) ? cells[i]!.Trim() : null;
        var code = Get(Column.Code);
        code = code is null ? null : new string(code.Where(char.IsLetterOrDigit).ToArray());
        return new RowData(
            string.IsNullOrEmpty(code) ? null : code, Get(Column.BloodType), Get(Column.Allergies), Get(Column.Conditions), Get(Column.Medications),
            Get(Column.Restrictions), Get(Column.FirstAid), Get(Column.EmergencyName), Get(Column.EmergencyPhone), Get(Column.EmergencyRelationship),
            Get(Column.Source), Get(Column.Notes));
    }

    /// <summary>Campo vazio no arquivo nunca apaga o que já existe; contato novo é acrescentado.</summary>
    private static HealthData Merge(HealthData? current, RowData row)
    {
        var contacts = (current?.EmergencyContacts ?? []).ToList();
        if (row.EmergencyName is { } name && !contacts.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            contacts.Add(new EmergencyContact(name, row.EmergencyPhone, row.EmergencyRelationship));
        }

        var merged = new HealthData(
            row.BloodType ?? current?.BloodType, row.Allergies ?? current?.Allergies, row.Conditions ?? current?.Conditions,
            row.Medications ?? current?.Medications, row.Restrictions ?? current?.Restrictions, row.FirstAid ?? current?.FirstAidInstructions,
            contacts, row.Source ?? current?.Source, row.Notes ?? current?.Notes);
        return PayloadRules.Normalize(merged, []);
    }

    private static bool Same(HealthData a, HealthData b) =>
        a.BloodType == b.BloodType && a.Allergies == b.Allergies && a.Conditions == b.Conditions && a.Medications == b.Medications
        && a.Restrictions == b.Restrictions && a.FirstAidInstructions == b.FirstAidInstructions && a.Source == b.Source && a.Notes == b.Notes
        && (a.EmergencyContacts ?? []).SequenceEqual(b.EmergencyContacts ?? []);

    private static (int Row, IReadOnlyDictionary<Column, int> Map)? FindHeader(ParsedSheet sheet)
    {
        foreach (var (rowNumber, cells) in sheet.Rows.Take(10))
        {
            var map = new Dictionary<Column, int>();
            for (var i = 0; i < cells.Length; i++)
            {
                var text = Normalize(cells[i]);
                if (text.Length == 0)
                {
                    continue;
                }

                // Sinônimo exato primeiro; "contato" sozinho não pode roubar "telefone do contato".
                var match = Columns.Where(c => !map.ContainsKey(c.Key) && c.Value.Synonyms.Contains(text)).Select(c => (Column?)c.Key).FirstOrDefault();
                if (match is { } col)
                {
                    map[col] = i;
                }
            }

            if (map.ContainsKey(Column.Code) && map.Count >= 2)
            {
                return (rowNumber, map);
            }
        }

        return null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var d = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
