namespace YouplanAdminTool.Infrastructure.Options;

public sealed class AppOptions
{
    public const string SectionName = "App";

    public int PollingIntervalMinutes { get; set; } = 15;

    public int DefaultDateRangeDays { get; set; } = 90;

    /// <summary>Pfad zur lokalen SQLite-Datenbank. Relative Pfade werden gegen das Benutzer-AppData-Verzeichnis aufgelöst.</summary>
    public string DatabasePath { get; set; } = "state.db";

    /// <summary>Dateiname für die persistierten Benutzereinstellungen (Intervall, Filter). Relative Pfade werden gegen das Benutzer-AppData-Verzeichnis aufgelöst.</summary>
    public string UserSettingsFileName { get; set; } = "usersettings.json";
}
