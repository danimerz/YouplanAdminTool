using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.Core.Services;

public sealed class StatusChangeDetector : IStatusChangeDetector
{
    public IReadOnlyList<AbsenceActionItem> DetectActionsNeeded(
        IReadOnlyList<AbsenceRequest> currentRequests,
        IReadOnlyDictionary<long, AbsenceRequestStatus> previousStatuses)
    {
        var items = new List<AbsenceActionItem>();

        foreach (var request in currentRequests)
        {
            var hadPreviousStatus = previousStatuses.TryGetValue(request.Id, out var previousStatus);

            if (request.Status == AbsenceRequestStatus.Approved
                && (!hadPreviousStatus || previousStatus != AbsenceRequestStatus.Approved))
            {
                items.Add(new AbsenceActionItem(request, SapAction.Add));
            }
            else if (request.Status is AbsenceRequestStatus.Cancelled or AbsenceRequestStatus.Declined
                && hadPreviousStatus && previousStatus == AbsenceRequestStatus.Approved)
            {
                items.Add(new AbsenceActionItem(request, SapAction.Remove));
            }
        }

        return items;
    }
}
