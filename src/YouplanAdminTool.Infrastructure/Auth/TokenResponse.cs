using System.Text.Json.Serialization;

namespace YouplanAdminTool.Infrastructure.Auth;

internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresInSeconds { get; set; }
}
