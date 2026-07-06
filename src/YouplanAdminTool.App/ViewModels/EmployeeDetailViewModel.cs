namespace YouplanAdminTool.App.ViewModels;

/// <summary>Momentaufnahme aller aktuell geladenen Abwesenheiten eines einzelnen Mitarbeiters,
/// angezeigt in der Detailansicht (Doppelklick auf eine Zeile der Ferien-Übersicht).</summary>
public sealed class EmployeeDetailViewModel
{
    public required string EmployeeName { get; init; }
    public required string DepartmentName { get; init; }
    public required IReadOnlyList<AbsenceRowViewModel> Absences { get; init; }

    public string WindowTitle => $"{EmployeeName} – Abwesenheiten";
}
