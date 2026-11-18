using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sinedu.SharedKernel.Web;

namespace Sinedu.Tenant.Access;

public sealed record PlatformInviteResult(Guid? UserId, bool PlatformAccount);

/// <summary>Chamadas à API interna da plataforma (convites e vínculos), autenticadas por client_credentials.</summary>
public interface IPlatformMemberships
{
    Task<PlatformInviteResult> InviteAsync(string tenantSlug, string email, string fullName, bool privileged, CancellationToken ct);

    Task SyncAsync(string tenantSlug, Guid userId, bool active, bool privileged, CancellationToken ct);
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

    private async Task<HttpRequestMessage> NewRequestAsync(string path, object body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(urls.Value.AccountUrl, path))
        {
            Content = JsonContent.Create(body),
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
