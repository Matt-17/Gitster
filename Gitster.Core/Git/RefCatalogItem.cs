namespace Gitster.Core.Git;

public enum RefCatalogKind
{
    LocalBranch,
    RemoteBranch,
    Tag,
    Stash,
    Note,
    Replace,
}

public sealed record RefCatalogItem(
    string DisplayName,
    string CanonicalName,
    RefCatalogKind Kind,
    string TipSha,
    bool IsCurrent,
    bool HasUpstream,
    int Ahead,
    int Behind);
