using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Services.Git;

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
        "RangeDiff"         => HasCapability(GitCapabilities.RangeDiff),
        "Worktrees"         => HasCapability(GitCapabilities.Worktrees),
        "CommitSigning"     => HasCapability(GitCapabilities.CommitSigning),
        _                   => true,
    };

    public string GetMissingReason(string capabilityName) => capabilityName switch
    {
        "GitCli"            => "Requires Git command-line tool to be installed.",
        "InteractiveRebase" => "Requires Git CLI (install Git to enable).",
        "FixupAutosquash"   => "Requires Git CLI (install Git to enable).",
        "PickaxeSearch"     => "Coming in phase 4.",
        "RangeDiff"         => "Coming in phase 4 (requires Git CLI).",
        "Worktrees"         => "Coming in phase 3.",
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
