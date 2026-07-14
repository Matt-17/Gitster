using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using Gitster.Services;
using Gitster.Core;
using Gitster.Views.Helper;

namespace Gitster.Tests;

[STATestClass]
[DoNotParallelize]
public sealed class PersistedGridSplitterTests
{
    [STATestMethod]
    public void Loaded_AppliesStoredColumnLength()
    {
        var preferences = CreatePreferences();
        preferences.SetSplitterLength("test.column", 275);
        PersistedGridSplitter.Initialize(preferences);

        var (grid, splitter) = CreateColumnGrid();
        PersistedGridSplitter.SetKey(splitter, "test.column");
        PersistedGridSplitter.SetDefinitionKind(splitter, PersistedGridDefinitionKind.Column);
        PersistedGridSplitter.SetTargetIndex(splitter, 2);

        splitter.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, splitter));

        Assert.AreEqual(GridUnitType.Pixel, grid.ColumnDefinitions[2].Width.GridUnitType);
        Assert.AreEqual(275, grid.ColumnDefinitions[2].Width.Value, 0.001);
    }

    [STATestMethod]
    public void DragCompleted_SavesTargetRowHeight()
    {
        var preferences = CreatePreferences();
        PersistedGridSplitter.Initialize(preferences);

        var (grid, splitter) = CreateRowGrid();
        PersistedGridSplitter.SetKey(splitter, "test.row");
        PersistedGridSplitter.SetDefinitionKind(splitter, PersistedGridDefinitionKind.Row);
        PersistedGridSplitter.SetTargetIndex(splitter, 3);
        Measure(grid, width: 500, height: 400);

        splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, false)
        {
            RoutedEvent = GridSplitter.DragCompletedEvent,
            Source = splitter,
        });

        Assert.AreEqual(140, preferences.GetSplitterLength("test.row")!.Value, 0.5);
    }

    private static UiPreferencesService CreatePreferences()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new UiPreferencesService(Path.Combine(dir, "ui-settings.json"));
    }

    private static (Grid Grid, GridSplitter Splitter) CreateColumnGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320), MinWidth = 260 });

        var splitter = new GridSplitter();
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        return (grid, splitter);
    }

    private static (Grid Grid, GridSplitter Splitter) CreateRowGrid()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140), MinHeight = 60 });

        grid.Children.Add(new Border { Height = 20 });

        var splitter = new GridSplitter();
        Grid.SetRow(splitter, 2);
        grid.Children.Add(splitter);
        return (grid, splitter);
    }

    private static void Measure(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
}
