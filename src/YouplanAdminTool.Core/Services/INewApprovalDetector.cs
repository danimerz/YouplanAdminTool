using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.Core.Services;

public interface INewApprovalDetector
{
    /// <summary>Liefert die genehmigten Anträge aus <paramref name="currentRequests"/>, deren Id noch nicht in
    /// <paramref name="previouslySeenApprovedIds"/> enthalten ist.</summary>
    IReadOnlyList<AbsenceRequest> DetectNewApprovals(
        IReadOnlyList<AbsenceRequest> currentRequests,
        IReadOnlySet<long> previouslySeenApprovedIds);
}
