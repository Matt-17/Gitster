namespace Gitster.Core.Models;

/// <summary>
/// UI-framework-neutral window state persisted in settings. Mirrors the three values of
/// WPF's <c>System.Windows.WindowState</c>; the WPF head maps between them at its boundary.
/// String names are kept identical so previously persisted settings deserialize unchanged.
/// </summary>
public enum WindowStateKind
{
    Normal,
    Minimized,
    Maximized,
}
