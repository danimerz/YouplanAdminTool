namespace YouplanAdminTool.Infrastructure.Options;

public sealed class PlandayOptions
{
    public const string SectionName = "Planday";

    public string ClientId { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string AuthBaseUrl { get; set; } = "https://id.planday.com";

    public string ApiBaseUrl { get; set; } = "https://openapi.planday.com";
}
