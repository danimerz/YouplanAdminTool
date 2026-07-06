using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Core.Services;

namespace YouplanAdminTool.Core.Tests;

public class StatusChangeDetectorTests
{
    private readonly StatusChangeDetector _sut = new();

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
    public void FlagsNewlyApprovedRequestAsAdd()
    {
        var current = new[] { CreateRequest(1, AbsenceRequestStatus.Approved) };
        var previous = new Dictionary<long, AbsenceRequestStatus>();

        var result = _sut.DetectActionsNeeded(current, previous);

        var item = Assert.Single(result);
        Assert.Equal(1, item.Request.Id);
        Assert.Equal(SapAction.Add, item.Action);
    }

    [Fact]
    public void FlagsPreviouslyApprovedNowCancelledAsRemove()
    {
        var current = new[] { CreateRequest(1, AbsenceRequestStatus.Cancelled) };
        var previous = new Dictionary<long, AbsenceRequestStatus> { [1] = AbsenceRequestStatus.Approved };

        var result = _sut.DetectActionsNeeded(current, previous);

        var item = Assert.Single(result);
        Assert.Equal(1, item.Request.Id);
        Assert.Equal(SapAction.Remove, item.Action);
    }

    [Fact]
    public void FlagsPreviouslyApprovedNowDeclinedAsRemove()
    {
        var current = new[] { CreateRequest(1, AbsenceRequestStatus.Declined) };
        var previous = new Dictionary<long, AbsenceRequestStatus> { [1] = AbsenceRequestStatus.Approved };

        var result = _sut.DetectActionsNeeded(current, previous);

        var item = Assert.Single(result);
        Assert.Equal(SapAction.Remove, item.Action);
    }

    [Fact]
    public void DoesNotFlagDeclinedRequestThatWasNeverApproved()
    {
        var current = new[] { CreateRequest(1, AbsenceRequestStatus.Declined) };
        var previous = new Dictionary<long, AbsenceRequestStatus> { [1] = AbsenceRequestStatus.Submitted };

        var result = _sut.DetectActionsNeeded(current, previous);

        Assert.Empty(result);
    }

    [Fact]
    public void DoesNotFlagCancelledRequestNeverSeenBefore()
    {
        // Erstes Sichten eines bereits stornierten Antrags: wir wissen nicht, ob er je genehmigt/
        // in SAP erfasst war, daher keine Aktion.
        var current = new[] { CreateRequest(1, AbsenceRequestStatus.Cancelled) };
        var previous = new Dictionary<long, AbsenceRequestStatus>();

        var result = _sut.DetectActionsNeeded(current, previous);

        Assert.Empty(result);
    }

    [Fact]
    public void DoesNotFlagUnchangedApprovedRequest()
    {
        var current = new[] { CreateRequest(1, AbsenceRequestStatus.Approved) };
        var previous = new Dictionary<long, AbsenceRequestStatus> { [1] = AbsenceRequestStatus.Approved };

        var result = _sut.DetectActionsNeeded(current, previous);

        Assert.Empty(result);
    }

    [Fact]
    public void DoesNotFlagSubmittedRequest()
    {
        var current = new[] { CreateRequest(1, AbsenceRequestStatus.Submitted) };
        var previous = new Dictionary<long, AbsenceRequestStatus>();

        var result = _sut.DetectActionsNeeded(current, previous);

        Assert.Empty(result);
    }

    [Fact]
    public void ReturnsEmptyForEmptyInput()
    {
        var result = _sut.DetectActionsNeeded([], new Dictionary<long, AbsenceRequestStatus>());

        Assert.Empty(result);
    }
}
