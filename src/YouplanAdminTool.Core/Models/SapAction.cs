namespace YouplanAdminTool.Core.Models;

/// <summary>Welche manuelle Aktion in SAP Glohria für einen Abwesenheitsantrag nötig ist.</summary>
public enum SapAction
{
    /// <summary>Antrag wurde (neu) genehmigt und muss in SAP eingetragen werden.</summary>
    Add,

    /// <summary>Ein zuvor genehmigter Antrag wurde storniert/abgelehnt und muss in SAP rückgängig gemacht werden.</summary>
    Remove,
}
