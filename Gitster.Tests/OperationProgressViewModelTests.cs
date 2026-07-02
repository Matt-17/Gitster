using Gitster.ViewModels;

namespace Gitster.Tests;

[TestClass]
public sealed class OperationProgressViewModelTests
{
    [TestMethod]
    public void Cancel_WhenCancelable_RaisesCancelRequestedAndUpdatesText()
    {
        using var viewModel = new OperationProgressViewModel("Push") { CanCancel = true };
        var raised = false;
        viewModel.CancelRequested += (_, _) => raised = true;

        viewModel.Cancel();

        Assert.IsTrue(raised);
        Assert.IsTrue(viewModel.IsCancelRequested);
        Assert.AreEqual("Canceling", viewModel.StageText);
    }

    [TestMethod]
    public void Cancel_WhenNotCancelable_DoesNothing()
    {
        using var viewModel = new OperationProgressViewModel("Push");
        var raised = false;
        viewModel.CancelRequested += (_, _) => raised = true;

        viewModel.Cancel();

        Assert.IsFalse(raised);
        Assert.IsFalse(viewModel.IsCancelRequested);
    }
}
