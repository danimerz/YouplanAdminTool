using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YouplanAdminTool.App.ViewModels;

namespace YouplanAdminTool.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<long, EmployeeDetailWindow> _openDetailWindows = [];
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
        _viewModel.ActionItemsDetected += OnActionItemsDetected;

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

    private void OnAbsenceRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: IHasEmployeeId row })
        {
            return;
        }

        if (_openDetailWindows.TryGetValue(row.EmployeeId, out var existingWindow))
        {
            existingWindow.Activate();
            return;
        }

        var detail = _viewModel.GetEmployeeDetail(row.EmployeeId);
        if (detail is null)
        {
            return;
        }

        var window = new EmployeeDetailWindow(detail) { Owner = this };
        _openDetailWindows[row.EmployeeId] = window;
        window.Closed += (_, _) => _openDetailWindows.Remove(row.EmployeeId);
        window.Show();
    }

    private void OnActionItemsDetected(object? sender, int count)
    {
        _trayIconController?.ShowBalloonTip(
            "Offene Posten für SAP",
            count == 1 ? "1 neuer offener Posten für SAP." : $"{count} neue offene Posten für SAP.");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.ActionItemsDetected -= OnActionItemsDetected;
        _viewModel.Dispose();
        _trayIconController?.Dispose();
    }
}
