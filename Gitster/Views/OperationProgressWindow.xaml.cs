using System.ComponentModel;
using System.Windows;

using Gitster.ViewModels;

namespace Gitster.Views;

public partial class OperationProgressWindow : Window
{
    private bool _completed;

    public OperationProgressWindow(OperationProgressViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void Complete(bool success)
    {
        _completed = true;
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
        DialogResult = success;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_completed)
            return;

        e.Cancel = true;
    }
}
