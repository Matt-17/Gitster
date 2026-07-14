namespace Gitster.ApplicationLayer;

public sealed class HeadRefreshCoordinator : IDisposable
{
    private readonly TimeSpan _debounceDelay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();

    private Func<CancellationToken, Task>? _refreshAsync;
    private int _requestVersion;
    private bool _requested;
    private bool _workerRunning;
    private int _suspendCount;
    private bool _disposed;

    public HeadRefreshCoordinator()
        : this(TimeSpan.FromMilliseconds(75))
    {
    }

    public HeadRefreshCoordinator(TimeSpan debounceDelay)
    {
        _debounceDelay = debounceDelay;
    }

    public bool IsSuspended => Volatile.Read(ref _suspendCount) > 0;

    public void Configure(Func<CancellationToken, Task> refreshAsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
    }

    public void Queue()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsSuspended)
            return;

        bool startWorker;
        lock (_sync)
        {
            _requested = true;
            _requestVersion++;

            // Claim the worker slot atomically so two callers can never start two workers.
            startWorker = !_workerRunning;
            if (startWorker)
                _workerRunning = true;
        }

        if (startWorker)
            _ = RunQueuedRefreshAsync();
    }

    public void ClearPending()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            _requested = false;
            _requestVersion++;
        }
    }

    public async Task RunExclusiveAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct);
        try
        {
            await action(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public IDisposable Suspend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Interlocked.Increment(ref _suspendCount);
        return new Suspension(this);
    }

    private async Task RunQueuedRefreshAsync()
    {
        // The worker slot (_workerRunning) was claimed by the caller under _sync.
        try
        {
            while (!IsSuspended)
            {
                int version;
                lock (_sync)
                {
                    if (!_requested)
                        return;
                    version = _requestVersion;
                }

                await Task.Delay(_debounceDelay);

                lock (_sync)
                {
                    if (IsSuspended || !_requested)
                        return;

                    if (version != _requestVersion)
                        continue;

                    _requested = false;
                }

                var refresh = _refreshAsync
                    ?? throw new InvalidOperationException("Head refresh coordinator has not been configured.");
                await RunExclusiveAsync(refresh);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            bool restart;
            lock (_sync)
            {
                _workerRunning = false;
                restart = _requested && !IsSuspended && !_disposed;
                if (restart)
                    _workerRunning = true;
            }

            if (restart)
                _ = RunQueuedRefreshAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        lock (_sync)
        {
            _requested = false;
        }
        _gate.Dispose();
    }

    private sealed class Suspension : IDisposable
    {
        private HeadRefreshCoordinator? _owner;

        public Suspension(HeadRefreshCoordinator owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
                return;

            Interlocked.Decrement(ref owner._suspendCount);
        }
    }
}
