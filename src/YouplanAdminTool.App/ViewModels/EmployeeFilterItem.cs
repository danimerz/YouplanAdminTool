namespace YouplanAdminTool.App.ViewModels;

/// <summary>Eintrag für die Mitarbeiterfilter-ComboBox. Value=null steht für "Alle Mitarbeiter".</summary>
public sealed record EmployeeFilterItem(long? Value, string Display)
{
    public override string ToString() => Display;
}
