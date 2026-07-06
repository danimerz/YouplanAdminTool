using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Services;
using YouplanAdminTool.Infrastructure.Absence;
using YouplanAdminTool.Infrastructure.Auth;
using YouplanAdminTool.Infrastructure.Hr;
using YouplanAdminTool.Infrastructure.Http;
using YouplanAdminTool.Infrastructure.Options;
using YouplanAdminTool.Infrastructure.Persistence;

namespace YouplanAdminTool.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registriert die Planday-Anbindung (Auth, Absence-/HR-API-Clients) sowie die lokale Persistenz.</summary>
    public static IServiceCollection AddPlandayIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlandayOptions>(configuration.GetSection(PlandayOptions.SectionName));
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.SectionName));
        services.Configure<SqlServerOptions>(configuration.GetSection(SqlServerOptions.SectionName));

        // IAccessTokenProvider muss ein echtes Singleton sein, damit der zwischengespeicherte
        // Access Token von allen Planday-API-Clients gemeinsam genutzt wird (kein Refresh pro Client).
        services.AddHttpClient("PlandayAuth", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PlandayOptions>>().Value;
            client.BaseAddress = new Uri(options.AuthBaseUrl);
        });

        services.AddSingleton<IAccessTokenProvider>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("PlandayAuth");
            return new PlandayAccessTokenProvider(httpClient, sp.GetRequiredService<IOptions<PlandayOptions>>());
        });

        services.AddTransient<PlandayAuthHeaderHandler>();

        services.AddHttpClient<IPlandayAbsenceService, PlandayAbsenceService>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<PlandayOptions>>().Value;
                client.BaseAddress = new Uri(options.ApiBaseUrl);
            })
            .AddHttpMessageHandler<PlandayAuthHeaderHandler>();

        services.AddHttpClient<IPlandayHrService, PlandayHrService>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<PlandayOptions>>().Value;
                client.BaseAddress = new Uri(options.ApiBaseUrl);
            })
            .AddHttpMessageHandler<PlandayAuthHeaderHandler>();

        // Zentrale SQL-Server-DB, sobald eine ConnectionString hinterlegt ist (alle Benutzerinnen teilen
        // sich dann denselben Bearbeitungsstatus); sonst lokale SQLite-Datei als Fallback.
        var sqlServerConnectionString = configuration.GetSection(SqlServerOptions.SectionName)["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(sqlServerConnectionString))
        {
            services.AddSingleton<IAbsenceProcessingStore, SqlServerAbsenceProcessingStore>();
        }
        else
        {
            services.AddSingleton<IAbsenceProcessingStore, SqliteAbsenceProcessingStore>();
        }

        services.AddSingleton<IUserSettingsStore, JsonUserSettingsStore>();
        services.AddSingleton<IStatusChangeDetector, StatusChangeDetector>();

        return services;
    }
}
