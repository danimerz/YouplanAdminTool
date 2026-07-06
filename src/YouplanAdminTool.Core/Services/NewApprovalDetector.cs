using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.Core.Services;

public sealed class NewApprovalDetector : INewApprovalDetector
{
    public IReadOnlyList<AbsenceRequest> DetectNewApprovals(
        IReadOnlyList<AbsenceRequest> currentRequests,
        IReadOnlySet<long> previouslySeenApprovedIds)
    {
        return currentRequests
            .Where(r => r.Status == AbsenceRequestStatus.Approved && !previouslySeenApprovedIds.Contains(r.Id))
            .ToList();
    }
}
