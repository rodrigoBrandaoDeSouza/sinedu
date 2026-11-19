using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Tenant.Access;

public sealed record PlatformInviteResult(Guid? UserId, bool PlatformAccount);

/// <summary>Desbloqueio ou reinício de MFA feito no painel sobre a conta de alguém desta escola (P-12).</summary>
public sealed record PlatformAccountEvent(long Id, string Action, Guid UserId, DateTimeOffset OccurredAt);

/// <summary>Pedido de suporte como a plataforma o devolve (sem dados de aluno).</summary>
public sealed record PlatformSupportRequest(
    Guid Id, Guid UserId, string RequesterName, string RequesterEmail, string Reason, int DurationHours,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

/// <summary>Chamadas à API interna da plataforma (convites e vínculos), autenticadas por client_credentials.</summary>
public interface IPlatformMemberships
{
    Task<PlatformInviteResult> InviteAsync(string tenantSlug, string email, string fullName, bool privileged, CancellationToken ct);

    Task SyncAsync(string tenantSlug, Guid userId, bool active, bool privileged, CancellationToken ct);

    /// <summary>Pedidos de suporte desta escola (P-11).</summary>
    Task<IReadOnlyList<PlatformSupportRequest>> SupportRequestsAsync(string tenantSlug, CancellationToken ct);

    /// <summary>Registra a decisão do Diretor. Nulo se o pedido não está mais pendente.</summary>
    Task<PlatformSupportRequest?> DecideSupportAsync(string tenantSlug, Guid requestId, bool approve, string decidedBy, CancellationToken ct);

    Task RevokeSupportAsync(string tenantSlug, Guid requestId, string revokedBy, CancellationToken ct);

    /// <summary>Ações da plataforma sobre contas de pessoas desta escola, dos últimos 30 dias (P-12).</summary>
    Task<IReadOnlyList<PlatformAccountEvent>> AccountEventsAsync(string tenantSlug, CancellationToken ct);
}

internal sealed class PlatformMembershipsClient(HttpClient http, IOptions<SineduUrls> urls, IConfiguration configuration, TimeProvider clock)
    : IPlatformMemberships
{
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static (string Token, DateTimeOffset ExpiresAt)? _token;

    public async Task<PlatformInviteResult> InviteAsync(string tenantSlug, string email, string fullName, bool privileged, CancellationToken ct)
    {
        using var request = await NewRequestAsync("api/internal/invitations", new { tenantSlug, email, fullName, privileged }, ct);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return new PlatformInviteResult(null, PlatformAccount: true);
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<InviteResponse>(ct);
        return new PlatformInviteResult(body!.UserId, PlatformAccount: false);
    }

    public async Task SyncAsync(string tenantSlug, Guid userId, bool active, bool privileged, CancellationToken ct)
    {
        using var request = await NewRequestAsync("api/internal/memberships/sync", new { tenantSlug, userId, active, privileged }, ct);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<PlatformSupportRequest>> SupportRequestsAsync(string tenantSlug, CancellationToken ct)
    {
        using var request = await NewRequestAsync($"api/internal/support-requests?tenantSlug={Uri.EscapeDataString(tenantSlug)}", null, ct);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PlatformSupportRequest>>(ct) ?? [];
    }

    public async Task<PlatformSupportRequest?> DecideSupportAsync(string tenantSlug, Guid requestId, bool approve, string decidedBy, CancellationToken ct)
    {
        using var request = await NewRequestAsync($"api/internal/support-requests/{requestId}/decision", new { tenantSlug, approve, decidedBy }, ct);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlatformSupportRequest>(ct);
    }

    public async Task<IReadOnlyList<PlatformAccountEvent>> AccountEventsAsync(string tenantSlug, CancellationToken ct)
    {
        using var request = await NewRequestAsync($"api/internal/account-events?tenantSlug={Uri.EscapeDataString(tenantSlug)}", null, ct);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PlatformAccountEvent>>(ct) ?? [];
    }

    public async Task RevokeSupportAsync(string tenantSlug, Guid requestId, string revokedBy, CancellationToken ct)
    {
        using var request = await NewRequestAsync($"api/internal/support-requests/{requestId}/revoke", new { tenantSlug, revokedBy }, ct);
        using var response = await http.SendAsync(request, ct);
        // Já revogado/expirado na plataforma: a escola revoga localmente do mesmo jeito.
        if (response.StatusCode is not (System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound))
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<HttpRequestMessage> NewRequestAsync(string path, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, new Uri(urls.Value.AccountUrl, path))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
        return request;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is { } cached && cached.ExpiresAt > clock.GetUtcNow().AddSeconds(30))
        {
            return cached.Token;
        }

        await TokenLock.WaitAsync(ct);
        try
        {
            if (_token is { } again && again.ExpiresAt > clock.GetUtcNow().AddSeconds(30))
            {
                return again.Token;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(urls.Value.AccountUrl, "connect/token"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = OidcContract.TenantClientId,
                    ["client_secret"] = configuration["Oidc:TenantClientSecret"] ?? "",
                    ["scope"] = OidcContract.MembershipsScope,
                }),
            };
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            _token = (token!.AccessToken, clock.GetUtcNow().AddSeconds(token.ExpiresIn));
            return token.AccessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private sealed record InviteResponse(Guid UserId);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
