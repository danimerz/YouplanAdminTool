namespace YouplanAdminTool.Core.Models;

/// <summary>Vom Benutzer in der UI gewählte Einstellungen, die über App-Neustarts hinweg erhalten bleiben sollen.</summary>
public sealed record UserSettings(
    int? PollingIntervalMinutes,
    AbsenceType? AbsenceTypeFilter,
    long? DepartmentFilterId)
{
    public static UserSettings Empty { get; } = new(null, null, null);
}
