namespace Gitster.Core.Ui;

/// <summary>No-op <see cref="IClipboard"/> for ViewModels constructed outside DI.</summary>
public sealed class NullClipboard : IClipboard
{
    public static readonly NullClipboard Instance = new();

    private NullClipboard() { }

    public void SetText(string text) { }
}
