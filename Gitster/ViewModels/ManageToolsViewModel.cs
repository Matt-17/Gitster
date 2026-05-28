using System.Collections.ObjectModel;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;
using Gitster.Services;

namespace Gitster.ViewModels;

public partial class ManageToolsViewModel : BaseViewModel
{
    private readonly CustomToolsService _service;

    public ObservableCollection<CustomTool> Tools { get; } = [];

    public bool RepositoryAvailable => _service.RepositoryAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial CustomTool? SelectedTool { get; set; }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Command { get; set; } = string.Empty;
    [ObservableProperty] public partial string Confirm { get; set; } = string.Empty;
    [ObservableProperty] public partial string Prompt { get; set; } = string.Empty;
    [ObservableProperty] public partial bool NeedsCommit { get; set; }
    [ObservableProperty] public partial bool IsRepoScope { get; set; }

    public bool HasSelection => SelectedTool != null;

    public ManageToolsViewModel(CustomToolsService service)
    {
        _service = service;
        // Repository scope only if a repo is open; otherwise default to global.
        IsRepoScope = false;
        ReloadFromDisk();
    }

    private void ReloadFromDisk()
    {
        Tools.Clear();
        foreach (var t in _service.GetEditableTools(CustomToolScope.Global))
            Tools.Add(t);
        if (_service.RepositoryAvailable)
            foreach (var t in _service.GetEditableTools(CustomToolScope.Repository))
                Tools.Add(t);
    }

    partial void OnSelectedToolChanged(CustomTool? value)
    {
        if (value == null) return;
        Name        = value.Name;
        Command     = value.Command;
        Confirm     = value.Confirm ?? string.Empty;
        Prompt      = value.Prompt ?? string.Empty;
        NeedsCommit = value.NeedsCommit;
        IsRepoScope = value.Scope == CustomToolScope.Repository;
    }

    [RelayCommand]
    private void New()
    {
        SelectedTool = null;
        Name = Command = Confirm = Prompt = string.Empty;
        NeedsCommit = false;
        IsRepoScope = _service.RepositoryAvailable;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            MessageBox.Show("Give the tool a name.", "Manage tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(Command))
        {
            MessageBox.Show("Enter the command to run.", "Manage tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var scope = IsRepoScope && _service.RepositoryAvailable
            ? CustomToolScope.Repository
            : CustomToolScope.Global;

        var tool = new CustomTool(
            Name.Trim(),
            Command.Trim(),
            string.IsNullOrWhiteSpace(Confirm) ? null : Confirm.Trim(),
            NeedsCommit,
            string.IsNullOrWhiteSpace(Prompt) ? null : Prompt.Trim(),
            scope);

        // Replace any existing editable tool with the same name (in any scope), then add.
        var working = Tools.Where(t =>
            !string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        working.Add(tool);

        PersistAndReload(working);
        SelectedTool = Tools.FirstOrDefault(t =>
            string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (SelectedTool is not { } sel) return;

        var confirm = MessageBox.Show($"Delete tool '{sel.Name}'?", "Manage tools",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var working = Tools.Where(t => t != sel).ToList();
        PersistAndReload(working);
        New();
    }

    private void PersistAndReload(List<CustomTool> working)
    {
        try
        {
            _service.Save(CustomToolScope.Global,
                working.Where(t => t.Scope == CustomToolScope.Global));
            if (_service.RepositoryAvailable)
                _service.Save(CustomToolScope.Repository,
                    working.Where(t => t.Scope == CustomToolScope.Repository));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ReloadFromDisk();
    }

    // ── Template suggestions ─────────────────────────────────────────────────

    [RelayCommand]
    private void TemplateFeatureBranch()
    {
        Name = "Create feature branch";
        Command = "git checkout -b feature/$ARGS";
        Prompt = "Feature name:";
        Confirm = string.Empty;
        NeedsCommit = false;
        SelectedTool = null;
    }

    [RelayCommand]
    private void TemplateOpenOnGitHub()
    {
        Name = "Open commit on GitHub";
        Command = "start \"\" https://github.com/OWNER/REPO/commit/$REVISION";
        Prompt = string.Empty;
        Confirm = string.Empty;
        NeedsCommit = true;
        SelectedTool = null;
    }

    [RelayCommand]
    private void TemplateRunTests()
    {
        Name = "Run tests";
        Command = "dotnet test";
        Prompt = string.Empty;
        Confirm = "Run the test suite now?";
        NeedsCommit = false;
        SelectedTool = null;
    }
}
