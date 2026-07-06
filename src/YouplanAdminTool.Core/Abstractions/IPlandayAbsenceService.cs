using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.Core.Abstractions;

/// <summary>Zugriff auf die Absence-API von Planday (Abwesenheitsanträge und Kontoarten).</summary>
public interface IPlandayAbsenceService
{
    Task<IReadOnlyList<AbsenceRequest>> GetAbsenceRequestsAsync(AbsenceRequestQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountType>> GetAccountTypesAsync(CancellationToken cancellationToken = default);
}
