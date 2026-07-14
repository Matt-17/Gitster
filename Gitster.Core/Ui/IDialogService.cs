using Gitster.Core.Models;

namespace Gitster.Core.Ui;

/// <summary>
/// Abstracts modal dialogs behind intent-revealing, UI-framework-free methods that return data.
/// The WPF head implements this with the styled dialog windows; a future Blazor head implements it
/// with its own components. ViewModels depend only on this port, never on the concrete dialogs.
/// </summary>
/// <remarks>Methods return <c>null</c> (or the documented "cancelled" value) when the user dismisses
/// the dialog without confirming.</remarks>
public interface IDialogService
{
    /// <summary>Prompts for a new author identity. Returns the entry, or null if cancelled.</summary>
    AuthorEntry? AddAuthor();

    /// <summary>Opens the author-directory editor. Returns the chosen author/committer text, or null if cancelled.</summary>
    AuthorSelection? EditAuthors(AuthorDirectoryService authorDir);
}

/// <summary>Author/committer identity text chosen in the author-directory editor.</summary>
public sealed record AuthorSelection(string AuthorText, string CommitterText);
