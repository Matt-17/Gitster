namespace Gitster.Core.Ui;

/// <summary>
/// No-op <see cref="IUserInteraction"/> used as a safe default when a ViewModel is constructed
/// outside DI (design-time, certain tests). Silently swallows notifications and cancels prompts.
/// </summary>
public sealed class NullUserInteraction : IUserInteraction
{
    public static readonly NullUserInteraction Instance = new();

    private NullUserInteraction() { }

    public MessageResult Ask(string text, string caption, MessageButtons buttons = MessageButtons.Ok, MessageIcon icon = MessageIcon.None)
        => buttons == MessageButtons.Ok ? MessageResult.Ok : MessageResult.Cancel;

    public bool Confirm(string text, string caption) => false;

    public void Info(string text, string caption = "Gitster") { }

    public void Warning(string text, string caption = "Gitster") { }

    public void Error(string text, string caption = "Gitster") { }
}
