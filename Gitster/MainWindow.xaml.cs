using System.Windows;
using Gitster.ViewModels;

namespace Gitster;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        _viewModel.OnWindowActivated();
    }
}