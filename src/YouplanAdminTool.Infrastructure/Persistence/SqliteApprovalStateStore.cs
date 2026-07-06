using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Infrastructure.Options;

namespace YouplanAdminTool.Infrastructure.Persistence;

/// <summary>Persistiert die Ids bereits gesehener, genehmigter Abwesenheitsanträge lokal in SQLite,
/// damit "neu genehmigt seit letzter Abfrage" auch App-Neustarts überdauert.</summary>
public sealed class SqliteApprovalStateStore : IApprovalStateStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public SqliteApprovalStateStore(IOptions<AppOptions> options)
    {
        var databasePath = ResolveDatabasePath(options.Value.DatabasePath);
        _connectionString = $"Data Source={databasePath}";
    }

    public async Task<IReadOnlySet<long>> GetSeenApprovedIdsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT AbsenceRequestId FROM SeenApprovedRequests";

        var ids = new HashSet<long>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    public async Task MarkAsSeenAsync(IEnumerable<long> absenceRequestIds, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SeenApprovedRequests (AbsenceRequestId, SeenAtUtc)
            VALUES ($id, $seenAtUtc)
            ON CONFLICT(AbsenceRequestId) DO NOTHING
            """;
        var idParameter = command.CreateParameter();
        idParameter.ParameterName = "$id";
        command.Parameters.Add(idParameter);

        var seenAtParameter = command.CreateParameter();
        seenAtParameter.ParameterName = "$seenAtUtc";
        seenAtParameter.Value = DateTimeOffset.UtcNow.ToString("O");
        command.Parameters.Add(seenAtParameter);

        foreach (var id in absenceRequestIds)
        {
            idParameter.Value = id;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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

            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS SeenApprovedRequests (
                    AbsenceRequestId INTEGER PRIMARY KEY,
                    SeenAtUtc TEXT NOT NULL
                )
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static string ResolveDatabasePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YouplanAdminTool");
        Directory.CreateDirectory(appDataDirectory);

        return Path.Combine(appDataDirectory, configuredPath);
    }
}
