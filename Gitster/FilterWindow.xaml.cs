using System;
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
    
    public event EventHandler? FiltersApplied;

    public FilterWindow(FilterWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultOk = true;
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        // Window stays open when Apply is clicked
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultOk = false;
        Close();
    }
}
