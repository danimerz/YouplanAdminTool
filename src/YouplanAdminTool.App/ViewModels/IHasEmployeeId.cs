namespace YouplanAdminTool.App.ViewModels;

/// <summary>Implementiert von Zeilen-ViewModels, die einem Mitarbeiter zugeordnet sind, damit z.B.
/// ein einzelner Doppelklick-Handler für mehrere Grid-Zeilentypen (Ferien-Übersicht, Offene Posten)
/// die Mitarbeiter-Detailansicht öffnen kann.</summary>
public interface IHasEmployeeId
{
    long EmployeeId { get; }
}
