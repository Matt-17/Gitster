namespace Gitster.Services.Features;

public enum CommitSigningStatus
{
    Unknown,
    Good,
    Bad,
    Untrusted,
    NoSignature,
}

public static class CommitSigningStatusParser
{
    public static CommitSigningStatus Parse(string marker) =>
        marker.Trim() switch
        {
            "G" => CommitSigningStatus.Good,
            "B" => CommitSigningStatus.Bad,
            "U" => CommitSigningStatus.Untrusted,
            "N" => CommitSigningStatus.NoSignature,
            _ => CommitSigningStatus.Unknown,
        };
}
