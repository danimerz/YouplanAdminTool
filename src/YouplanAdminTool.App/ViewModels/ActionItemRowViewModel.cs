namespace YouplanAdminTool.App.ViewModels;

/// <summary>Eine Zeile in der "Offene Posten für SAP"-Liste.</summary>
public sealed class ActionItemRowViewModel : IHasEmployeeId
{
    public required long Id { get; init; }
    public required long EmployeeId { get; init; }
    public required string EmployeeName { get; init; }
    public required string DepartmentName { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required string AbsenceTypeDisplay { get; init; }
    public required string StatusDisplay { get; init; }
    public required string ActionDisplay { get; init; }
    public string? Note { get; init; }

    public string Zeitraum => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}";
}
