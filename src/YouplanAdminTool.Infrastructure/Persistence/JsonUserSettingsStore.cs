using System.Text.Json;
using Microsoft.Extensions.Options;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Options;

namespace YouplanAdminTool.Infrastructure.Persistence;

/// <summary>Speichert Benutzereinstellungen als kleine JSON-Datei im lokalen AppData-Verzeichnis.</summary>
public sealed class JsonUserSettingsStore : IUserSettingsStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonUserSettingsStore(IOptions<AppOptions> options)
    {
        _filePath = AppDataPathResolver.Resolve(options.Value.UserSettingsFileName);
    }

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return UserSettings.Empty;
            }

            await using var stream = File.OpenRead(_filePath);
            var settings = await JsonSerializer.DeserializeAsync<UserSettings>(stream, cancellationToken: cancellationToken);
            return settings ?? UserSettings.Empty;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
