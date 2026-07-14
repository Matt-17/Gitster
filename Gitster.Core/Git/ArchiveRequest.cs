namespace Gitster.Core.Git;

public sealed record ArchiveRequest(
    string TreeishSha,
    string OutputPath,
    string Prefix);

public sealed record ArchiveResult(
    string OutputPath,
    string TreeishSha,
    long SizeBytes);
