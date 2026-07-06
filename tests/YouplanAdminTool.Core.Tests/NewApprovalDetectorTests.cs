using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Core.Services;

namespace YouplanAdminTool.Core.Tests;

public class NewApprovalDetectorTests
{
    private readonly NewApprovalDetector _sut = new();

    private static AbsenceRequest CreateRequest(long id, AbsenceRequestStatus status) =>
        new(
            Id: id,
            EmployeeId: 1,
            Status: status,
            StartDate: new DateOnly(2026, 7, 1),
            EndDate: new DateOnly(2026, 7, 5),
            Note: null,
            Accounts: []);

    [Fact]
    public void ReturnsApprovedRequestsNotYetSeen()
    {
        var current = new[]
        {
            CreateRequest(1, AbsenceRequestStatus.Approved),
            CreateRequest(2, AbsenceRequestStatus.Approved),
        };
        var seen = new HashSet<long> { 1 };

        var result = _sut.DetectNewApprovals(current, seen);

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public void IgnoresNonApprovedRequests()
    {
        var current = new[]
        {
            CreateRequest(1, AbsenceRequestStatus.Submitted),
            CreateRequest(2, AbsenceRequestStatus.Declined),
            CreateRequest(3, AbsenceRequestStatus.Cancelled),
        };
        var seen = new HashSet<long>();

        var result = _sut.DetectNewApprovals(current, seen);

        Assert.Empty(result);
    }

    [Fact]
    public void ReturnsEmptyWhenAllApprovedRequestsAlreadySeen()
    {
        var current = new[]
        {
            CreateRequest(1, AbsenceRequestStatus.Approved),
            CreateRequest(2, AbsenceRequestStatus.Approved),
        };
        var seen = new HashSet<long> { 1, 2 };

        var result = _sut.DetectNewApprovals(current, seen);

        Assert.Empty(result);
    }

    [Fact]
    public void ReturnsAllApprovedRequestsWhenNothingSeenYet()
    {
        var current = new[]
        {
            CreateRequest(1, AbsenceRequestStatus.Approved),
            CreateRequest(2, AbsenceRequestStatus.Approved),
        };
        var seen = new HashSet<long>();

        var result = _sut.DetectNewApprovals(current, seen);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ReturnsEmptyForEmptyInput()
    {
        var result = _sut.DetectNewApprovals([], new HashSet<long>());

        Assert.Empty(result);
    }
}
