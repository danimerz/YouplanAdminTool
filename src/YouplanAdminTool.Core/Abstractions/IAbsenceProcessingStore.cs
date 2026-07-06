using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.Core.Abstractions;

/// <summary>Persistiert lokal, welche Abwesenheitsanträge welchen Status zuletzt hatten und welche
/// SAP-Aktionsposten (Eintragen/Stornieren) noch offen sind. Ermöglicht die Erkennung von
/// Statusänderungen und ein echtes "erledigt"-Tracking über App-Neustarts hinweg.</summary>
public interface IAbsenceProcessingStore
{
    Task<IReadOnlyDictionary<long, AbsenceRequestStatus>> GetLastKnownStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>Aktualisiert die zuletzt bekannten Stati aller aktuell geladenen Anträge und legt für
    /// neu erkannte Aktionsposten offene Einträge an (bzw. setzt sie erneut auf "offen").</summary>
    Task ApplyRefreshAsync(
        IReadOnlyList<AbsenceRequest> currentRequests,
        IReadOnlyList<AbsenceActionItem> newActionItems,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersistedActionItem>> GetOpenItemsAsync(CancellationToken cancellationToken = default);

    Task SetCompletedAsync(long absenceRequestId, bool isCompleted, CancellationToken cancellationToken = default);
}
