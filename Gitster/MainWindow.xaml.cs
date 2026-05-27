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

    private async void Window_Activated(object sender, EventArgs e)
    {
        await _viewModel.OnWindowActivatedAsync();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        _viewModel.AutoFetch.OnWindowStateChanged(WindowState);
    }
}