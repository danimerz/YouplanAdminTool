using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.App.Display;

/// <summary>Übersetzt Core-Enums in deutsche Anzeigetexte für die UI.</summary>
internal static class AbsenceDisplayText
{
    public static string ForType(AbsenceType type) => type switch
    {
        AbsenceType.Vacation => "Urlaub",
        AbsenceType.Absence => "Abwesenheit",
        AbsenceType.Flextime => "Gleitzeit",
        AbsenceType.Accrued => "Aufgelaufen",
        _ => "Unbekannt",
    };

    public static string ForStatus(AbsenceRequestStatus status) => status switch
    {
        AbsenceRequestStatus.Submitted => "Eingereicht",
        AbsenceRequestStatus.Approved => "Genehmigt",
        AbsenceRequestStatus.Declined => "Abgelehnt",
        AbsenceRequestStatus.Cancelled => "Storniert",
        _ => "Unbekannt",
    };

    public static string ForAction(SapAction action) => action switch
    {
        SapAction.Add => "In SAP eintragen",
        SapAction.Remove => "In SAP stornieren",
        _ => "Unbekannt",
    };
}
