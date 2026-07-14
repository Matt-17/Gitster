namespace Gitster.Models;

public sealed record AuthorEntry(string Name, string Email)
{
    public string DisplayName => $"{Name} <{Email}>";
    public override string ToString() => DisplayName;
}
