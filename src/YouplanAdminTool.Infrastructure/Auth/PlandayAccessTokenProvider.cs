using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Infrastructure.Options;

namespace YouplanAdminTool.Infrastructure.Auth;

/// <summary>Beschafft OAuth2-Access-Tokens von Planday über den Refresh-Token-Flow und hält sie
/// bis kurz vor Ablauf im Speicher (Thread-sicher), damit nicht vor jedem API-Call neu authentifiziert werden muss.</summary>
public sealed class PlandayAccessTokenProvider : IAccessTokenProvider
{
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly PlandayOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedAccessToken;
    private DateTimeOffset _cachedExpiryUtc = DateTimeOffset.MinValue;

    public PlandayAccessTokenProvider(HttpClient httpClient, IOptions<PlandayOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _cachedExpiryUtc)
        {
            return _cachedAccessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _cachedExpiryUtc)
            {
                return _cachedAccessToken;
            }

            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _options.ClientId,
                ["refresh_token"] = _options.RefreshToken,
            });

            using var response = await _httpClient.PostAsync("/connect/token", requestContent, cancellationToken);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Planday hat keine gültige Token-Antwort geliefert.");

            _cachedAccessToken = token.AccessToken;
            _cachedExpiryUtc = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(token.ExpiresInSeconds) - ExpiryBuffer;

            return _cachedAccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate()
    {
        _cachedAccessToken = null;
        _cachedExpiryUtc = DateTimeOffset.MinValue;
    }
}
