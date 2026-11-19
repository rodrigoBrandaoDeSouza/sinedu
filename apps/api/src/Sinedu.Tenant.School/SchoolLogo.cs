using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sinedu.SharedKernel.Audit;
using Sinedu.SharedKernel.Storage;
using Sinedu.SharedKernel.Tenancy;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Tenant.School;

/// <summary>
/// Logo da escola (F5-05): imagem institucional, sem dado pessoal, guardada no bucket da própria escola.
/// Usada no cabeçalho da ata. Só PNG ou JPG (conferido pelos bytes), até 512 KB.
/// </summary>
public static class SchoolLogo
{
    public const string Key = "school/logo";
    public const long MaxBytes = 512 * 1024;

    public static async Task<byte[]?> ReadAsync(IObjectStorage storage, TenantScope tenant, CancellationToken ct) =>
        await storage.GetAsync(tenant.Current.BucketName, Key, ct) is { Length: > 0 } bytes ? bytes : null;

    internal static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/profile/logo", async (IObjectStorage storage, TenantScope tenant, CancellationToken ct) =>
            await ReadAsync(storage, tenant, ct) is { } logo && DocumentTypes.Detect(logo) is { } type && type.ContentType.StartsWith("image/", StringComparison.Ordinal)
                ? Results.File(logo, type.ContentType)
                : Results.NotFound());

        api.MapPut("/profile/logo", async (HttpRequest request, IObjectStorage storage, TenantScope tenant, IAuditLog audit, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0 || file.Length > MaxBytes)
            {
                return Invalid("Envie uma imagem PNG ou JPG de até 512 KB.");
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (DocumentTypes.Detect(bytes) is not { } type || !type.ContentType.StartsWith("image/", StringComparison.Ordinal))
            {
                return Invalid("O arquivo precisa ser uma imagem PNG ou JPG.");
            }

            await storage.PutAsync(tenant.Current.BucketName, Key, bytes, ct);
            await audit.WriteAsync(new AuditEntry("school_logo_updated", ResourceType: "school_profile"), ct);
            return Results.NoContent();
        }).DisableAntiforgery().WithMetadata(new MaxRequestSize(MaxBytes + 64_000)).RequireAuthorization(Policies.SchoolConfig);

        api.MapDelete("/profile/logo", async (IObjectStorage storage, TenantScope tenant, IAuditLog audit, CancellationToken ct) =>
        {
            // O armazenamento não tem "apagar objeto": um objeto vazio vale como "sem logo".
            await storage.PutAsync(tenant.Current.BucketName, Key, [], ct);
            await audit.WriteAsync(new AuditEntry("school_logo_removed", ResourceType: "school_profile"), ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.SchoolConfig);
    }

    private static IResult Invalid(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
}
