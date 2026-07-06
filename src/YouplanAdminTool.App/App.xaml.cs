using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui.Appearance;
using YouplanAdminTool.App.ViewModels;
using YouplanAdminTool.App.Views;
using YouplanAdminTool.Infrastructure;

namespace YouplanAdminTool.App;

public partial class App : System.Windows.Application
{
    // Siemens iX Primärfarbe (--theme-color-primary aus https://github.com/siemens/ix).
    private static readonly System.Windows.Media.Color SiemensAccentColor = System.Windows.Media.Color.FromRgb(0x00, 0x6E, 0x93);

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplicationThemeManager.Apply(ApplicationTheme.Light);
        ApplicationAccentColorManager.Apply(SiemensAccentColor, ApplicationTheme.Light);

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
                config.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddPlandayIntegration(context.Configuration);
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
