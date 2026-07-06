# Youplan Admin Tool – Ferien-Übersicht

WPF-Anwendung (.NET 9), die über die [Planday Open API](https://openapi.planday.com/) eine
Ferien-/Abwesenheitsübersicht anzeigt und neu genehmigte Anträge seit der letzten Abfrage hervorhebt.

## Einrichtung der Zugangsdaten

Die Client ID (App ID) und der Refresh Token aus deiner Planday-App-Registrierung (Settings ->
Integrations -> API Access) dürfen **nicht** eingecheckt werden. Trage sie stattdessen lokal ein:

1. Kopiere `src/YouplanAdminTool.App/appsettings.local.json.example` zu
   `src/YouplanAdminTool.App/appsettings.local.json` (diese Datei ist in `.gitignore` und wird nie committet).
2. Trage dort `ClientId` und `RefreshToken` ein.

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
- `YouplanAdminTool.Infrastructure` – Planday-API-Clients (Absence, HR), OAuth2-Token-Handling, lokale SQLite-Persistenz.
- `YouplanAdminTool.App` – WPF-Oberfläche (MVVM, CommunityToolkit.Mvvm), komplett auf Deutsch.

Neue Planday-Module (z.B. Schedule, Payroll) lassen sich ergänzen, ohne bestehende Schichten anzufassen:
einfach ein weiteres Interface in `Core.Abstractions` plus Implementierung in `Infrastructure` hinzufügen.
