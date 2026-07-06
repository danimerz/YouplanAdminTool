using YouplanAdminTool.Core.Models;

namespace YouplanAdminTool.Core.Abstractions;

/// <summary>Persistiert die zuletzt gewählten Benutzereinstellungen (Intervall, Filter) lokal,
/// damit sie beim nächsten Start der Anwendung wiederhergestellt werden können.</summary>
public interface IUserSettingsStore
{
    Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
}
