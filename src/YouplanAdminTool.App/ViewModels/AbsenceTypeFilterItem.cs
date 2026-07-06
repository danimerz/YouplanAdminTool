using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.App.ViewModels;

/// <summary>Eintrag für die Art-Filter-ComboBox. Value=null steht für "Alle Arten".</summary>
public sealed record AbsenceTypeFilterItem(AbsenceType? Value, string Display)
{
    public override string ToString() => Display;
}
