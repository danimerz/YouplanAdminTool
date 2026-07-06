using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Options;

namespace YouplanAdminTool.Infrastructure.Persistence;

/// <summary>Persistiert lokal in SQLite, welchen Status ein Abwesenheitsantrag zuletzt hatte und
/// welche SAP-Aktionsposten (Eintragen/Stornieren) noch offen sind, damit Statusänderungen erkannt
/// und ein echtes "in SAP erledigt"-Tracking über App-Neustarts hinweg geführt werden können.</summary>
public sealed class SqliteAbsenceProcessingStore : IAbsenceProcessingStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public SqliteAbsenceProcessingStore(IOptions<AppOptions> options)
    {
        var databasePath = AppDataPathResolver.Resolve(options.Value.DatabasePath);
        _connectionString = $"Data Source={databasePath}";
    }

    public async Task<IReadOnlyDictionary<long, AbsenceRequestStatus>> GetLastKnownStatusesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT AbsenceRequestId, LastKnownStatus FROM AbsenceProcessing";

        var statuses = new Dictionary<long, AbsenceRequestStatus>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Enum.TryParse<AbsenceRequestStatus>(reader.GetString(1), out var status))
            {
                statuses[reader.GetInt64(0)] = status;
            }
        }

        return statuses;
    }

    public async Task ApplyRefreshAsync(
        IReadOnlyList<AbsenceRequest> currentRequests,
        IReadOnlyList<AbsenceActionItem> newActionItems,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var actionByRequestId = newActionItems.ToDictionary(item => item.Request.Id, item => item.Action);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        using var upsertKeepingAction = CreateUpsertCommand(connection, transaction, setsPendingAction: false);
        using var upsertWithNewAction = CreateUpsertCommand(connection, transaction, setsPendingAction: true);

        foreach (var request in currentRequests)
        {
            var hasNewAction = actionByRequestId.TryGetValue(request.Id, out var action);
            var command = hasNewAction ? upsertWithNewAction : upsertKeepingAction;

            SetUpsertParameters(command, request);
            if (hasNewAction)
            {
                command.Parameters["$pendingAction"].Value = action.ToString();
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PersistedActionItem>> GetOpenItemsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AbsenceRequestId, EmployeeId, StartDate, EndDate, AccountName, Note, PendingAction, LastKnownStatus
            FROM AbsenceProcessing
            WHERE PendingAction IS NOT NULL AND IsCompleted = 0
            """;

        var items = new List<PersistedActionItem>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<SapAction>(reader.GetString(6), out var action)
                || !Enum.TryParse<AbsenceRequestStatus>(reader.GetString(7), out var status))
            {
                continue;
            }

            items.Add(new PersistedActionItem(
                reader.GetInt64(0),
                reader.GetInt64(1),
                DateOnly.Parse(reader.GetString(2)),
                DateOnly.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                action,
                status));
        }

        return items;
    }

    public async Task SetCompletedAsync(long absenceRequestId, bool isCompleted, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AbsenceProcessing SET IsCompleted = $completed WHERE AbsenceRequestId = $id";
        command.Parameters.AddWithValue("$completed", isCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$id", absenceRequestId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CreateUpsertCommand(SqliteConnection connection, SqliteTransaction transaction, bool setsPendingAction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = setsPendingAction
            ? """
                INSERT INTO AbsenceProcessing
                    (AbsenceRequestId, EmployeeId, StartDate, EndDate, AccountName, Note, LastKnownStatus, PendingAction, IsCompleted, UpdatedAtUtc)
                VALUES
                    ($id, $employeeId, $startDate, $endDate, $accountName, $note, $status, $pendingAction, 0, $updatedAtUtc)
                ON CONFLICT(AbsenceRequestId) DO UPDATE SET
                    EmployeeId = excluded.EmployeeId,
                    StartDate = excluded.StartDate,
                    EndDate = excluded.EndDate,
                    AccountName = excluded.AccountName,
                    Note = excluded.Note,
                    LastKnownStatus = excluded.LastKnownStatus,
                    PendingAction = excluded.PendingAction,
                    IsCompleted = 0,
                    UpdatedAtUtc = excluded.UpdatedAtUtc
                """
            : """
                INSERT INTO AbsenceProcessing
                    (AbsenceRequestId, EmployeeId, StartDate, EndDate, AccountName, Note, LastKnownStatus, PendingAction, IsCompleted, UpdatedAtUtc)
                VALUES
                    ($id, $employeeId, $startDate, $endDate, $accountName, $note, $status, NULL, 0, $updatedAtUtc)
                ON CONFLICT(AbsenceRequestId) DO UPDATE SET
                    EmployeeId = excluded.EmployeeId,
                    StartDate = excluded.StartDate,
                    EndDate = excluded.EndDate,
                    AccountName = excluded.AccountName,
                    Note = excluded.Note,
                    LastKnownStatus = excluded.LastKnownStatus,
                    UpdatedAtUtc = excluded.UpdatedAtUtc
                """;

        command.Parameters.Add(new SqliteParameter("$id", SqliteType.Integer));
        command.Parameters.Add(new SqliteParameter("$employeeId", SqliteType.Integer));
        command.Parameters.Add(new SqliteParameter("$startDate", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$endDate", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$accountName", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$note", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$status", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$updatedAtUtc", SqliteType.Text));
        if (setsPendingAction)
        {
            command.Parameters.Add(new SqliteParameter("$pendingAction", SqliteType.Text));
        }

        return command;
    }

    private static void SetUpsertParameters(SqliteCommand command, AbsenceRequest request)
    {
        command.Parameters["$id"].Value = request.Id;
        command.Parameters["$employeeId"].Value = request.EmployeeId;
        command.Parameters["$startDate"].Value = request.StartDate.ToString("yyyy-MM-dd");
        command.Parameters["$endDate"].Value = request.EndDate.ToString("yyyy-MM-dd");
        command.Parameters["$accountName"].Value = request.Accounts.Count > 0 ? request.Accounts[0].Name : string.Empty;
        command.Parameters["$note"].Value = (object?)request.Note ?? DBNull.Value;
        command.Parameters["$status"].Value = request.Status.ToString();
        command.Parameters["$updatedAtUtc"].Value = DateTimeOffset.UtcNow.ToString("O");
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using (var createCommand = connection.CreateCommand())
            {
                createCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS AbsenceProcessing (
                        AbsenceRequestId INTEGER PRIMARY KEY,
                        EmployeeId INTEGER NOT NULL,
                        StartDate TEXT NOT NULL,
                        EndDate TEXT NOT NULL,
                        AccountName TEXT NOT NULL,
                        Note TEXT NULL,
                        LastKnownStatus TEXT NOT NULL,
                        PendingAction TEXT NULL,
                        IsCompleted INTEGER NOT NULL DEFAULT 0,
                        UpdatedAtUtc TEXT NOT NULL
                    )
                    """;
                await createCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await MigrateFromLegacyApprovalStateAsync(connection, cancellationToken);

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Übernimmt einmalig die Ids aus der alten "SeenApprovedRequests"-Tabelle (frühere
    /// Version, die nur genehmigte Anträge trackte) als "zuletzt bekannt: Approved", ohne offenen
    /// Aktionsposten. Ohne das würde der erste Refresh nach dem Update jeden aktuell genehmigten
    /// Antrag fälschlich als neuen Aktionsposten melden, obwohl er vermutlich längst in SAP erfasst wurde.</summary>
    private static async Task MigrateFromLegacyApprovalStateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SeenApprovedRequests'";
        var legacyTableExists = await checkCommand.ExecuteScalarAsync(cancellationToken) is not null;
        if (!legacyTableExists)
        {
            return;
        }

        using var migrateCommand = connection.CreateCommand();
        migrateCommand.CommandText = """
            INSERT INTO AbsenceProcessing
                (AbsenceRequestId, EmployeeId, StartDate, EndDate, AccountName, Note, LastKnownStatus, PendingAction, IsCompleted, UpdatedAtUtc)
            SELECT
                AbsenceRequestId, 0, '0001-01-01', '0001-01-01', '', NULL, 'Approved', NULL, 0, SeenAtUtc
            FROM SeenApprovedRequests
            WHERE NOT EXISTS (
                SELECT 1 FROM AbsenceProcessing WHERE AbsenceProcessing.AbsenceRequestId = SeenApprovedRequests.AbsenceRequestId
            )
            """;
        await migrateCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
