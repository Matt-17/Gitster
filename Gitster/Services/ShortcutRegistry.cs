namespace Gitster.Services;

public sealed record ShortcutEntry(string Area, string Gesture, string Command);

public static class ShortcutRegistry
{
    public static IReadOnlyList<ShortcutEntry> All { get; } =
    [
        new("Repository", "Ctrl+O", "Open repository"),
        new("Repository", "F5", "Fetch remote updates"),
        new("Repository", "Ctrl+K", "Open commit panel"),
        new("Repository", "Ctrl+Enter", "Amend selected commit"),
        new("Edit", "Ctrl+Z", "Undo last operation"),
        new("Edit", "Ctrl+F", "Focus commit search"),
        new("View", "Ctrl+1", "Commits"),
        new("View", "Ctrl+2", "Stashes"),
        new("View", "Ctrl+3", "Branches"),
        new("View", "Ctrl+4", "Worktrees"),
        new("View", "Ctrl+5", "Search"),
        new("View", "Ctrl+6", "Operations log"),
        new("View", "Alt+Up/Down", "Move commit selection"),
        new("View", "Alt+PageUp/PageDown", "Move across commit ancestry"),
        new("Tools", "Ctrl+/", "Keyboard shortcuts"),
        new("Tools", "F1", "Keyboard shortcuts"),
        new("Tools", "Ctrl+Shift+P", "Command palette"),
    ];
}
