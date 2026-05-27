using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LibGit2Sharp;

namespace Gitster.ViewModels;

public partial class HealthcheckItem : ObservableObject
{
    public string Title { get; }
    public string Description { get; }
    public IRelayCommand ApplyCommand { get; }

    [ObservableProperty]
    public partial bool IsApplied { get; set; }

    public HealthcheckItem(string title, string description, bool isApplied, Action apply)
    {
        Title = title;
        Description = description;
        IsApplied = isApplied;
        ApplyCommand = new RelayCommand(() => { apply(); IsApplied = true; });
    }
}

public partial class RepositorySettingsViewModel : BaseViewModel
{
    public ObservableCollection<HealthcheckItem> HealthcheckItems { get; } = [];

    public RepositorySettingsViewModel(string repoPath)
    {
        LoadHealthcheck(repoPath);
    }

    private void LoadHealthcheck(string repoPath)
    {
        AddCheck(repoPath,
            "diff.algorithm = histogram",
            "Produces more accurate diffs for renamed or moved code blocks.",
            r => r.Config.Get<string>("diff.algorithm")?.Value == "histogram",
            r => r.Config.Set("diff.algorithm", "histogram"));

        AddCheck(repoPath,
            "fetch.prune = true",
            "Automatically remove stale remote-tracking branches after fetching.",
            r => r.Config.Get<bool>("fetch.prune")?.Value == true,
            r => r.Config.Set("fetch.prune", true));

        AddCheck(repoPath,
            "rerere.enabled = true",
            "Record and replay merge conflict resolutions automatically.",
            r => r.Config.Get<bool>("rerere.enabled")?.Value == true,
            r => r.Config.Set("rerere.enabled", true));

        AddCheck(repoPath,
            "push.autoSetupRemote = true",
            "Automatically set upstream when pushing a new branch for the first time.",
            r => r.Config.Get<bool>("push.autoSetupRemote")?.Value == true,
            r => r.Config.Set("push.autoSetupRemote", true));

        AddCheck(repoPath,
            "branch.sort = -committerdate",
            "Show recently-used branches at the top in branch listings.",
            r => r.Config.Get<string>("branch.sort")?.Value == "-committerdate",
            r => r.Config.Set("branch.sort", "-committerdate"));

        AddCheck(repoPath,
            "init.defaultBranch = main",
            "New repositories start on 'main' instead of 'master'.",
            r => r.Config.Get<string>("init.defaultBranch")?.Value == "main",
            r => r.Config.Set("init.defaultBranch", "main"));

        AddCheck(repoPath,
            "pull.rebase = true",
            "Rebase instead of merge when pulling — keeps history linear. Only enable if you understand rebasing.",
            r => r.Config.Get<bool>("pull.rebase")?.Value == true,
            r => r.Config.Set("pull.rebase", true));
    }

    private void AddCheck(string repoPath, string title, string description,
        Func<Repository, bool> check, Action<Repository> apply)
    {
        bool isApplied = false;
        try
        {
            using var repo = new Repository(repoPath);
            isApplied = check(repo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HealthcheckItem.Check [{title}]: {ex.Message}");
        }

        HealthcheckItems.Add(new HealthcheckItem(title, description, isApplied, () =>
        {
            try
            {
                using var repo = new Repository(repoPath);
                apply(repo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HealthcheckItem.Apply [{title}]: {ex.Message}");
            }
        }));
    }
}
