using System.ComponentModel;
using System.Windows;
using YouplanAdminTool.App.ViewModels;

namespace YouplanAdminTool.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private TrayIconController? _trayIconController;
    private bool _allowClose;
    private bool _hasShownMinimizeHint;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _trayIconController = new TrayIconController(this);
        _trayIconController.ExitRequested += OnExitRequested;
        _viewModel.NewApprovalsDetected += OnNewApprovalsDetected;

        await _viewModel.InitializeAsync();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        _allowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();

        if (!_hasShownMinimizeHint)
        {
            _hasShownMinimizeHint = true;
            _trayIconController?.ShowBalloonTip(
                "Youplan Admin Tool läuft weiter",
                "Die Anwendung wurde ins Symbolfeld minimiert und aktualisiert die Ferien-Übersicht im Hintergrund.");
        }
    }

    private void OnNewApprovalsDetected(object? sender, int count)
    {
        _trayIconController?.ShowBalloonTip(
            "Neu genehmigte Ferien",
            count == 1 ? "1 neuer genehmigter Antrag." : $"{count} neue genehmigte Anträge.");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.NewApprovalsDetected -= OnNewApprovalsDetected;
        _viewModel.Dispose();
        _trayIconController?.Dispose();
    }
}
