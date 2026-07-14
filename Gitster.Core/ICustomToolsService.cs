using Gitster.Core.Models;

namespace Gitster.Core;

public interface ICustomToolsService
{
    bool RepositoryAvailable { get; }

    void Attach(string repoPath);
    void Detach();
    IReadOnlyList<CustomTool> GetTools();
    List<CustomTool> GetEditableTools(CustomToolScope scope);
    void Save(CustomToolScope scope, IEnumerable<CustomTool> tools);
    string Substitute(string command, string? revision, string? args, string? branch);
    Task<ToolRunResult> RunAsync(string command, CancellationToken ct = default);
}
