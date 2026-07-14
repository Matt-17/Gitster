using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Core.Git;

namespace Gitster.Services.Capabilities;

public partial class CapabilityService : ObservableObject
{
    private readonly IGitBackend _git;

    [ObservableProperty]
    public partial bool IsGitCliAvailable { get; set; }

    [ObservableProperty]
    public partial string? GitCliVersion { get; set; }

    public CapabilityService(IGitBackend git)
    {
        _git = git;
        _ = DetectGitCliAsync();
    }

    public bool HasCapability(GitCapabilities cap) => _git.Capabilities.HasFlag(cap);

    public bool Requires(string capabilityName) => capabilityName switch
    {
        "GitCli"            => IsGitCliAvailable,
        "InteractiveRebase" => HasCapability(GitCapabilities.InteractiveRebase),
        "FixupAutosquash"   => HasCapability(GitCapabilities.FixupAutosquash),
        "PickaxeSearch"     => HasCapability(GitCapabilities.PickaxeSearch),
        "DiffRegexSearch"   => HasCapability(GitCapabilities.DiffRegexSearch),
        "BlameFollow"       => HasCapability(GitCapabilities.BlameFollow),
        "RangeDiff"         => HasCapability(GitCapabilities.RangeDiff),
        "Worktrees"         => HasCapability(GitCapabilities.Worktrees),
        "SourceArchive"     => HasCapability(GitCapabilities.SourceArchive),
        "CommitSigning"     => HasCapability(GitCapabilities.CommitSigning),
        _                   => true,
    };

    public string GetMissingReason(string capabilityName) => capabilityName switch
    {
        "GitCli"            => "Requires Git command-line tool to be installed.",
        "InteractiveRebase" => "Requires Git CLI (install Git to enable).",
        "FixupAutosquash"   => "Requires Git CLI (install Git to enable).",
        "PickaxeSearch"     => "Requires the Git command-line tool (git log -S).",
        "DiffRegexSearch"   => "Requires the Git command-line tool (git log -G).",
        "BlameFollow"       => "Requires the Git command-line tool (git blame -w -C).",
        "RangeDiff"         => "Requires the Git command-line tool (git range-diff).",
        "Worktrees"         => "Requires the Git command-line tool to be installed.",
        "SourceArchive"     => "Requires the Git command-line tool (git archive).",
        "CommitSigning"     => "Coming later (requires Git CLI and GPG/SSH setup).",
        _                   => "Unavailable.",
    };

    private async Task DetectGitCliAsync()
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("git", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                IsGitCliAvailable = true;
                GitCliVersion = output.Trim();
            }
        }
        catch
        {
            IsGitCliAvailable = false;
        }
    }
}
