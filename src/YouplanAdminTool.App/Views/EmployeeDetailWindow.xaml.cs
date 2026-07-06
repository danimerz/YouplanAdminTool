using System.Windows;
using YouplanAdminTool.App.ViewModels;

namespace YouplanAdminTool.App.Views;

public partial class EmployeeDetailWindow : Window
{
    public EmployeeDetailWindow(EmployeeDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
