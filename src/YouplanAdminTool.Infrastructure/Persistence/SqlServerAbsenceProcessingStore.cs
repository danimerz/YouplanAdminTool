using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Options;

namespace YouplanAdminTool.Infrastructure.Persistence;

/// <summary>Persistiert zentral auf SQL Server, welchen Status ein Abwesenheitsantrag zuletzt hatte
/// und welche SAP-Aktionsposten (Eintragen/Stornieren) noch offen sind. Im Gegensatz zur lokalen
/// SQLite-Variante sehen alle Benutzerinnen denselben Bearbeitungsstatus (u.a. "erledigt"-Markierungen),
/// da sie dieselbe zentrale Datenbank verwenden.</summary>
public sealed class SqlServerAbsenceProcessingStore : IAbsenceProcessingStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public SqlServerAbsenceProcessingStore(IOptions<SqlServerOptions> options)
    {
        _connectionString = options.Value.ConnectionString
            ?? throw new InvalidOperationException("SqlServer:ConnectionString ist nicht konfiguriert.");
    }

    public async Task<IReadOnlyDictionary<long, AbsenceRequestStatus>> GetLastKnownStatusesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT AbsenceRequestId, LastKnownStatus FROM tbl_ypat_AbsenceProcessing";

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

        using var connection = new SqlConnection(_connectionString);
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
                command.Parameters["@pendingAction"].Value = action.ToString();
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PersistedActionItem>> GetOpenItemsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AbsenceRequestId, EmployeeId, StartDate, EndDate, AccountName, Note, PendingAction, LastKnownStatus
            FROM tbl_ypat_AbsenceProcessing
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

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE tbl_ypat_AbsenceProcessing SET IsCompleted = @completed WHERE AbsenceRequestId = @id";
        command.Parameters.AddWithValue("@completed", isCompleted);
        command.Parameters.AddWithValue("@id", absenceRequestId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Baut die MERGE-basierte Upsert-Anweisung. Mit <see cref="Microsoft.Data.SqlClient.SqlConnection.HOLDLOCK"/>-
    /// artigem Tabellenhinweis, damit gleichzeitige Zugriffe mehrerer Benutzerinnen nicht zu doppelten
    /// Einfügeversuchen (Race Condition) führen.</summary>
    private static SqlCommand CreateUpsertCommand(SqlConnection connection, SqlTransaction transaction, bool setsPendingAction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = setsPendingAction
            ? """
                MERGE INTO tbl_ypat_AbsenceProcessing WITH (HOLDLOCK) AS target
                USING (SELECT @id AS AbsenceRequestId) AS source
                ON target.AbsenceRequestId = source.AbsenceRequestId
                WHEN MATCHED THEN
                    UPDATE SET
                        EmployeeId = @employeeId,
                        StartDate = @startDate,
                        EndDate = @endDate,
                        AccountName = @accountName,
                        Note = @note,
                        LastKnownStatus = @status,
                        PendingAction = @pendingAction,
                        IsCompleted = 0,
                        UpdatedAtUtc = @updatedAtUtc
                WHEN NOT MATCHED THEN
                    INSERT (AbsenceRequestId, EmployeeId, StartDate, EndDate, AccountName, Note, LastKnownStatus, PendingAction, IsCompleted, UpdatedAtUtc)
                    VALUES (@id, @employeeId, @startDate, @endDate, @accountName, @note, @status, @pendingAction, 0, @updatedAtUtc);
                """
            : """
                MERGE INTO tbl_ypat_AbsenceProcessing WITH (HOLDLOCK) AS target
                USING (SELECT @id AS AbsenceRequestId) AS source
                ON target.AbsenceRequestId = source.AbsenceRequestId
                WHEN MATCHED THEN
                    UPDATE SET
                        EmployeeId = @employeeId,
                        StartDate = @startDate,
                        EndDate = @endDate,
                        AccountName = @accountName,
                        Note = @note,
                        LastKnownStatus = @status,
                        UpdatedAtUtc = @updatedAtUtc
                WHEN NOT MATCHED THEN
                    INSERT (AbsenceRequestId, EmployeeId, StartDate, EndDate, AccountName, Note, LastKnownStatus, PendingAction, IsCompleted, UpdatedAtUtc)
                    VALUES (@id, @employeeId, @startDate, @endDate, @accountName, @note, @status, NULL, 0, @updatedAtUtc);
                """;

        command.Parameters.Add(new SqlParameter("@id", System.Data.SqlDbType.BigInt));
        command.Parameters.Add(new SqlParameter("@employeeId", System.Data.SqlDbType.BigInt));
        command.Parameters.Add(new SqlParameter("@startDate", System.Data.SqlDbType.NVarChar, 10));
        command.Parameters.Add(new SqlParameter("@endDate", System.Data.SqlDbType.NVarChar, 10));
        command.Parameters.Add(new SqlParameter("@accountName", System.Data.SqlDbType.NVarChar, 200));
        command.Parameters.Add(new SqlParameter("@note", System.Data.SqlDbType.NVarChar, 1000));
        command.Parameters.Add(new SqlParameter("@status", System.Data.SqlDbType.NVarChar, 50));
        command.Parameters.Add(new SqlParameter("@updatedAtUtc", System.Data.SqlDbType.NVarChar, 50));
        if (setsPendingAction)
        {
            command.Parameters.Add(new SqlParameter("@pendingAction", System.Data.SqlDbType.NVarChar, 50));
        }

        return command;
    }

    private static void SetUpsertParameters(SqlCommand command, AbsenceRequest request)
    {
        command.Parameters["@id"].Value = request.Id;
        command.Parameters["@employeeId"].Value = request.EmployeeId;
        command.Parameters["@startDate"].Value = request.StartDate.ToString("yyyy-MM-dd");
        command.Parameters["@endDate"].Value = request.EndDate.ToString("yyyy-MM-dd");
        command.Parameters["@accountName"].Value = request.Accounts.Count > 0 ? request.Accounts[0].Name : string.Empty;
        command.Parameters["@note"].Value = (object?)request.Note ?? DBNull.Value;
        command.Parameters["@status"].Value = request.Status.ToString();
        command.Parameters["@updatedAtUtc"].Value = DateTimeOffset.UtcNow.ToString("O");
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

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var createCommand = connection.CreateCommand();
            createCommand.CommandText = """
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tbl_ypat_AbsenceProcessing')
                CREATE TABLE tbl_ypat_AbsenceProcessing (
                    AbsenceRequestId BIGINT NOT NULL PRIMARY KEY,
                    EmployeeId BIGINT NOT NULL,
                    StartDate NVARCHAR(10) NOT NULL,
                    EndDate NVARCHAR(10) NOT NULL,
                    AccountName NVARCHAR(200) NOT NULL,
                    Note NVARCHAR(1000) NULL,
                    LastKnownStatus NVARCHAR(50) NOT NULL,
                    PendingAction NVARCHAR(50) NULL,
                    IsCompleted BIT NOT NULL DEFAULT 0,
                    UpdatedAtUtc NVARCHAR(50) NOT NULL
                )
                """;
            await createCommand.ExecuteNonQueryAsync(cancellationToken);

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
