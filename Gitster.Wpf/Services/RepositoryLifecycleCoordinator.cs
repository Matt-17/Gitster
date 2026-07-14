namespace Gitster.Services;

public sealed class RepositoryLifecycleCoordinator
{
    private readonly RepositorySwitchCoordinator _switchCoordinator;

    public RepositoryLifecycleCoordinator(RepositorySwitchCoordinator switchCoordinator)
    {
        _switchCoordinator = switchCoordinator;
    }

    public bool InitialRepositoryLoadStarted { get; private set; }

    public bool IsSwitchingRepository => _switchCoordinator.IsSwitchingRepository;

    public string? LoadedRepositoryPath => _switchCoordinator.LoadedRepositoryPath;

    public async Task InitializeAsync(
        string initialPath,
        Func<string, bool, bool, Task<bool>> switchRepositoryAsync)
    {
        if (InitialRepositoryLoadStarted)
            return;

        InitialRepositoryLoadStarted = true;
        if (!string.IsNullOrWhiteSpace(initialPath))
            await switchRepositoryAsync(initialPath, false, true);
    }

    public Task<bool> SwitchAsync(
        RepositorySwitchRequest request,
        RepositorySwitchCallbacks callbacks) =>
        _switchCoordinator.SwitchAsync(request, callbacks);
}
