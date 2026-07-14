using Gitster.Core;
using Gitster.Core.Models;
using Gitster.Core.Ui;
using Gitster.Views;

namespace Gitster.Services;

/// <summary>
/// WPF implementation of <see cref="IDialogService"/>. Owns construction of the styled dialog
/// windows and marshals their results back as plain data so ViewModels stay UI-agnostic.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    private readonly IWindowService _windows;

    public WpfDialogService(IWindowService windows) => _windows = windows;

    public AuthorEntry? AddAuthor()
    {
        var dialog = new AddAuthorDialog();
        return _windows.ShowDialog(dialog) == true ? dialog.Result : null;
    }

    public AuthorSelection? EditAuthors(AuthorDirectoryService authorDir)
    {
        var dialog = new EditAuthorsDialog(authorDir);
        return _windows.ShowDialog(dialog) == true
            ? new AuthorSelection(dialog.SelectedAuthorText, dialog.SelectedCommitterText)
            : null;
    }
}
