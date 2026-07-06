using System.Windows;
using YouplanAdminTool.App.ViewModels;

namespace YouplanAdminTool.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RefreshCommand.ExecuteAsync(null);
        Closed += (_, _) => _viewModel.Dispose();
    }
}
