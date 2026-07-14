using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Core.Ui;

namespace Gitster.Core;

public abstract record OperationFeedback
{
    public sealed record Running(string Verb, DateTime StartedAt) : OperationFeedback;
    public sealed record Success(string Verb, string? Detail, DateTime CompletedAt) : OperationFeedback;
    public sealed record Failure(string Verb, string Reason, DateTime CompletedAt) : OperationFeedback;
}

public partial class OperationFeedbackService : ObservableObject
{
    private const int SuccessFadeOutMs = 3000;
    private readonly RepositoryStateService? _stateService;
    private readonly IDispatcher? _dispatcher;
    private CancellationTokenSource? _fadeOutCts;

    public OperationFeedbackService()
    {
    }

    public OperationFeedbackService(RepositoryStateService stateService, IDispatcher? dispatcher = null)
    {
        _stateService = stateService;
        _dispatcher = dispatcher;
    }

    [ObservableProperty]
    private OperationFeedback? _current;

    public async Task<T> RunAsync<T>(string verb, Func<Task<T>> action, Func<T, string?>? detailSelector = null)
    {
        CancelPendingFadeOut();
        Current = new OperationFeedback.Running(verb, DateTime.Now);

        try
        {
            using var lease = _stateService is null
                ? null
                : await _stateService.BeginOperationAsync();
            _stateService?.ThrowIfIndexLocked();

            var result = await action();
            Current = new OperationFeedback.Success(verb, detailSelector?.Invoke(result), DateTime.Now);
            ScheduleFadeOut();
            return result;
        }
        catch (OperationCanceledException)
        {
            Current = null;
            throw;
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
                void ClearIfStillSuccess()
                {
                    if (Current is OperationFeedback.Success)
                        Current = null;
                }
                if (_dispatcher is not null)
                    await _dispatcher.InvokeAsync(ClearIfStillSuccess);
                else
                    ClearIfStillSuccess();
            }
            catch (TaskCanceledException)
            {
            }
        });
    }
}
