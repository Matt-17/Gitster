namespace Gitster.Services.History;

public static class CommitGraphPalette
{
    public static IReadOnlyList<string> HexColors { get; } =
    [
        "#007ACC",
        "#C2410C",
        "#15803D",
        "#7C3AED",
        "#B91C1C",
        "#0E7490",
        "#BE185D",
        "#475569",
    ];

    public static int Count => HexColors.Count;
}
