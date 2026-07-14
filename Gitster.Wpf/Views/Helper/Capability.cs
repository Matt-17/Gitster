using System.Windows;
using System.Windows.Documents;

using Gitster.ApplicationLayer.Capabilities;

namespace Gitster.Views.Helper;

public static class Capability
{
    public static readonly DependencyProperty RequiresProperty = DependencyProperty.RegisterAttached(
        "Requires", typeof(string), typeof(Capability),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRequiresChanged));

    public static readonly DependencyProperty VisibleIfProperty = DependencyProperty.RegisterAttached(
        "VisibleIf", typeof(string), typeof(Capability),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnVisibleIfChanged));

    public static string GetRequires(DependencyObject obj) => (string)obj.GetValue(RequiresProperty);
    public static void SetRequires(DependencyObject obj, string value) => obj.SetValue(RequiresProperty, value);

    public static string GetVisibleIf(DependencyObject obj) => (string)obj.GetValue(VisibleIfProperty);
    public static void SetVisibleIf(DependencyObject obj, string value) => obj.SetValue(VisibleIfProperty, value);

    private static CapabilityService? _service;

    public static void Initialize(CapabilityService service)
    {
        _service = service;
        service.PropertyChanged += (_, _) => RefreshAll();
    }

    private static readonly List<WeakReference<FrameworkElement>> _trackedElements = [];

    private static void OnRequiresChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        Track(element);
        ApplyRequires(element, (string?)e.NewValue);
    }

    private static void OnVisibleIfChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        Track(element);
        ApplyVisibleIf(element, (string?)e.NewValue);
    }

    private static void Track(FrameworkElement element)
    {
        _trackedElements.RemoveAll(wr => !wr.TryGetTarget(out _));
        if (!_trackedElements.Any(wr => wr.TryGetTarget(out var t) && t == element))
            _trackedElements.Add(new WeakReference<FrameworkElement>(element));
    }

    private static void RefreshAll()
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var wr in _trackedElements.ToList())
            {
                if (wr.TryGetTarget(out var element))
                {
                    ApplyRequires(element, GetRequires(element));
                    ApplyVisibleIf(element, GetVisibleIf(element));
                }
            }
        });
    }

    private static void ApplyRequires(FrameworkElement element, string? capability)
    {
        if (string.IsNullOrEmpty(capability) || _service is null)
        {
            element.ClearValue(UIElement.IsEnabledProperty);
            CapabilityAdorner.Detach(element);
            return;
        }

        var available = _service.Requires(capability);
        if (!available)
        {
            element.IsEnabled = false;
            CapabilityAdorner.Attach(element, _service.GetMissingReason(capability));
        }
        else
        {
            element.ClearValue(UIElement.IsEnabledProperty);
            CapabilityAdorner.Detach(element);
        }
    }

    private static void ApplyVisibleIf(FrameworkElement element, string? capability)
    {
        if (string.IsNullOrEmpty(capability) || _service is null)
        {
            element.Visibility = Visibility.Visible;
            return;
        }
        element.Visibility = _service.Requires(capability) ? Visibility.Visible : Visibility.Collapsed;
    }
}
