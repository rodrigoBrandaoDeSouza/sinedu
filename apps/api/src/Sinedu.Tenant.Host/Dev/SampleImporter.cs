using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.Tenant.Imports;
using Sinedu.Tenant.School;

namespace Sinedu.Tenant.Host.Dev;

/// <summary>
/// Carrega uma planilha ANONIMIZADA (.doc/amostras) numa escola de desenvolvimento usando o MESMO
/// pipeline da importação real (envio → mapeamento sugerido → prévia → confirmação). Linhas que exigem
/// revisão (possíveis duplicados, conflitos) são ignoradas e listadas no resultado.
/// Uso: dotnet run --project src/Sinedu.Tenant.Host -- import-sample escola-teste /caminho/absoluto/amostra.xlsx [aba]
/// </summary>
internal static class SampleImporter
{
    public static async Task RunAsync(IServiceProvider root, string slug, string path, string? sheetName)
    {
        if (!root.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            throw new InvalidOperationException("import-sample só pode rodar em Development.");
        }

        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var resolution = await sp.GetRequiredService<ITenantResolver>().ResolveAsync(slug);
        if (resolution.Status != TenantResolutionStatus.Ready)
        {
            throw new InvalidOperationException($"Escola '{slug}' indisponível: {resolution.Status}.");
        }

        sp.GetRequiredService<TenantScope>().Set(resolution.Tenant!);
        await EnsureActiveYearAsync(sp.GetRequiredService<SchoolDbContext>(), DateTime.UtcNow.Year);

        var imports = sp.GetRequiredService<ImportService>();
        await using var file = File.OpenRead(path);
        var batch = await imports.UploadAsync(file, Path.GetFileName(path), Guid.Empty, CancellationToken.None);

        var sheets = ImportService.Sheets(batch);
        var sheet = sheets.FirstOrDefault(s => s.Name == sheetName) ?? sheets.OrderByDescending(s => s.RowCount).First();
        var (mapping, _) = await imports.SuggestAsync(batch, sheet.Name, sheet.HeaderRow, CancellationToken.None);
        Console.WriteLine($"Aba \"{sheet.Name}\" (cabeçalho na linha {sheet.HeaderRow}); colunas mapeadas: {string.Join(", ", mapping.Select(m => $"{m.Key}=#{m.Value + 1}"))}");

        var preview = await imports.MapAndValidateAsync(batch, sheet.Name, sheet.HeaderRow, mapping, CancellationToken.None);
        Console.WriteLine($"Prévia: {preview.New} novos, {preview.Update} atualizações, {preview.Unchanged} sem alteração, " +
            $"{preview.PossibleDuplicate} possíveis duplicados, {preview.Conflict} conflitos, {preview.Rejected} rejeitados, {preview.NewClassrooms} turmas novas.");

        await imports.SkipPendingAsync(batch, Guid.Empty, CancellationToken.None);
        var result = await imports.ConfirmAsync(batch, Guid.Empty, CancellationToken.None);
        Console.WriteLine($"Escola {slug}: {result.Created} criados, {result.Updated} atualizados, {result.Unchanged} sem alteração, " +
            $"{result.Skipped} ignorados (revisão pendente/rejeitados), {result.Failed} falhas. Lote {batch.Id}.");
    }

    private static async Task EnsureActiveYearAsync(SchoolDbContext db, int value)
    {
        if (await db.Years.AnyAsync(y => y.Status == SchoolYearStatus.Active))
        {
            return;
        }

        var year = new SchoolYear
        {
            Id = Guid.CreateVersion7(), Year = value, StartsOn = new DateOnly(value, 2, 2), EndsOn = new DateOnly(value, 12, 18), Status = SchoolYearStatus.Active,
        };
        year.Terms.AddRange(SchoolModule.StandardTerms(year));
        db.Years.Add(year);
        await db.SaveChangesAsync();
    }
}
