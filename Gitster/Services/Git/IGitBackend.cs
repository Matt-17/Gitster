using Gitster.Models;

namespace Gitster.Services.Git;

public interface IGitBackend
{
    string? RepositoryPath { get; }
    Task OpenAsync(string path);

    Task<WorkingTreeState> GetWorkingTreeStateAsync();
    Task<BranchInfo> GetCurrentBranchAsync();

    Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(Gitster.ViewModels.CommitFilter? filter = null);
    Task<CommitDetails> GetCommitAsync(string sha);

    Task<string> AmendAsync(AmendRequest request);
    Task FetchAsync(string remoteName = "origin");
    Task PullAsync(string remoteName = "origin");
    Task PushAsync(string remoteName = "origin", bool forceWithLease = false);

    Task<string> GetReflogSelectorForHeadAsync();
    Task ResetHardAsync(string targetReference);

    GitCapabilities Capabilities { get; }
}
