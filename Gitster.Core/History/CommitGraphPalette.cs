namespace Gitster.Core.History;

public static class CommitGraphPalette
{
    public static IReadOnlyList<string> BrushKeys { get; } =
    [
        "CommitGraphColor0",
        "CommitGraphColor1",
        "CommitGraphColor2",
        "CommitGraphColor3",
        "CommitGraphColor4",
        "CommitGraphColor5",
        "CommitGraphColor6",
        "CommitGraphColor7",
    ];

    public static int Count => BrushKeys.Count;
}
