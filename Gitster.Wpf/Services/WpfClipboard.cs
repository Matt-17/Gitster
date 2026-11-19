using System.Runtime.InteropServices;
using System.Threading;
using Gitster.Core.Ui;

namespace Gitster.Services;

/// <summary>WPF implementation of <see cref="IClipboard"/>.</summary>
public sealed class WpfClipboard : IClipboard
{
    private const int Attempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(30);

    public void SetText(string text) => TrySetText(text);

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard. Another process (clipboard managers,
    /// remote desktop) may hold the clipboard open for a moment, so busy failures are retried
    /// instead of silently dropping the copy. Returns false when it never got through.
    /// </summary>
    public static bool TrySetText(string text)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (ExternalException) when (attempt < Attempts)
            {
                Thread.Sleep(RetryDelay);
            }
            catch (ExternalException)
            {
                return false;
            }
        }
    }
}
