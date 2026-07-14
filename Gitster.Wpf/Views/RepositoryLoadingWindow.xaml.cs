using System.ComponentModel;
using System.Windows;

using Gitster.ViewModels;

namespace Gitster.Views;

public partial class RepositoryLoadingWindow : Window
{
    private bool _completed;

    public RepositoryLoadingWindow(RepositoryLoadingViewModel viewModel)
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

        if (DataContext is RepositoryLoadingViewModel viewModel
            && viewModel.CancelCommand.CanExecute(null))
        {
            viewModel.CancelCommand.Execute(null);
        }

        e.Cancel = true;
    }
}
