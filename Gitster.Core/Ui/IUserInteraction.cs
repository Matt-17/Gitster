namespace Gitster.Core.Ui;

/// <summary>Buttons offered by a message prompt (UI-framework-neutral).</summary>
public enum MessageButtons { Ok, OkCancel, YesNo, YesNoCancel }

/// <summary>Severity/glyph of a message prompt.</summary>
public enum MessageIcon { None, Information, Warning, Error, Question }

/// <summary>The button the user chose.</summary>
public enum MessageResult { Ok, Cancel, Yes, No }

/// <summary>
/// Simple modal notifications and confirmations, free of any UI framework. The WPF head implements
/// this with the styled <c>GitsterDialog</c> (never a raw MessageBox); a Blazor head uses its own modal.
/// </summary>
public interface IUserInteraction
{
    MessageResult Ask(string text, string caption, MessageButtons buttons = MessageButtons.Ok, MessageIcon icon = MessageIcon.None);

    bool Confirm(string text, string caption);

    void Info(string text, string caption = "Gitster");

    void Warning(string text, string caption = "Gitster");

    void Error(string text, string caption = "Gitster");
}
