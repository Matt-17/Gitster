using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;

namespace Gitster.Services;

public abstract record OperationFeedback
{
    public sealed record Running(string Verb, DateTime StartedAt) : OperationFeedback;
    public sealed record Success(string Verb, string? Detail, DateTime CompletedAt) : OperationFeedback;
    public sealed record Failure(string Verb, string Reason, DateTime CompletedAt) : OperationFeedback;
}

public partial class OperationFeedbackService : ObservableObject
{
    private const int SuccessFadeOutMs = 3000;
    private CancellationTokenSource? _fadeOutCts;

    [ObservableProperty]
    private OperationFeedback? _current;

    public async Task<T> RunAsync<T>(string verb, Func<Task<T>> action, Func<T, string?>? detailSelector = null)
    {
        CancelPendingFadeOut();
        Current = new OperationFeedback.Running(verb, DateTime.Now);

        try
        {
            var result = await action();
            Current = new OperationFeedback.Success(verb, detailSelector?.Invoke(result), DateTime.Now);
            ScheduleFadeOut();
            return result;
        }
        catch (Exception ex)
        {
            Current = new OperationFeedback.Failure(verb, ex.Message, DateTime.Now);
            throw;
        }
    }

    public Task RunAsync(string verb, Func<Task> action)
        => RunAsync<object?>(verb, async () =>
        {
            await action();
            return null;
        });

    public void Dismiss() => Current = null;

    private void CancelPendingFadeOut()
    {
        _fadeOutCts?.Cancel();
        _fadeOutCts = null;
    }

    private void ScheduleFadeOut()
    {
        _fadeOutCts = new CancellationTokenSource();
        var token = _fadeOutCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SuccessFadeOutMs, token);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (Current is OperationFeedback.Success)
                        Current = null;
                });
            }
            catch (TaskCanceledException)
            {
            }
        });
    }
}
