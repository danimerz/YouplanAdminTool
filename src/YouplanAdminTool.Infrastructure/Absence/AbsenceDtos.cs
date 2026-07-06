using System.Text.Json.Serialization;

namespace YouplanAdminTool.Infrastructure.Absence;

internal sealed class AbsenceRequestDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("employeeId")]
    public long? EmployeeId { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("absencePeriod")]
    public AbsencePeriodDto? AbsencePeriod { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("requestedAccounts")]
    public List<RequestedAccountDto>? RequestedAccounts { get; set; }
}

internal sealed class AbsencePeriodDto
{
    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }
}

internal sealed class RequestedAccountDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class AccountTypeDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("absenceType")]
    public string? AbsenceType { get; set; }
}

/// <summary>Eine konkrete Konto-Instanz eines Mitarbeiters (z.B. "Marias Urlaubskonto").
/// <see cref="RequestedAccountDto.Id"/> in einem Abwesenheitsantrag referenziert diese Id,
/// nicht direkt die Kontoart - die Abwesenheitsart ergibt sich erst über <see cref="TypeId"/>.</summary>
internal sealed class AccountDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("typeId")]
    public long? TypeId { get; set; }
}
