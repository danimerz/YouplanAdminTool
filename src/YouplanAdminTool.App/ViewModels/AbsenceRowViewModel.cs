namespace YouplanAdminTool.App.ViewModels;

/// <summary>Eine Zeile in der Ferien-Übersicht, bereits für die Anzeige aufbereitet (deutsche Texte).</summary>
public sealed class AbsenceRowViewModel
{
    public required long Id { get; init; }
    public required long EmployeeId { get; init; }
    public required string EmployeeName { get; init; }
    public required string DepartmentName { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required string AbsenceTypeDisplay { get; init; }
    public required string StatusDisplay { get; init; }
    public string? Note { get; init; }
    public required bool IsNew { get; init; }

    public string Zeitraum => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}";
}
