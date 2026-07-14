using Gitster.Core.Ui;

namespace Gitster.Services;

/// <summary>WPF implementation of <see cref="IClipboard"/>.</summary>
public sealed class WpfClipboard : IClipboard
{
    public void SetText(string text) => System.Windows.Clipboard.SetText(text);
}
