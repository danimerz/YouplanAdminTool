using System.Text.Json.Serialization;

namespace YouplanAdminTool.Infrastructure.Hr;

internal sealed class EmployeeDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("departments")]
    public List<long>? Departments { get; set; }
}

internal sealed class DepartmentDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
