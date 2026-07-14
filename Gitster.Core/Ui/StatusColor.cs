namespace Gitster.Core.Ui;

/// <summary>
/// Semantic colour of a status/feedback indicator. ViewModels express intent with this;
/// each UI head maps it to its own theme colours (WPF: theme brushes via a converter).
/// </summary>
public enum StatusColor
{
    /// <summary>No emphasis — idle or informational secondary text.</summary>
    Neutral,
    Success,
    Warning,
    Danger,
    /// <summary>In-progress / informational emphasis.</summary>
    Info,
}
