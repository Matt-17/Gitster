namespace Gitster.Core.History;

internal sealed class HistoryCacheValidator
{
    public HistoryCacheOpenDecision DecideOpenState(
        HistoryContext context,
        BranchState? existing,
        bool historyRowsNeedGraphUpgrade)
    {
        var initialIsComplete = existing?.IsComplete ?? false;
        var initialCachedCount = existing?.CachedCount ?? 0;

        if (existing is null || existing.CachedCount == 0)
            return HistoryCacheOpenDecision.Keep(initialIsComplete, initialCachedCount);

        if (historyRowsNeedGraphUpgrade)
            return HistoryCacheOpenDecision.Reset(initialIsComplete, initialCachedCount);

        if (string.Equals(existing.HeadSha, context.HeadSha, StringComparison.OrdinalIgnoreCase))
            return HistoryCacheOpenDecision.Keep(initialIsComplete, initialCachedCount);

        return context.Scope == HistoryScope.CurrentBranch
            ? HistoryCacheOpenDecision.ValidateHead(initialIsComplete, initialCachedCount)
            : HistoryCacheOpenDecision.Reset(initialIsComplete, initialCachedCount);
    }
}

internal sealed record HistoryCacheOpenDecision(
    bool InitialIsComplete,
    int InitialCachedCount,
    bool ResetRows,
    bool ValidateHeadChange)
{
    public static HistoryCacheOpenDecision Keep(bool initialIsComplete, int initialCachedCount) =>
        new(initialIsComplete, initialCachedCount, ResetRows: false, ValidateHeadChange: false);

    public static HistoryCacheOpenDecision Reset(bool initialIsComplete, int initialCachedCount) =>
        new(initialIsComplete, initialCachedCount, ResetRows: true, ValidateHeadChange: false);

    public static HistoryCacheOpenDecision ValidateHead(bool initialIsComplete, int initialCachedCount) =>
        new(initialIsComplete, initialCachedCount, ResetRows: false, ValidateHeadChange: true);
}
