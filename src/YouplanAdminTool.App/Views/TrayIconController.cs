using System.Windows;
using System.Windows.Forms;

namespace YouplanAdminTool.App.Views;

/// <summary>Verwaltet das Tray-Icon der Anwendung: Kontextmenü (Öffnen/Beenden), Wiederherstellen
/// des Hauptfensters und Benachrichtigungen (Balloon-Tips) bei neu genehmigten Anträgen.</summary>
public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Window _mainWindow;

    public event EventHandler? ExitRequested;

    public TrayIconController(Window mainWindow)
    {
        _mainWindow = mainWindow;

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Öffnen", null, (_, _) => RestoreWindow());
        contextMenu.Items.Add("Beenden", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Youplan Admin Tool – Ferien-Übersicht",
            Visible = true,
            ContextMenuStrip = contextMenu,
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreWindow();
    }

    public void RestoreWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ShowBalloonTip(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}
