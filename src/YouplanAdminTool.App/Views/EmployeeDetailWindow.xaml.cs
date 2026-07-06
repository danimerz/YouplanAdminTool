using Wpf.Ui.Controls;
using YouplanAdminTool.App.ViewModels;

namespace YouplanAdminTool.App.Views;

public partial class EmployeeDetailWindow : FluentWindow
{
    public EmployeeDetailWindow(EmployeeDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
