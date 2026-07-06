using Microsoft.Data.Sqlite;
using MsOptions = Microsoft.Extensions.Options.Options;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Options;
using YouplanAdminTool.Infrastructure.Persistence;

namespace YouplanAdminTool.Infrastructure.Tests;

public class SqliteAbsenceProcessingStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"youplan-processing-{Guid.NewGuid()}.db");

    private SqliteAbsenceProcessingStore CreateSut() =>
        new(MsOptions.Create(new AppOptions { DatabasePath = _dbPath }));

    private static AbsenceRequest CreateRequest(long id, AbsenceRequestStatus status, long employeeId = 1) =>
        new(
            Id: id,
            EmployeeId: employeeId,
            Status: status,
            StartDate: new DateOnly(2026, 7, 1),
            EndDate: new DateOnly(2026, 7, 5),
            Note: "Testnotiz",
            Accounts: [new AbsenceAccountReference(10, "Ferien", AbsenceType.Vacation)]);

    [Fact]
    public async Task NewActionItemBecomesVisibleAsOpenItem()
    {
        var sut = CreateSut();
        var request = CreateRequest(1, AbsenceRequestStatus.Approved);

        await sut.ApplyRefreshAsync([request], [new AbsenceActionItem(request, SapAction.Add)]);

        var openItems = await sut.GetOpenItemsAsync();
        var item = Assert.Single(openItems);
        Assert.Equal(1, item.AbsenceRequestId);
        Assert.Equal(SapAction.Add, item.Action);
        Assert.Equal("Ferien", item.AccountName);
    }

    [Fact]
    public async Task RequestWithoutNewActionDoesNotBecomeOpenItem()
    {
        var sut = CreateSut();
        var request = CreateRequest(1, AbsenceRequestStatus.Submitted);

        await sut.ApplyRefreshAsync([request], []);

        var openItems = await sut.GetOpenItemsAsync();
        Assert.Empty(openItems);
    }

    [Fact]
    public async Task LastKnownStatusIsUpdatedEvenWithoutNewAction()
    {
        var sut = CreateSut();
        var request = CreateRequest(1, AbsenceRequestStatus.Submitted);
        await sut.ApplyRefreshAsync([request], []);

        var updated = request with { Status = AbsenceRequestStatus.Approved };
        await sut.ApplyRefreshAsync([updated], []); // simulate no action detected this cycle (detector runs separately)

        var statuses = await sut.GetLastKnownStatusesAsync();
        Assert.Equal(AbsenceRequestStatus.Approved, statuses[1]);
    }

    [Fact]
    public async Task MarkingCompletedRemovesItFromOpenItems()
    {
        var sut = CreateSut();
        var request = CreateRequest(1, AbsenceRequestStatus.Approved);
        await sut.ApplyRefreshAsync([request], [new AbsenceActionItem(request, SapAction.Add)]);

        await sut.SetCompletedAsync(1, true);

        var openItems = await sut.GetOpenItemsAsync();
        Assert.Empty(openItems);
    }

    [Fact]
    public async Task NewActionResetsPreviouslyCompletedItem()
    {
        var sut = CreateSut();
        var request = CreateRequest(1, AbsenceRequestStatus.Approved);
        await sut.ApplyRefreshAsync([request], [new AbsenceActionItem(request, SapAction.Add)]);
        await sut.SetCompletedAsync(1, true);

        // Antrag wird später storniert -> neue Aktion, obwohl vorheriger Posten erledigt war.
        var cancelled = request with { Status = AbsenceRequestStatus.Cancelled };
        await sut.ApplyRefreshAsync([cancelled], [new AbsenceActionItem(cancelled, SapAction.Remove)]);

        var openItems = await sut.GetOpenItemsAsync();
        var item = Assert.Single(openItems);
        Assert.Equal(SapAction.Remove, item.Action);
    }

    [Fact]
    public async Task MigratesLegacySeenApprovedRequestsAsKnownApproved()
    {
        var dbPath = _dbPath;
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE SeenApprovedRequests (AbsenceRequestId INTEGER PRIMARY KEY, SeenAtUtc TEXT NOT NULL);
                INSERT INTO SeenApprovedRequests (AbsenceRequestId, SeenAtUtc) VALUES (42, '2026-01-01T00:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var sut = CreateSut();
        var statuses = await sut.GetLastKnownStatusesAsync();

        Assert.Equal(AbsenceRequestStatus.Approved, statuses[42]);

        // Ein danach als "neu genehmigt" erkannter Posten für dieselbe Id soll NICHT nochmal als offen auftauchen,
        // wenn der Status weiterhin Approved ist (Detector würde das separat prüfen; hier nur der Store-Seed-Effekt).
        var openItems = await sut.GetOpenItemsAsync();
        Assert.Empty(openItems);
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite poolt native Verbindungen; ohne ClearAllPools() hält der Pool
        // die Datei noch offen und File.Delete schlägt mit IOException fehl.
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
