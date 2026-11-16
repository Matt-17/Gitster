using System.ComponentModel;
using System.Windows;

using Gitster.ViewModels;

namespace Gitster.Views;

public partial class RepositoryLoadingWindow : Window
{
    private bool _completed;
    private bool _closed;

    public RepositoryLoadingWindow(RepositoryLoadingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => _closed = true;
    }

    /// <summary>
    /// Ends the dialog once loading finished. The load continuation can arrive after the
    /// window is already gone (app shutdown, owner closed) or a second time; DialogResult
    /// may only be set while the window is open as a dialog, so those cases are ignored.
    /// </summary>
    public void Complete(bool success)
    {
        if (_completed)
            return;

        _completed = true;
        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        if (_closed || !IsVisible)
            return;

        try
        {
            DialogResult = success;
        }
        catch (InvalidOperationException)
        {
            // ShowDialog already ended (an exception escaped its modal loop) while the window
            // stayed visible: DialogResult is no longer allowed, so just close it.
            Close();
        }
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
