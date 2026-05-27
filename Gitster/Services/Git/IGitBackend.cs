using Gitster.Models;

namespace Gitster.Services.Git;

public interface IGitBackend
{
    string? RepositoryPath { get; }
    Task OpenAsync(string path);

    event EventHandler? HeadChanged;

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
    Task<string> GetHeadShaAsync();
    Task<string> ResolveRefAsync(string refSpec);
    Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string fromSha, string toSha);
    Task<bool> CommitExistsAsync(string sha);
    Task CherryPickAsync(string sha);

    GitCapabilities Capabilities { get; }
}
