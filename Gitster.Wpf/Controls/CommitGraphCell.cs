using System.Windows;
using System.Windows.Media;

using Gitster.Core.Git;
using Gitster.Core.History;

namespace Gitster.Controls;

public sealed class CommitGraphCell : FrameworkElement
{
    private const double LaneSpacing = 10;
    private const double NodeRadius = 4.25;
    private const double OutgoingMarkerRadius = 2.1;
    private const double LeftPadding = 6;

    public static readonly DependencyProperty GraphRowProperty =
        DependencyProperty.Register(
            nameof(GraphRow),
            typeof(CommitGraphRow),
            typeof(CommitGraphCell),
            new FrameworkPropertyMetadata(
                CommitGraphRow.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public CommitGraphRow GraphRow
    {
        get => (CommitGraphRow)GetValue(GraphRowProperty);
        set => SetValue(GraphRowProperty, value);
    }

    public static readonly DependencyProperty IsSelectedRowProperty =
        DependencyProperty.Register(
            nameof(IsSelectedRow),
            typeof(bool),
            typeof(CommitGraphCell),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsSelectedRow
    {
        get => (bool)GetValue(IsSelectedRowProperty);
        set => SetValue(IsSelectedRowProperty, value);
    }

    public static readonly DependencyProperty RemoteStateProperty =
        DependencyProperty.Register(
            nameof(RemoteState),
            typeof(CommitRemoteState),
            typeof(CommitGraphCell),
            new FrameworkPropertyMetadata(CommitRemoteState.OnRemote, FrameworkPropertyMetadataOptions.AffectsRender));

    public CommitRemoteState RemoteState
    {
        get => (CommitRemoteState)GetValue(RemoteStateProperty);
        set => SetValue(RemoteStateProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var lanes = Math.Max(GraphRow?.LaneCount ?? 1, 1);
        return new Size(LeftPadding * 2 + lanes * LaneSpacing, availableSize.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var row = GraphRow ?? CommitGraphRow.Empty;
        foreach (var edge in row.Edges)
            DrawEdge(dc, edge);

        var center = Anchor(row.NodeLane, CommitGraphAnchor.Center);
        var laneStroke = GraphBrush(row.NodeColorIndex);
        dc.DrawEllipse(
            ResourceBrush("CommitGraphNodeFillBrush", SystemColors.WindowBrush),
            new Pen(laneStroke, IsSelectedRow ? 1.8 : 1.5),
            center,
            NodeRadius,
            NodeRadius);

        if (HasOutgoingMarker)
            dc.DrawEllipse(ResourceBrush("CommitGraphLocalOnlyBrush", SystemColors.HighlightBrush), null, center, OutgoingMarkerRadius, OutgoingMarkerRadius);
    }

    private void DrawEdge(DrawingContext dc, CommitGraphEdge edge)
    {
        var from = Anchor(edge.FromLane, edge.FromAnchor);
        var to = Anchor(edge.ToLane, edge.ToAnchor);
        var pen = new Pen(GraphBrush(edge.ColorIndex), 1.7)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        if (Math.Abs(from.X - to.X) < 0.1)
        {
            dc.DrawLine(pen, from, to);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(from, isFilled: false, isClosed: false);
            var midY = from.Y + ((to.Y - from.Y) * 0.55);
            ctx.BezierTo(
                new Point(from.X, midY),
                new Point(to.X, midY),
                to,
                isStroked: true,
                isSmoothJoin: true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private Point Anchor(int lane, CommitGraphAnchor anchor)
    {
        var row = GraphRow ?? CommitGraphRow.Empty;
        var x = LeftPadding + (lane * EffectiveLaneSpacing(row));
        var y = anchor switch
        {
            CommitGraphAnchor.Top => 0,
            CommitGraphAnchor.Center => ActualHeight / 2,
            CommitGraphAnchor.Bottom => ActualHeight,
            _ => ActualHeight / 2,
        };

        return new Point(x, y);
    }

    private double EffectiveLaneSpacing(CommitGraphRow row)
    {
        if (row.LaneCount <= 1)
            return LaneSpacing;

        var available = Math.Max(ActualWidth - (LeftPadding * 2), LaneSpacing);
        return Math.Min(LaneSpacing, available / (row.LaneCount - 1));
    }

    private bool HasOutgoingMarker =>
        RemoteState is CommitRemoteState.LocalOnly or CommitRemoteState.NoTrackingBranch;

    private static Brush GraphBrush(int colorIndex)
    {
        var keys = CommitGraphPalette.BrushKeys;
        return ResourceBrush(keys[colorIndex % keys.Count], SystemColors.HighlightBrush);
    }

    private static Brush ResourceBrush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
