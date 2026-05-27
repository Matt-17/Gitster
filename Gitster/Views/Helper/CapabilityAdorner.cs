using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Gitster.Views.Helper;

public class CapabilityAdorner : Adorner
{
    private static readonly Brush DotBrush;

    static CapabilityAdorner()
    {
        DotBrush = new SolidColorBrush(Color.FromArgb(180, 120, 120, 120));
        DotBrush.Freeze();
    }

    public CapabilityAdorner(UIElement adornedElement, string reason) : base(adornedElement)
    {
        IsHitTestVisible = true;
        ToolTip = $"Unavailable – {reason}";
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(AdornedElement.RenderSize);
        var center = new Point(rect.Right - 7, rect.Top + 7);
        dc.DrawEllipse(DotBrush, null, center, 3.5, 3.5);
    }

    public static void Attach(FrameworkElement element, string reason)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null)
        {
            element.Loaded += DeferredAttach;
            return;
        }

        Detach(element);
        layer.Add(new CapabilityAdorner(element, reason));

        void DeferredAttach(object? sender, RoutedEventArgs e)
        {
            element.Loaded -= DeferredAttach;
            Attach(element, reason);
        }
    }

    public static void Detach(FrameworkElement element)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null) return;
        var existing = layer.GetAdorners(element);
        if (existing is null) return;
        foreach (var ad in existing.OfType<CapabilityAdorner>())
            layer.Remove(ad);
    }
}
