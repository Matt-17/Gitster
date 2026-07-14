namespace Gitster.Services.History;

public sealed class CommitGraphLayoutService
{
    public IReadOnlyDictionary<string, CommitGraphRow> Layout(IReadOnlyList<CommitGraphNode> commits)
    {
        var visible = commits
            .Select(c => c.Sha)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var active = new List<ActiveLane>();
        var rows = new Dictionary<string, CommitGraphRow>(StringComparer.OrdinalIgnoreCase);
        var nextColorIndex = 0;

        foreach (var commit in commits)
        {
            var sha = commit.Sha;
            var topActive = active.ToList();
            var lane = active.FindIndex(l => SameSha(l.Sha, sha));
            if (lane < 0)
            {
                lane = active.Count;
                active.Add(new ActiveLane(sha, nextColorIndex));
                nextColorIndex = NextColor(nextColorIndex);
            }

            var nodeColorIndex = active[lane].ColorIndex;
            var visibleParents = commit.ParentShas
                .Where(p => visible.Contains(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var parentLanes = AssignParentLanes(active, lane, nodeColorIndex, visibleParents, ref nextColorIndex);
            var bottomActive = active.ToList();

            var edges = new List<CommitGraphEdge>();

            AddTopEdges(topActive, bottomActive, sha, lane, edges);
            AddParentEdges(lane, parentLanes, edges);

            var laneCount = Math.Max(
                Math.Max(topActive.Count, bottomActive.Count),
                lane + 1);

            rows[sha] = new CommitGraphRow(
                lane,
                Math.Max(laneCount, 1),
                nodeColorIndex,
                edges,
                commit.RefLabels);
        }

        return rows;
    }

    private static Dictionary<string, LaneAssignment> AssignParentLanes(
        List<ActiveLane> active,
        int lane,
        int currentColorIndex,
        IReadOnlyList<string> visibleParents,
        ref int nextColorIndex)
    {
        var parentLanes = new Dictionary<string, LaneAssignment>(StringComparer.OrdinalIgnoreCase);

        if (visibleParents.Count == 0)
        {
            active.RemoveAt(lane);
            return parentLanes;
        }

        var firstParent = visibleParents[0];
        var firstParentLane = active.FindIndex(l => SameSha(l.Sha, firstParent));
        var insertionLane = lane + 1;
        if (firstParentLane >= 0 && firstParentLane != lane)
        {
            active.RemoveAt(lane);
            if (firstParentLane > lane)
                firstParentLane--;
            parentLanes[firstParent] = new LaneAssignment(firstParentLane, currentColorIndex);
            insertionLane = firstParentLane + 1;
        }
        else
        {
            active[lane] = new ActiveLane(firstParent, currentColorIndex);
            parentLanes[firstParent] = new LaneAssignment(lane, currentColorIndex);
        }

        foreach (var parent in visibleParents.Skip(1))
        {
            var existingLane = active.FindIndex(l => SameSha(l.Sha, parent));
            if (existingLane >= 0)
            {
                parentLanes[parent] = new LaneAssignment(existingLane, active[existingLane].ColorIndex);
                continue;
            }

            var colorIndex = nextColorIndex;
            nextColorIndex = NextColor(nextColorIndex);
            active.Insert(insertionLane, new ActiveLane(parent, colorIndex));
            parentLanes[parent] = new LaneAssignment(insertionLane, colorIndex);
            insertionLane++;
        }

        return parentLanes;
    }

    private static void AddTopEdges(
        IReadOnlyList<ActiveLane> topActive,
        IReadOnlyList<ActiveLane> bottomActive,
        string currentSha,
        int currentLane,
        List<CommitGraphEdge> edges)
    {
        for (var topLane = 0; topLane < topActive.Count; topLane++)
        {
            var activeLane = topActive[topLane];
            if (SameSha(activeLane.Sha, currentSha))
            {
                edges.Add(new CommitGraphEdge(
                    topLane,
                    CommitGraphAnchor.Top,
                    currentLane,
                    CommitGraphAnchor.Center,
                    activeLane.ColorIndex));
                continue;
            }

            var bottomLane = FindLane(bottomActive, activeLane.Sha);
            if (bottomLane < 0)
            {
                edges.Add(new CommitGraphEdge(
                    topLane,
                    CommitGraphAnchor.Top,
                    topLane,
                    CommitGraphAnchor.Center,
                    activeLane.ColorIndex));
                continue;
            }

            edges.Add(new CommitGraphEdge(
                topLane,
                CommitGraphAnchor.Top,
                bottomLane,
                CommitGraphAnchor.Bottom,
                activeLane.ColorIndex));
        }
    }

    private static void AddParentEdges(
        int currentLane,
        IReadOnlyDictionary<string, LaneAssignment> parentLanes,
        List<CommitGraphEdge> edges)
    {
        foreach (var (_, parentLane) in parentLanes)
        {
            edges.Add(new CommitGraphEdge(
                currentLane,
                CommitGraphAnchor.Center,
                parentLane.Lane,
                CommitGraphAnchor.Bottom,
                parentLane.ColorIndex));
        }
    }

    private static int FindLane(IReadOnlyList<ActiveLane> lanes, string sha)
    {
        for (var i = 0; i < lanes.Count; i++)
            if (SameSha(lanes[i].Sha, sha))
                return i;
        return -1;
    }

    private static bool SameSha(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static int NextColor(int current) => (current + 1) % CommitGraphPalette.Count;

    private sealed record ActiveLane(string Sha, int ColorIndex);

    private sealed record LaneAssignment(int Lane, int ColorIndex);
}
