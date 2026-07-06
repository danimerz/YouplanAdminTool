namespace YouplanAdminTool.App.ViewModels;

/// <summary>Eintrag für die Abteilungsfilter-ComboBox. Value=null steht für "Alle Abteilungen".</summary>
public sealed record DepartmentFilterItem(long? Value, string Display)
{
    public override string ToString() => Display;
}
