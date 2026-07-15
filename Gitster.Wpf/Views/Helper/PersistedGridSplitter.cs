using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

using Gitster.Services;
using Gitster.Core;

namespace Gitster.Views.Helper;

public enum PersistedGridDefinitionKind
{
    Column,
    Row,
}

public static class PersistedGridSplitter
{
    private static readonly DependencyProperty IsAttachedProperty = DependencyProperty.RegisterAttached(
        "IsAttached",
        typeof(bool),
        typeof(PersistedGridSplitter),
        new PropertyMetadata(false));

    private static readonly List<WeakReference<GridSplitter>> TrackedSplitters = [];

    private static UiPreferencesService? _preferences;

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(PersistedGridSplitter),
        new PropertyMetadata(null, OnSplitterPropertyChanged));

    public static readonly DependencyProperty DefinitionKindProperty = DependencyProperty.RegisterAttached(
        "DefinitionKind",
        typeof(PersistedGridDefinitionKind),
        typeof(PersistedGridSplitter),
        new PropertyMetadata(PersistedGridDefinitionKind.Column, OnSplitterPropertyChanged));

    public static readonly DependencyProperty TargetIndexProperty = DependencyProperty.RegisterAttached(
        "TargetIndex",
        typeof(int),
        typeof(PersistedGridSplitter),
        new PropertyMetadata(-1, OnSplitterPropertyChanged));

    public static void Initialize(UiPreferencesService preferences)
    {
        _preferences = preferences;
        RefreshTrackedSplitters();
    }

    public static string? GetKey(DependencyObject obj) => (string?)obj.GetValue(KeyProperty);
    public static void SetKey(DependencyObject obj, string? value) => obj.SetValue(KeyProperty, value);

    public static PersistedGridDefinitionKind GetDefinitionKind(DependencyObject obj) =>
        (PersistedGridDefinitionKind)obj.GetValue(DefinitionKindProperty);

    public static void SetDefinitionKind(DependencyObject obj, PersistedGridDefinitionKind value) =>
        obj.SetValue(DefinitionKindProperty, value);

    public static int GetTargetIndex(DependencyObject obj) => (int)obj.GetValue(TargetIndexProperty);
    public static void SetTargetIndex(DependencyObject obj, int value) => obj.SetValue(TargetIndexProperty, value);

    private static void OnSplitterPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GridSplitter splitter)
            return;

        // Only wire up here — do NOT apply the saved length yet. XAML assigns attached properties
        // one at a time in document order, so when Key arrives TargetIndex is still -1 and the
        // fallback below would resolve to the wrong definition and overwrite it with an absolute
        // pixel width (which silently destroys a "*" column). Loaded/Initialize apply it once every
        // property is set.
        EnsureAttached(splitter);
    }

    private static void EnsureAttached(GridSplitter splitter)
    {
        if ((bool)splitter.GetValue(IsAttachedProperty))
            return;

        splitter.SetValue(IsAttachedProperty, true);
        splitter.Loaded += OnLoaded;
        splitter.IsVisibleChanged += OnIsVisibleChanged;
        splitter.DragCompleted += OnDragCompleted;
        Track(splitter);

        // XAML always finishes setting the attached properties before Loaded, so Loaded is the
        // natural apply point. A splitter configured from code may already be loaded, though —
        // deferring lets the remaining properties land first.
        if (splitter.IsLoaded)
            splitter.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ApplySavedLength(splitter));
    }

    private static void Track(GridSplitter splitter)
    {
        TrackedSplitters.RemoveAll(reference => !reference.TryGetTarget(out _));
        if (!TrackedSplitters.Any(reference => reference.TryGetTarget(out var tracked) && ReferenceEquals(tracked, splitter)))
            TrackedSplitters.Add(new WeakReference<GridSplitter>(splitter));
    }

    private static void RefreshTrackedSplitters()
    {
        TrackedSplitters.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in TrackedSplitters.ToList())
        {
            if (!reference.TryGetTarget(out var splitter))
                continue;

            if (splitter.Dispatcher.CheckAccess())
                ApplySavedLength(splitter);
            else if (!splitter.Dispatcher.HasShutdownStarted)
                splitter.Dispatcher.BeginInvoke(() => ApplySavedLength(splitter));
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is GridSplitter splitter)
            ApplySavedLength(splitter);
    }

    /// <summary>Re-applies the saved length when a collapsed pane is expanded again.</summary>
    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not GridSplitter splitter || e.NewValue is not true)
            return;

        // Deferred: the style triggers that release the collapsed MaxWidth react to the same
        // property change, and the order is not guaranteed. Re-apply once layout has settled,
        // otherwise the saved length would be clamped against a MaxWidth that is still 0.
        splitter.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ApplySavedLength(splitter));
    }

    private static void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is GridSplitter splitter)
            SaveCurrentLength(splitter);
    }

    private static void ApplySavedLength(GridSplitter splitter)
    {
        // A hidden splitter means its pane is collapsed. Its target definition is pinned to
        // zero (via MaxWidth), so restoring now would clamp the saved length to 0 and lose it.
        // The length is re-applied when the pane — and with it the splitter — becomes visible.
        if (splitter.Visibility != Visibility.Visible)
            return;

        var length = _preferences?.GetSplitterLength(GetKey(splitter));
        if (length is not { } value)
            return;

        switch (GetDefinitionKind(splitter))
        {
            case PersistedGridDefinitionKind.Row:
                if (TryGetTargetRow(splitter, out var row))
                    row.Height = new GridLength(Clamp(value, row.MinHeight, row.MaxHeight), GridUnitType.Pixel);
                break;

            default:
                if (TryGetTargetColumn(splitter, out var column))
                    column.Width = new GridLength(Clamp(value, column.MinWidth, column.MaxWidth), GridUnitType.Pixel);
                break;
        }
    }

    private static void SaveCurrentLength(GridSplitter splitter)
    {
        switch (GetDefinitionKind(splitter))
        {
            case PersistedGridDefinitionKind.Row:
                if (TryGetTargetRow(splitter, out var row))
                    _preferences?.SetSplitterLength(GetKey(splitter), row.ActualHeight);
                break;

            default:
                if (TryGetTargetColumn(splitter, out var column))
                    _preferences?.SetSplitterLength(GetKey(splitter), column.ActualWidth);
                break;
        }
    }

    private static bool TryGetTargetColumn(GridSplitter splitter, out ColumnDefinition column)
    {
        column = null!;
        if (splitter.Parent is not Grid grid)
            return false;

        // No guessing: an index inferred from the splitter's own position is frequently wrong
        // (a splitter often resizes the definition *before* it), and writing an absolute width to
        // the wrong column silently turns a "*" column into a fixed one.
        var index = GetTargetIndex(splitter);
        if (index < 0 || index >= grid.ColumnDefinitions.Count)
            return false;

        column = grid.ColumnDefinitions[index];
        return true;
    }

    private static bool TryGetTargetRow(GridSplitter splitter, out RowDefinition row)
    {
        row = null!;
        if (splitter.Parent is not Grid grid)
            return false;

        var index = GetTargetIndex(splitter);
        if (index < 0 || index >= grid.RowDefinitions.Count)
            return false;

        row = grid.RowDefinitions[index];
        return true;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return min > 0 ? min : 1;

        var clamped = Math.Max(value, min);
        return double.IsInfinity(max) ? clamped : Math.Min(clamped, max);
    }
}
