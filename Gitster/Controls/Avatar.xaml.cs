using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Gitster.Services;

namespace Gitster.Controls;

/// <summary>
/// A small author avatar (plan A14). Shows initials-in-a-circle immediately; when
/// <see cref="Enabled"/> is set it fetches the Gravatar asynchronously and swaps it in,
/// falling back to initials when there is none or the fetch fails.
/// </summary>
public partial class Avatar : UserControl
{
    private int _requestToken;

    public Avatar()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty EmailProperty =
        DependencyProperty.Register(nameof(Email), typeof(string), typeof(Avatar),
            new PropertyMetadata(null, OnInputChanged));

    public string? Email
    {
        get => (string?)GetValue(EmailProperty);
        set => SetValue(EmailProperty, value);
    }

    public static readonly DependencyProperty DisplayNameProperty =
        DependencyProperty.Register(nameof(DisplayName), typeof(string), typeof(Avatar),
            new PropertyMetadata(null, OnInputChanged));

    public string? DisplayName
    {
        get => (string?)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.Register(nameof(Enabled), typeof(bool), typeof(Avatar),
            new PropertyMetadata(false, OnInputChanged));

    public bool Enabled
    {
        get => (bool)GetValue(EnabledProperty);
        set => SetValue(EnabledProperty, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((Avatar)d).Refresh();

    private void Refresh()
    {
        // Initials + seeded colour first, always.
        InitialsText.Text = GravatarService.Initials(DisplayName);
        InitialsCircle.Background = new SolidColorBrush(GravatarService.ColorFor(Email ?? DisplayName));
        InitialsCircle.Visibility = Visibility.Visible;
        ImageCircle.Visibility = Visibility.Collapsed;

        var token = ++_requestToken;          // invalidate any in-flight request (recycled rows)
        if (!Enabled || string.IsNullOrWhiteSpace(Email))
            return;

        var email = Email;
        _ = LoadAsync(email, token);
    }

    private async Task LoadAsync(string email, int token)
    {
        var image = await GravatarService.GetAvatarAsync(email);
        // Bail if this Avatar was recycled to a different author meanwhile.
        if (image == null || token != _requestToken) return;

        ImageFill.ImageSource = image;
        ImageCircle.Visibility = Visibility.Visible;
        InitialsCircle.Visibility = Visibility.Collapsed;
    }
}
