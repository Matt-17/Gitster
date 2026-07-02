using CommunityToolkit.Mvvm.ComponentModel;
using Gitster.Services;
using Microsoft.Extensions.Logging;

namespace Gitster.ViewModels;

/// <summary>
/// Base view model that all view models inherit from.
/// Inherits from ObservableRecipient to support MVVM pattern with INPC and messaging.
/// </summary>
public abstract class BaseViewModel : ObservableRecipient
{
    protected static async Task ExecuteGuardedAsync(
        Func<Task> action,
        string operationName,
        OperationFeedbackService? feedback = null,
        IWindowService? windowService = null,
        ILogger? logger = null)
    {
        try
        {
            if (feedback is null)
            {
                await action();
            }
            else
            {
                await feedback.RunAsync(operationName, action);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "{OperationName} failed", operationName);
            windowService?.Error($"{operationName} failed:\n{ex.Message}", "Gitster");
        }
    }
}
