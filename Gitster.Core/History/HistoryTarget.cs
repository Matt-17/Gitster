namespace Gitster.Core.History;

public sealed record HistoryTarget(
    HistoryScope Scope,
    string? RefName = null,
    string? DisplayName = null)
{
    public static HistoryTarget CurrentBranch { get; } = new(HistoryScope.CurrentBranch);
    public static HistoryTarget AllBranches { get; } = new(HistoryScope.AllBranches);

    public static HistoryTarget ForScope(HistoryScope scope) =>
        scope == HistoryScope.AllBranches ? AllBranches : CurrentBranch;

    public static HistoryTarget ForRef(string refName, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(refName))
            throw new ArgumentException("Ref name is required.", nameof(refName));

        return new HistoryTarget(HistoryScope.Ref, refName.Trim(), displayName?.Trim());
    }
}
