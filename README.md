# Youplan Admin Tool – Ferien-Übersicht

WPF-Anwendung (.NET 9), die über die [Planday Open API](https://openapi.planday.com/) eine
Ferien-/Abwesenheitsübersicht anzeigt und neu genehmigte Anträge seit der letzten Abfrage hervorhebt.

## Einrichtung der Zugangsdaten

Die Client ID (App ID) und der Refresh Token aus deiner Planday-App-Registrierung (Settings ->
Integrations -> API Access) dürfen **nicht** eingecheckt werden. Trage sie stattdessen lokal ein:

1. Kopiere `src/YouplanAdminTool.App/appsettings.local.json.example` zu
   `src/YouplanAdminTool.App/appsettings.local.json` (diese Datei ist in `.gitignore` und wird nie committet).
2. Trage dort `ClientId` und `RefreshToken` ein.

## Zentrale SQL-Datenbank für den SAP-Bearbeitungsstatus (optional)

Standardmäßig speichert die App den Bearbeitungsstatus ("erledigt"-Markierungen, erkannte
Statusänderungen) lokal in SQLite - jede Benutzerin sieht dann nur ihren eigenen Stand. Um diesen
Status zwischen allen Benutzerinnen zu teilen, in `appsettings.local.json` unter `SqlServer:ConnectionString`
eine SQL-Server-Verbindung hinterlegen (siehe `appsettings.local.json.example`). Ist keine
ConnectionString gesetzt, läuft die App unverändert mit der lokalen SQLite-Datei weiter.

**Wichtig beim ersten Umstieg auf die zentrale Datenbank:** Die Tabelle wird beim ersten Zugriff leer
angelegt. Die App erkennt das und übernimmt den aktuell geladenen Stand als Basis, ohne alle
aktuell genehmigten Anträge fälschlich als neue offene Posten zu melden - Statusänderungen werden
erst ab diesem Zeitpunkt erkannt.

## Starten

```
dotnet run --project src/YouplanAdminTool.App
```

## Bauen & Testen

```
dotnet build
dotnet test
```

## Architektur

- `YouplanAdminTool.Core` – Domänenmodelle und Schnittstellen, keine Abhängigkeit zu Planday oder WPF.
- `YouplanAdminTool.Infrastructure` – Planday-API-Clients (Absence, HR), OAuth2-Token-Handling, Persistenz
  (SQLite lokal, optional SQL Server zentral - siehe oben).
- `YouplanAdminTool.App` – WPF-Oberfläche (MVVM, CommunityToolkit.Mvvm), komplett auf Deutsch.

Neue Planday-Module (z.B. Schedule, Payroll) lassen sich ergänzen, ohne bestehende Schichten anzufassen:
einfach ein weiteres Interface in `Core.Abstractions` plus Implementierung in `Infrastructure` hinzufügen.
