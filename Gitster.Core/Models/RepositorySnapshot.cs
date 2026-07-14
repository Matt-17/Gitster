namespace Gitster.Core.Models;

public sealed record RepositorySnapshot(
    string Id,
    DateTimeOffset CapturedAt,
    string TriggerDescription,
    Dictionary<string, string> RefStates);
