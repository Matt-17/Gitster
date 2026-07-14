using Gitster.Core.Git;

namespace Gitster.Core.History;

public enum CommitGraphAnchor
{
    Top,
    Center,
    Bottom,
}

public sealed record CommitGraphNode(
    string Sha,
    IReadOnlyList<string> ParentShas,
    IReadOnlyList<CommitRefLabel> RefLabels);

public sealed record CommitGraphEdge(
    int FromLane,
    CommitGraphAnchor FromAnchor,
    int ToLane,
    CommitGraphAnchor ToAnchor,
    int ColorIndex);

public sealed record CommitGraphRow(
    int NodeLane,
    int LaneCount,
    int NodeColorIndex,
    IReadOnlyList<CommitGraphEdge> Edges,
    IReadOnlyList<CommitRefLabel> RefLabels)
{
    public static readonly CommitGraphRow Empty = new(
        NodeLane: 0,
        LaneCount: 1,
        NodeColorIndex: 0,
        Edges: Array.Empty<CommitGraphEdge>(),
        RefLabels: Array.Empty<CommitRefLabel>());
}
