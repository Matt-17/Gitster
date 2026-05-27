namespace Gitster.Services.Git;

public sealed record BranchInfo(string Name, int Incoming, int Outgoing);
