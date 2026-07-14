using Gitster.Services;
using Gitster.Core;

namespace Gitster.Tests;

[TestClass]
public sealed class HeadRefreshCoordinatorTests
{
    [TestMethod]
    public async Task Queue_RapidRequests_CoalescesIntoSingleRefresh()
    {
        using var coordinator = new HeadRefreshCoordinator(TimeSpan.FromMilliseconds(15));
        var refreshCount = 0;
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        coordinator.Configure(_ =>
        {
            Interlocked.Increment(ref refreshCount);
            refreshed.TrySetResult();
            return Task.CompletedTask;
        });

        coordinator.Queue();
        coordinator.Queue();
        coordinator.Queue();

        await WaitForAsync(refreshed.Task);
        await Task.Delay(60);

        Assert.AreEqual(1, Volatile.Read(ref refreshCount));
    }

    [TestMethod]
    public async Task ClearPending_QueuedRefresh_DoesNotRun()
    {
        using var coordinator = new HeadRefreshCoordinator(TimeSpan.FromMilliseconds(25));
        var refreshCount = 0;

        coordinator.Configure(_ =>
        {
            Interlocked.Increment(ref refreshCount);
            return Task.CompletedTask;
        });

        coordinator.Queue();
        coordinator.ClearPending();

        await Task.Delay(80);

        Assert.AreEqual(0, Volatile.Read(ref refreshCount));
    }

    [TestMethod]
    public async Task Suspend_QueuedRefresh_DropsWorkUntilResumed()
    {
        using var coordinator = new HeadRefreshCoordinator(TimeSpan.FromMilliseconds(10));
        var refreshCount = 0;
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        coordinator.Configure(_ =>
        {
            Interlocked.Increment(ref refreshCount);
            refreshed.TrySetResult();
            return Task.CompletedTask;
        });

        using (coordinator.Suspend())
        {
            coordinator.Queue();
            await Task.Delay(40);
        }

        Assert.AreEqual(0, Volatile.Read(ref refreshCount));

        coordinator.Queue();

        await WaitForAsync(refreshed.Task);
        Assert.AreEqual(1, Volatile.Read(ref refreshCount));
    }

    [TestMethod]
    public async Task RunExclusiveAsync_ConcurrentCalls_AreSerialized()
    {
        using var coordinator = new HeadRefreshCoordinator(TimeSpan.FromMilliseconds(1));
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeCount = 0;
        var maxActiveCount = 0;

        var first = coordinator.RunExclusiveAsync(async _ =>
        {
            var active = Interlocked.Increment(ref activeCount);
            maxActiveCount = Math.Max(maxActiveCount, active);
            firstEntered.SetResult();
            await releaseFirst.Task;
            Interlocked.Decrement(ref activeCount);
        });

        await WaitForAsync(firstEntered.Task);

        var second = coordinator.RunExclusiveAsync(async _ =>
        {
            var active = Interlocked.Increment(ref activeCount);
            maxActiveCount = Math.Max(maxActiveCount, active);
            secondEntered.SetResult();
            await Task.Delay(10);
            Interlocked.Decrement(ref activeCount);
        });

        await Task.Delay(40);
        Assert.IsFalse(secondEntered.Task.IsCompleted, "The second refresh should wait for the first one.");

        releaseFirst.SetResult();
        await WaitForAsync(secondEntered.Task);
        await Task.WhenAll(first, second);

        Assert.AreEqual(1, maxActiveCount);
    }

    private static async Task WaitForAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(1000));
        Assert.AreSame(task, completed, "Timed out waiting for the coordinator.");
        await task;
    }
}
