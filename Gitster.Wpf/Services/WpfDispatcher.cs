using System.Windows;

using Gitster.ApplicationLayer.Ui;

namespace Gitster.Services;

/// <summary>WPF implementation of <see cref="IDispatcher"/> over the application dispatcher.</summary>
public sealed class WpfDispatcher : IDispatcher
{
    public bool IsDispatcherThread => Application.Current?.Dispatcher.CheckAccess() ?? true;

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}
