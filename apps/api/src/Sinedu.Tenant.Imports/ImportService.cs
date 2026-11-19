using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.Tenant.School;
using Sinedu.Tenant.Students;

namespace Sinedu.Tenant.Imports;

public sealed record ImportTotals(
    int Rows, int New, int Update, int Unchanged, int PossibleDuplicate, int Conflict, int Rejected, int Empty, int Pending, int NewClassrooms,
    int Created = 0, int Updated = 0, int Skipped = 0, int Failed = 0);

public sealed class ImportException(string message) : Exception(message);

/// <summary>Dados de uma linha já interpretada segundo o mapeamento.</summary>
internal sealed record ParsedRow(
    string? Code, string? Name, ClassroomKey? Classroom, string? ClassroomExternalCode, DateOnly? BirthDate, string? Email, string? Address, int? CallNumber,
    GuardianImport? Guardian, List<string> Errors, List<string> Warnings);

/// <summary>
/// Importação assistida (spec §7.1): envio → mapeamento → validação/prévia → revisão → confirmação.
/// Regras: CGM é o identificador confiável; sem CGM, nome igual vira só SUGESTÃO; vazio nunca apaga dado;
/// alunos nunca são unidos automaticamente pelo nome; mensagens de erro não carregam valores das células.
/// </summary>
public sealed class ImportService(
    ImportsDbContext db, IStudentRegistry students, IClassroomDirectory classrooms, IAuditLog audit, TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- 1. Envio ----------

    public async Task<ImportBatch> UploadAsync(Stream content, string fileName, Guid userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        await PurgeExpiredAsync(ct);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
        buffer.Position = 0;

        var sheets = SpreadsheetReader.Read(buffer, fileName);
        var now = clock.GetUtcNow();
        var batch = new ImportBatch
        {
            Id = Guid.CreateVersion7(),
            FileName = Path.GetFileName(fileName),
            FileSha256 = hash,
            FileSize = buffer.Length,
            Status = ImportStatus.Uploaded,
            SourceJson = JsonSerializer.Serialize(
                sheets.Select(s =>
                {
                    var headerRow = ImportFields.DetectHeaderRow(s);
                    return new SheetInfo(s.Name, s.Rows.Count, headerRow, s.Rows.FirstOrDefault(r => r.RowNumber == headerRow).Cells ?? []);
                }).ToList(),
                Json),
            CreatedBy = userId,
            CreatedAt = now,
            PurgeAfter = now.Add(Retention),
        };
        db.Batches.Add(batch);
        db.Rows.AddRange(sheets.SelectMany(s => s.Rows.Select(r => new ImportRow
        {
            Id = Guid.CreateVersion7(), BatchId = batch.Id, Sheet = s.Name, RowNumber = r.RowNumber, Cells = r.Cells, Action = RowAction.Pending,
        })));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("import_uploaded", ResourceType: "import", ResourceId: batch.Id.ToString()), ct);
        return batch;
    }

    public static IReadOnlyList<SheetInfo> Sheets(ImportBatch batch) =>
        JsonSerializer.Deserialize<List<SheetInfo>>(batch?.SourceJson ?? "[]", Json) ?? [];

    public static Dictionary<ImportField, int> Mapping(ImportBatch batch) =>
        batch?.MappingJson is null ? [] : JsonSerializer.Deserialize<Dictionary<ImportField, int>>(batch.MappingJson, Json) ?? [];

    public static ImportTotals? Totals(ImportBatch batch) =>
        batch?.TotalsJson is null ? null : JsonSerializer.Deserialize<ImportTotals>(batch.TotalsJson, Json);

    /// <summary>Mapeamento salvo para este formato de planilha (F3-02), senão a sugestão automática.</summary>
    public async Task<(Dictionary<ImportField, int> Mapping, bool Saved)> SuggestAsync(ImportBatch batch, string sheet, int headerRow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var rows = await db.Rows.AsNoTracking().Where(r => r.BatchId == batch.Id && r.Sheet == sheet).OrderBy(r => r.RowNumber).Take(250)
            .Select(r => new { r.RowNumber, r.Cells }).ToListAsync(ct);
        var headers = rows.FirstOrDefault(r => r.RowNumber == headerRow)?.Cells ?? [];
        var signature = Signature(headers);
        if (signature is not null && await db.Presets.AsNoTracking().FirstOrDefaultAsync(p => p.Signature == signature, ct) is { } preset)
        {
            var saved = JsonSerializer.Deserialize<Dictionary<ImportField, int>>(preset.MappingJson, Json) ?? [];
            if (saved.Count > 0 && saved.Values.All(c => c >= 0 && c < headers.Length))
            {
                return (saved, true);
            }
        }

        return (ImportFields.Suggest(new ParsedSheet(sheet, rows.Select(r => (r.RowNumber, r.Cells)).ToList()), headerRow), false);
    }

    /// <summary>Assinatura do formato: títulos normalizados das colunas. Nula sem cabeçalho de texto.</summary>
    public static string? Signature(IReadOnlyList<string?> headers)
    {
        var normalized = headers.Select(h => ImportFields.Normalize(h)).ToList();
        if (normalized.Count(h => h.Length > 0) < 2) return null;
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("", normalized))));
    }

    private async Task SavePresetAsync(ImportBatch batch, Guid userId, CancellationToken ct)
    {
        if (batch.Sheet is null || batch.HeaderRow is null || batch.MappingJson is null) return;
        var headers = await db.Rows.AsNoTracking().Where(r => r.BatchId == batch.Id && r.Sheet == batch.Sheet && r.RowNumber == batch.HeaderRow)
            .Select(r => r.Cells).FirstOrDefaultAsync(ct) ?? [];
        if (Signature(headers) is not { } signature) return;

        var preset = await db.Presets.FirstOrDefaultAsync(p => p.Signature == signature, ct);
        if (preset is null) db.Presets.Add(preset = new MappingPreset { Id = Guid.CreateVersion7(), Signature = signature, MappingJson = batch.MappingJson });
        preset.MappingJson = batch.MappingJson;
        preset.UpdatedAt = clock.GetUtcNow();
        preset.UpdatedBy = userId;
        await db.SaveChangesAsync(ct);
    }

    // ---------- 2/3. Mapeamento + validação (prévia) ----------

    public async Task<ImportTotals> MapAndValidateAsync(ImportBatch batch, string sheet, int headerRow, Dictionary<ImportField, int> mapping, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(mapping);
        if (batch.Status is not (ImportStatus.Uploaded or ImportStatus.Validated)) throw new ImportException("Este lote não pode mais ser alterado.");
        if (Sheets(batch).All(s => s.Name != sheet)) throw new ImportException("Aba inexistente no arquivo.");
        if (!mapping.ContainsKey(ImportField.Name) || !mapping.ContainsKey(ImportField.Classroom))
        {
            throw new ImportException("Mapeie pelo menos as colunas de nome do aluno e turma.");
        }

        if (mapping.Values.Distinct().Count() != mapping.Count) throw new ImportException("A mesma coluna foi usada para dois campos.");

        var year = await classrooms.GetActiveYearAsync(ct) ?? throw new ImportException("Cadastre e ative o ano letivo antes de importar.");
        var existingClassrooms = await classrooms.GetByKeyAsync(year.Id, ct);

        // Só a aba escolhida fica; as demais são descartadas (minimização).
        await db.Rows.Where(r => r.BatchId == batch.Id && r.Sheet != sheet).ExecuteDeleteAsync(ct);
        var rows = await db.Rows.Where(r => r.BatchId == batch.Id).OrderBy(r => r.RowNumber).ToListAsync(ct);

        var parsed = rows.ToDictionary(r => r.Id, r => r.RowNumber <= headerRow ? null : Parse(r.Cells, mapping));
        var codes = parsed.Values.Where(p => p?.Code is not null).Select(p => p!.Code!).Distinct().ToList();
        var names = parsed.Values.Where(p => p?.Name is not null).Select(p => Student.ToSearch(p!.Name!)).Distinct().ToList();
        var byCode = (await students.FindByCodesAsync(codes, ct)).ToDictionary(s => s.InstitutionalCode!);
        var byName = (await students.FindByNamesAsync(names, ct)).ToLookup(s => s.SearchName);

        var seenCodes = new Dictionary<string, int>();
        var newClassrooms = new HashSet<ClassroomKey>();

        foreach (var row in rows)
        {
            row.Decision = RowDecision.None;
            row.TargetStudentId = null;
            row.Candidates = [];
            row.Changes = [];
            var p = parsed[row.Id];
            if (p is null || (p.Name is null && p.Code is null && p.Classroom is null && p.Errors.Count == 0))
            {
                row.Action = RowAction.Empty;
                row.Errors = [];
                row.Warnings = [];
                continue;
            }

            row.Errors = [.. p.Errors];
            row.Warnings = [.. p.Warnings];
            if (p.Errors.Count > 0)
            {
                row.Action = RowAction.Rejected;
                continue;
            }

            if (!existingClassrooms.ContainsKey(p.Classroom!) && newClassrooms.Add(p.Classroom!))
            {
                row.Warnings = [.. row.Warnings, $"A turma {p.Classroom!.Display} será criada."];
            }

            if (p.Code is { } code)
            {
                if (seenCodes.TryGetValue(code, out var firstRow))
                {
                    row.Action = RowAction.Conflict;
                    row.Errors = [$"CGM repetido no arquivo (mesmo da linha {firstRow})."];
                    continue;
                }

                seenCodes[code] = row.RowNumber;
                if (byCode.TryGetValue(code, out var current))
                {
                    row.TargetStudentId = current.Id;
                    if (!SimilarNames(current.SearchName, Student.ToSearch(p.Name!)))
                    {
                        row.Action = RowAction.Conflict;
                        row.Errors = ["Este CGM já pertence a um aluno com nome diferente."];
                        continue;
                    }

                    var target = existingClassrooms.GetValueOrDefault(p.Classroom!);
                    row.Changes = [.. Diff(current, p, target)];
                    row.Action = row.Changes.Length == 0 ? RowAction.Unchanged : RowAction.Update;
                    continue;
                }
            }

            // Sem correspondência por CGM: nome igual é só sugestão (nunca união automática).
            var candidates = byName[Student.ToSearch(p.Name!)].Where(c => p.Code is null || c.InstitutionalCode is null || c.InstitutionalCode == p.Code).ToList();
            if (candidates.Count > 0)
            {
                row.Action = RowAction.PossibleDuplicate;
                row.Candidates = [.. candidates.Select(c => c.Id)];
                row.Warnings = [.. row.Warnings, candidates.Count == 1 ? "Já existe um aluno com o mesmo nome." : $"Já existem {candidates.Count} alunos com o mesmo nome."];
                continue;
            }

            row.Action = RowAction.New;
        }

        batch.Sheet = sheet;
        batch.HeaderRow = headerRow;
        batch.MappingJson = JsonSerializer.Serialize(mapping, Json);
        batch.Status = ImportStatus.Validated;
        batch.ValidatedAt = clock.GetUtcNow();
        var totals = Count(rows, newClassrooms.Count);
        batch.TotalsJson = JsonSerializer.Serialize(totals, Json);
        await db.SaveChangesAsync(ct);
        return totals;
    }

    // ---------- 4. Revisão ----------

    public async Task DecideAsync(ImportBatch batch, Guid rowId, RowDecision decision, Guid? targetStudentId, Guid userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Status != ImportStatus.Validated) throw new ImportException("Este lote não está em revisão.");
        var row = await db.Rows.FirstOrDefaultAsync(r => r.Id == rowId && r.BatchId == batch.Id, ct) ?? throw new ImportException("Linha inexistente.");

        var allowed = row.Action switch
        {
            RowAction.PossibleDuplicate => decision switch
            {
                RowDecision.Create or RowDecision.Skip => true,
                RowDecision.Update => targetStudentId is { } t && row.Candidates.Contains(t),
                _ => false,
            },
            // CGM igual com nome diferente: pode aceitar o CGM (atualizar o aluno) ou ignorar. CGM repetido no arquivo: só ignorar.
            RowAction.Conflict => decision == RowDecision.Skip || (decision == RowDecision.Update && row.TargetStudentId is not null && targetStudentId == row.TargetStudentId),
            RowAction.New or RowAction.Update => decision == RowDecision.Skip,
            _ => false,
        };
        if (!allowed) throw new ImportException("Decisão não permitida para esta linha.");

        row.Decision = decision;
        row.TargetStudentId = decision == RowDecision.Update ? targetStudentId : row.Action == RowAction.Conflict ? row.TargetStudentId : decision == RowDecision.Create ? null : row.TargetStudentId;
        row.ResolvedBy = userId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> SkipPendingAsync(ImportBatch batch, Guid userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return await db.Rows
            .Where(r => r.BatchId == batch.Id && r.Decision == RowDecision.None && (r.Action == RowAction.PossibleDuplicate || r.Action == RowAction.Conflict))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Decision, RowDecision.Skip).SetProperty(r => r.ResolvedBy, userId), ct);
    }

    // ---------- 5. Confirmação ----------

    public async Task<ImportTotals> ConfirmAsync(ImportBatch batch, Guid userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Status != ImportStatus.Validated) throw new ImportException("Este lote não está pronto para confirmar.");
        var pending = await db.Rows.CountAsync(r => r.BatchId == batch.Id && r.Decision == RowDecision.None
            && (r.Action == RowAction.PossibleDuplicate || r.Action == RowAction.Conflict), ct);
        if (pending > 0) throw new ImportException($"Ainda há {pending} linha(s) aguardando revisão.");

        batch.Status = ImportStatus.Processing;
        batch.ConfirmedBy = userId;
        await db.SaveChangesAsync(ct);

        // F3-02: o mapeamento confirmado vira o padrão deste formato de planilha.
        await SavePresetAsync(batch, userId, ct);

        var year = await classrooms.GetActiveYearAsync(ct) ?? throw new ImportException("Não há ano letivo ativo.");
        var mapping = Mapping(batch);
        var classroomIds = new Dictionary<ClassroomKey, Guid>(await classrooms.GetByKeyAsync(year.Id, ct));
        var rows = await db.Rows.Where(r => r.BatchId == batch.Id).OrderBy(r => r.RowNumber).ToListAsync(ct);
        int created = 0, updated = 0, unchanged = 0, skipped = 0, failed = 0;

        foreach (var row in rows)
        {
            var apply = row.Decision switch
            {
                RowDecision.Skip => (bool?)null,
                RowDecision.Create => true,
                RowDecision.Update => true,
                _ => row.Action is RowAction.New or RowAction.Update or RowAction.Unchanged ? true : null,
            };
            if (apply is null)
            {
                if (row.Action is not (RowAction.Empty or RowAction.Pending)) { row.Outcome = RowOutcome.Skipped; skipped++; }
                continue;
            }

            var p = Parse(row.Cells, mapping);
            try
            {
                if (!classroomIds.TryGetValue(p.Classroom!, out var classroomId))
                {
                    classroomId = await classrooms.EnsureAsync(year.Id, p.Classroom!, p.ClassroomExternalCode, ct);
                    classroomIds[p.Classroom!] = classroomId;
                }

                var target = row.Decision == RowDecision.Create ? null : row.TargetStudentId;
                var outcome = await students.ApplyAsync(
                    new StudentImport(p.Name!, p.Code, p.BirthDate, p.Email, classroomId, year.Id, year.StartsOn, p.CallNumber, p.Address, p.Guardian), target, ct);
                row.Outcome = outcome switch { StudentApplyOutcome.Created => RowOutcome.Created, StudentApplyOutcome.Updated => RowOutcome.Updated, _ => RowOutcome.Unchanged };
                _ = outcome switch { StudentApplyOutcome.Created => created++, StudentApplyOutcome.Updated => updated++, _ => unchanged++ };
            }
#pragma warning disable CA1031 // Uma linha com erro não derruba o lote; fica registrada como falha.
            catch (Exception)
#pragma warning restore CA1031
            {
                row.Outcome = RowOutcome.Failed;
                row.Errors = [.. row.Errors, "Não foi possível gravar esta linha (dado duplicado ou inválido no banco)."];
                failed++;
            }
        }

        var before = Totals(batch)!;
        var totals = before with { Created = created, Updated = updated, Skipped = skipped, Failed = failed, Unchanged = unchanged };
        batch.TotalsJson = JsonSerializer.Serialize(totals, Json);
        batch.Status = ImportStatus.Completed;
        batch.CompletedAt = clock.GetUtcNow();
        batch.PurgeAfter = batch.CompletedAt.Value.Add(Retention);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("import_confirmed", ResourceType: "import", ResourceId: batch.Id.ToString()), ct);
        return totals;
    }

    public async Task CancelAsync(ImportBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Status is ImportStatus.Completed or ImportStatus.Processing) throw new ImportException("Lote já processado.");
        batch.Status = ImportStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        await db.Rows.Where(r => r.BatchId == batch.Id).ExecuteDeleteAsync(ct);
    }

    /// <summary>P5: conteúdo das linhas é apagado após o prazo; ficam totais, ações e mensagens.</summary>
    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var expired = db.Batches.Where(b => b.PurgeAfter < now).Select(b => b.Id);
        await db.Rows.Where(r => expired.Contains(r.BatchId) && r.Cells.Length > 0).ExecuteUpdateAsync(s => s.SetProperty(r => r.Cells, Array.Empty<string?>()), ct);
    }

    // ---------- Interpretação de uma linha ----------

    internal static ParsedRow Parse(string?[] cells, IReadOnlyDictionary<ImportField, int> mapping)
    {
        string? Get(ImportField f) => mapping.TryGetValue(f, out var i) && i < cells.Length ? cells[i]?.Trim() is { Length: > 0 } v ? v : null : null;
        var errors = new List<string>();
        var warnings = new List<string>();

        var name = Get(ImportField.Name);
        if (name is null) errors.Add("Nome do aluno em branco.");
        else if (name.Count(char.IsLetter) < 3) errors.Add("Nome do aluno inválido.");

        ClassroomKey? classroom = null;
        string? externalCode = null;
        var classroomText = Get(ImportField.Classroom);
        if (classroomText is null) errors.Add("Turma em branco.");
        else if (!ClassroomLabel.TryParse(classroomText, out var key, out externalCode)) errors.Add("Turma em formato não reconhecido.");
        else classroom = key;

        var code = Get(ImportField.Code);
        if (code is not null)
        {
            code = ImportFields.Digits(code);
            if (code is null || code.Length is < 6 or > 15) { errors.Add("CGM inválido (use só números)."); code = null; }
        }

        DateOnly? birth = null;
        if (Get(ImportField.BirthDate) is { } birthText)
        {
            if (ImportFields.TryParseDate(birthText, out var b)) birth = b;
            else warnings.Add("Data de nascimento não reconhecida; ficará em branco.");
        }

        var email = Get(ImportField.Email)?.ToLowerInvariant();
        if (email is not null && !email.Contains('@', StringComparison.Ordinal)) { warnings.Add("E-mail inválido; ficará em branco."); email = null; }

        int? call = int.TryParse(Get(ImportField.CallNumber), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is > 0 and < 1000 ? n : null;

        GuardianImport? guardian = null;
        if (Get(ImportField.GuardianName) is { } guardianName && guardianName.Count(char.IsLetter) >= 3)
        {
            guardian = new GuardianImport(guardianName, Get(ImportField.GuardianDocument), Get(ImportField.GuardianPhone), Relationship(Get(ImportField.GuardianRelationship)));
        }

        return new ParsedRow(code, name, classroom, externalCode, birth, email, Get(ImportField.Address), call, guardian, errors, warnings);
    }

    internal static GuardianRelationship Relationship(string? value) => ImportFields.Normalize(value) switch
    {
        "MAE" => GuardianRelationship.Mae,
        "PAI" => GuardianRelationship.Pai,
        "AVOS" or "AVO" => GuardianRelationship.Avos,
        "PROPRIO ALUNO" => GuardianRelationship.ProprioAluno,
        "RESPONSAVEL" or "RESPONSAVEL LEGAL" or "TUTOR" => GuardianRelationship.Responsavel,
        _ => GuardianRelationship.Outro,
    };

    /// <summary>Mesmo aluno se compartilha o primeiro nome e ao menos mais uma parte do nome.</summary>
    internal static bool SimilarNames(string a, string b)
    {
        var x = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var y = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return x.Length > 0 && y.Contains(x[0]) && x.Skip(1).Any(y.Contains);
    }

    private static IEnumerable<string> Diff(StudentSnapshot current, ParsedRow p, Guid targetClassroom)
    {
        if (p.Name is { } name && name != current.FullName) yield return "nome";
        if (p.BirthDate is { } birth && birth != current.BirthDate) yield return "nascimento";
        if (p.Email is { } email && email != current.InstitutionalEmail) yield return "e-mail";
        if (targetClassroom == Guid.Empty || targetClassroom != current.ActiveClassroomId) yield return "turma";
        else if (p.CallNumber is { } call && call != current.CallNumber) yield return "nº de chamada";
        if (p.Address is { } address && address != current.Address) yield return "endereço";
        if (p.Guardian is { } g)
        {
            var match = current.Guardians.FirstOrDefault(x => (g.Document is not null && x.Document == g.Document) || Student.ToSearch(x.Name) == Student.ToSearch(g.Name));
            if (match is null) yield return "responsável";
            else if ((g.Phone is not null && g.Phone != match.Phone) || (g.Document is not null && g.Document != match.Document)) yield return "contato do responsável";
        }
    }

    private static ImportTotals Count(List<ImportRow> rows, int newClassrooms) => new(
        Rows: rows.Count(r => r.Action != RowAction.Empty),
        New: rows.Count(r => r.Action == RowAction.New),
        Update: rows.Count(r => r.Action == RowAction.Update),
        Unchanged: rows.Count(r => r.Action == RowAction.Unchanged),
        PossibleDuplicate: rows.Count(r => r.Action == RowAction.PossibleDuplicate),
        Conflict: rows.Count(r => r.Action == RowAction.Conflict),
        Rejected: rows.Count(r => r.Action == RowAction.Rejected),
        Empty: rows.Count(r => r.Action == RowAction.Empty),
        Pending: rows.Count(r => r.Action is RowAction.PossibleDuplicate or RowAction.Conflict),
        NewClassrooms: newClassrooms);
}
