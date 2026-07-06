namespace YouplanAdminTool.Infrastructure.Persistence;

/// <summary>Löst Dateinamen für lokale Zustandsdateien gegen das Benutzer-AppData-Verzeichnis auf.</summary>
internal static class AppDataPathResolver
{
    public static string Resolve(string configuredPath)
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
