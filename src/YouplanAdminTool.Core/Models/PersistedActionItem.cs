namespace YouplanAdminTool.Core.Models;

/// <summary>Ein lokal gespeicherter, noch offener SAP-Aktionsposten. Enthält genug Anzeigedaten,
/// um dargestellt zu werden, auch wenn der zugehörige Antrag gerade nicht im geladenen Zeitraum liegt.</summary>
public sealed record PersistedActionItem(
    long AbsenceRequestId,
    long EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string AccountName,
    string? Note,
    SapAction Action,
    AbsenceRequestStatus Status);
