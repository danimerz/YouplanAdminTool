namespace YouplanAdminTool.Core.Models;

public sealed record AbsenceRequest(
    long Id,
    long EmployeeId,
    AbsenceRequestStatus Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Note,
    IReadOnlyList<AbsenceAccountReference> Accounts)
{
    /// <summary>Bester Anhaltspunkt für die Abwesenheitsart dieses Antrags (erstes verknüpftes Konto).</summary>
    public AbsenceType PrimaryAbsenceType => Accounts.Count > 0 ? Accounts[0].AbsenceType : AbsenceType.Unknown;
}
