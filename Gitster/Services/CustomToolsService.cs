using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Gitster.Models;

using LibGit2Sharp;

namespace Gitster.Services;

/// <summary>Result of running a custom tool's command.</summary>
public sealed record ToolRunResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Loads, merges, persists and runs user-defined tools. Sources (repo overrides global):
/// Git's native <c>[guitool "name"]</c> sections plus Gitster's own JSON stores at
/// <c>%AppData%/Gitster/custom-tools.json</c> (global) and <c>.git/gitster/custom-tools.json</c> (repo).
/// </summary>
public sealed class CustomToolsService
{
    private string? _repoPath;
    private string? _repoGitDir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Attach(string repoPath)
    {
        _repoPath = repoPath;

        var gitDir = Path.Combine(repoPath, ".git");
        if (File.Exists(gitDir))
        {
            var content = File.ReadAllText(gitDir).Trim();
            if (content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                gitDir = content[7..].Trim();
        }
        _repoGitDir = Directory.Exists(gitDir) ? gitDir : null;
    }

    public void Detach()
    {
        _repoPath = null;
        _repoGitDir = null;
    }

    private static string GlobalJsonPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Gitster", "custom-tools.json");

    private string? RepoJsonPath =>
        _repoGitDir == null ? null : Path.Combine(_repoGitDir, "gitster", "custom-tools.json");

    /// <summary>True when a repository is open, so repository-scoped tools can be saved.</summary>
    public bool RepositoryAvailable => RepoJsonPath != null;

    // ── Loading & merging ──────────────────────────────────────────────────

    /// <summary>Returns all tools, repo-scoped overriding global ones with the same name.</summary>
    public IReadOnlyList<CustomTool> GetTools()
    {
        var byName = new Dictionary<string, CustomTool>(StringComparer.OrdinalIgnoreCase);

        // Global first, so repo-scoped entries can override by name.
        foreach (var t in LoadJson(GlobalJsonPath, CustomToolScope.Global))
            byName[t.Name] = t;

        foreach (var t in LoadGitConfigTools())
            byName[t.Name] = t;

        if (RepoJsonPath != null)
            foreach (var t in LoadJson(RepoJsonPath, CustomToolScope.Repository))
                byName[t.Name] = t;

        // Repo-scoped first in the menu, then global, each alphabetical.
        return byName.Values
            .OrderBy(t => t.Scope == CustomToolScope.Repository ? 0 : 1)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Returns only the JSON-backed tools for a scope (the editable ones).</summary>
    public List<CustomTool> GetEditableTools(CustomToolScope scope)
    {
        var path = scope == CustomToolScope.Global ? GlobalJsonPath : RepoJsonPath;
        return path == null ? [] : LoadJson(path, scope).ToList();
    }

    private static List<CustomTool> LoadJson(string path, CustomToolScope scope)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<CustomTool>>(json, JsonOpts) ?? [];
            // Force the scope to match the store it came from.
            return list.Select(t => t with { Scope = scope, FromGitConfig = false }).ToList();
        }
        catch
        {
            return [];
        }
    }

    private IEnumerable<CustomTool> LoadGitConfigTools()
    {
        if (_repoPath == null) yield break;

        Dictionary<string, Dictionary<string, (string Value, ConfigurationLevel Level)>> tools = new(StringComparer.OrdinalIgnoreCase);

        Repository? repo = null;
        try { repo = new Repository(_repoPath); }
        catch { repo = null; }

        if (repo == null) yield break;

        using (repo)
        {
            foreach (var entry in repo.Config)
            {
                // Keys look like: guitool.<name>.<prop>  (name may contain dots)
                var key = entry.Key;
                if (!key.StartsWith("guitool.", StringComparison.OrdinalIgnoreCase)) continue;

                var rest = key["guitool.".Length..];
                var lastDot = rest.LastIndexOf('.');
                if (lastDot <= 0) continue;

                var name = rest[..lastDot];
                var prop = rest[(lastDot + 1)..].ToLowerInvariant();

                if (!tools.TryGetValue(name, out var props))
                    tools[name] = props = new(StringComparer.OrdinalIgnoreCase);
                props[prop] = (entry.Value, entry.Level);
            }
        }

        foreach (var (name, props) in tools)
        {
            if (!props.TryGetValue("cmd", out var cmd) || string.IsNullOrWhiteSpace(cmd.Value))
                continue;

            var scope = cmd.Level is ConfigurationLevel.Local or ConfigurationLevel.Worktree
                ? CustomToolScope.Repository
                : CustomToolScope.Global;

            string? confirm = null;
            if (props.TryGetValue("confirm", out var conf) && IsTrue(conf.Value))
                confirm = $"Run '{name}'?";

            string? prompt = null;
            if (props.TryGetValue("argprompt", out var ap))
                prompt = IsTrue(ap.Value) ? "Arguments:" : ap.Value;

            var needsCommit =
                cmd.Value.Contains("$REVISION", StringComparison.Ordinal) ||
                cmd.Value.Contains("$CUR", StringComparison.Ordinal) ||
                (props.TryGetValue("revprompt", out var rp) && IsTrue(rp.Value)) ||
                (props.TryGetValue("needsfile", out var nf) && IsTrue(nf.Value));

            yield return new CustomTool(name, cmd.Value, confirm, needsCommit, prompt, scope)
            {
                FromGitConfig = true,
            };
        }
    }

    private static bool IsTrue(string? v) =>
        v != null && (v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                      v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                      v.Equals("1", StringComparison.Ordinal));

    // ── Persistence (JSON-backed tools only) ────────────────────────────────

    public void Save(CustomToolScope scope, IEnumerable<CustomTool> tools)
    {
        var path = scope == CustomToolScope.Global ? GlobalJsonPath : RepoJsonPath;
        if (path == null)
            throw new InvalidOperationException("No repository is open for repository-scoped tools.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var clean = tools.Select(t => t with { Scope = scope, FromGitConfig = false }).ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(clean, JsonOpts));
    }

    // ── Placeholder substitution ─────────────────────────────────────────────

    public string Substitute(string command, string? revision, string? args, string? branch)
    {
        var sb = new StringBuilder(command);
        sb.Replace("$REVISION", revision ?? string.Empty);
        sb.Replace("$CUR", revision ?? string.Empty);
        sb.Replace("$ARGS", args ?? string.Empty);
        sb.Replace("$BRANCH", branch ?? string.Empty);
        sb.Replace("$REPO", _repoPath ?? string.Empty);
        return sb.ToString();
    }

    // ── Running ──────────────────────────────────────────────────────────────

    /// <summary>Runs an arbitrary shell command in the repo working directory.</summary>
    public async Task<ToolRunResult> RunAsync(string command, CancellationToken ct = default)
    {
        if (_repoPath == null)
            throw new InvalidOperationException("No repository is open.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromMinutes(5));
        var token = linkedCts.Token;

        var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            WorkingDirectory       = _repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { }
            throw new TimeoutException("The tool did not finish within 5 minutes and was terminated.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\n{stderr}";
        return new ToolRunResult(process.ExitCode, output.Trim());
    }
}
