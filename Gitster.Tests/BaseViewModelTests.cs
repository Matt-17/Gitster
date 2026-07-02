using Gitster.Services;
using Gitster.ViewModels;

namespace Gitster.Tests;

[TestClass]
public sealed class BaseViewModelTests
{
    [TestMethod]
    public async Task ExecuteGuardedAsync_Cancellation_DoesNotReportFailure()
    {
        var feedback = new OperationFeedbackService();

        await TestViewModel.RunGuardedAsync(
            () => throw new OperationCanceledException(),
            "Cancel",
            feedback);

        Assert.IsNull(feedback.Current);
    }

    [TestMethod]
    public async Task ExecuteGuardedAsync_Failure_RoutesToFeedback()
    {
        var feedback = new OperationFeedbackService();

        await TestViewModel.RunGuardedAsync(
            () => throw new InvalidOperationException("boom"),
            "Explode",
            feedback);

        Assert.IsInstanceOfType<OperationFeedback.Failure>(feedback.Current);
    }

    private sealed class TestViewModel : BaseViewModel
    {
        public static Task RunGuardedAsync(
            Func<Task> action,
            string operationName,
            OperationFeedbackService feedback) =>
            ExecuteGuardedAsync(action, operationName, feedback);
    }
}
