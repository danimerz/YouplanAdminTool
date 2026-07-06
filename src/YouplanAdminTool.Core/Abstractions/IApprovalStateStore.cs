namespace YouplanAdminTool.Core.Abstractions;

/// <summary>Speichert lokal, welche genehmigten Abwesenheitsanträge dem Benutzer bereits angezeigt wurden.
/// Ermöglicht die Erkennung neu genehmigter Anträge seit der letzten Abfrage, auch über App-Neustarts hinweg.</summary>
public interface IApprovalStateStore
{
    Task<IReadOnlySet<long>> GetSeenApprovedIdsAsync(CancellationToken cancellationToken = default);

    Task MarkAsSeenAsync(IEnumerable<long> absenceRequestIds, CancellationToken cancellationToken = default);
}
