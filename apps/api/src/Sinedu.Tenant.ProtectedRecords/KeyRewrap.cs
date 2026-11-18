using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Secrets;
using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Tenant.ProtectedRecords;

public sealed record RewrapResult(int Versions, int Attachments, int Exports);

/// <summary>
/// Depois de rotacionar a chave da escola no Vault, recifra todas as DEKs com a versão nova (F4-09).
/// O Vault faz o rewrap sem devolver a DEK; o conteúdo e os anexos não são decifrados nem regravados.
/// </summary>
public sealed class KeyRewrap(ProtectedDbContext db, IDataKeyProvider keys, TenantScope tenant, IAuditLog audit)
{
    private const int Batch = 200;

    public async Task<RewrapResult> RunAsync(CancellationToken ct)
    {
        var key = tenant.Current.TransitKeyName;
        var versions = await RewrapAllAsync(db.Versions, async v => v.WrappedKey = await keys.RewrapAsync(key, v.WrappedKey, ct), ct);
        var attachments = await RewrapAllAsync(db.Attachments, async a =>
        {
            a.WrappedKey = await keys.RewrapAsync(key, a.WrappedKey, ct);
            a.MetadataWrappedKey = await keys.RewrapAsync(key, a.MetadataWrappedKey, ct);
        }, ct);
        var exports = await RewrapAllAsync(db.Exports, async e => e.JustificationWrappedKey = await keys.RewrapAsync(key, e.JustificationWrappedKey, ct), ct);

        await audit.WriteAsync(new AuditEntry("protected_keys_rewrapped", ResourceType: "protected"), ct);
        return new RewrapResult(versions, attachments, exports);
    }

    private async Task<int> RewrapAllAsync<T>(DbSet<T> set, Func<T, Task> rewrap, CancellationToken ct)
        where T : class
    {
        var total = 0;
        var skip = 0;
        while (true)
        {
            var page = await set.OrderBy(e => EF.Property<Guid>(e, "Id")).Skip(skip).Take(Batch).ToListAsync(ct);
            if (page.Count == 0)
            {
                return total;
            }

            foreach (var item in page)
            {
                await rewrap(item);
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += page.Count;
            skip += page.Count;
        }
    }
}
