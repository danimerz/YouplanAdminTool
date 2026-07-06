using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.Core.Abstractions;

public sealed record AbsenceRequestQuery(
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<AbsenceRequestStatus>? Statuses = null,
    long? EmployeeId = null);
