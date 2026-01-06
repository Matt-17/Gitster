using System.Windows;
using Gitster.ViewModels;

namespace Gitster;

/// <summary>
/// Interaction logic for FilterWindow.xaml
/// </summary>
public partial class FilterWindow : Window
{
    public FilterWindowViewModel ViewModel { get; }
    public bool DialogResultOk { get; private set; }
    public bool ApplyClicked { get; private set; }

    public FilterWindow(FilterWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultOk = true;
        ApplyClicked = true;
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyClicked = true;
        // Window stays open when Apply is clicked
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultOk = false;
        ApplyClicked = false;
        Close();
    }
}
