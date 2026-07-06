using System.Text.Json.Serialization;

namespace YouplanAdminTool.Infrastructure.Http;

internal sealed class ApiDataResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

internal sealed class PagedApiDataResponse<T>
{
    [JsonPropertyName("data")]
    public List<T>? Data { get; set; }

    [JsonPropertyName("paging")]
    public PagingDto? Paging { get; set; }
}

internal sealed class PagingDto
{
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("total")]
    public long Total { get; set; }
}
