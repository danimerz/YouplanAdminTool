namespace YouplanAdminTool.Infrastructure.Options;

/// <summary>Konfiguration für die zentrale SQL-Server-Datenbank, in der der SAP-Bearbeitungsstatus
/// (Statusänderungen, "erledigt"-Markierungen) geteilt zwischen allen Benutzerinnen gespeichert wird.
/// Ist keine ConnectionString hinterlegt, verwendet die App stattdessen die lokale SQLite-Datei.</summary>
public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    public string? ConnectionString { get; set; }
}
