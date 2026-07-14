namespace Gitster.Core.Ui;

/// <summary>
/// Marshals work onto the UI thread without binding the ViewModels to a specific UI framework.
/// The WPF head implements this over <c>Application.Current.Dispatcher</c>; a Blazor head would
/// implement it over its own synchronization context.
/// </summary>
public interface IDispatcher
{
    /// <summary>True when the caller is already on the UI thread.</summary>
    bool IsDispatcherThread { get; }

    /// <summary>Runs <paramref name="action"/> on the UI thread and awaits its completion.</summary>
    Task InvokeAsync(Action action);

    /// <summary>Runs <paramref name="func"/> on the UI thread and returns its result.</summary>
    Task<T> InvokeAsync<T>(Func<T> func);

    /// <summary>Queues <paramref name="action"/> on the UI thread and returns immediately.</summary>
    void Post(Action action);
}
