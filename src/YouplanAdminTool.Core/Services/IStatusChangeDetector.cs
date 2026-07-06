using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.Core.Services;

public interface IStatusChangeDetector
{
    /// <summary>Vergleicht die aktuell geladenen Anträge mit den zuletzt bekannten Stati und liefert,
    /// welche Anträge eine neue SAP-Aktion auslösen (neu genehmigt, oder nach vorheriger Genehmigung
    /// storniert/abgelehnt).</summary>
    IReadOnlyList<AbsenceActionItem> DetectActionsNeeded(
        IReadOnlyList<AbsenceRequest> currentRequests,
        IReadOnlyDictionary<long, AbsenceRequestStatus> previousStatuses);
}
