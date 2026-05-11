using System.IO;

namespace Firepit.Core.Mcp;

/// <summary>
/// Pre-flight check for a project's resolved MCP servers. Cheap, optimistic
/// PATH resolution for stdio servers — catches the "binary not installed"
/// failure mode that v0.5.0 silently hid (see issue #4). Does not attempt to
/// spawn the process; can't verify runtime crashes or bad-args failures.
/// That richer signal belongs to a later iteration that parses Claude Code's
/// own MCP startup output.
/// </summary>
public sealed class McpHealthChecker
{
    private readonly Func<string?> _readPath;
    private readonly Func<string?> _readPathExt;
    private readonly Func<string, bool> _fileExists;

    public McpHealthChecker()
        : this(
            readPath:    () => Environment.GetEnvironmentVariable("PATH"),
            readPathExt: () => Environment.GetEnvironmentVariable("PATHEXT"),
            fileExists:  File.Exists)
    {
    }

    public McpHealthChecker(
        Func<string?> readPath,
        Func<string?> readPathExt,
        Func<string, bool> fileExists)
    {
        _readPath = readPath;
        _readPathExt = readPathExt;
        _fileExists = fileExists;
    }

    public IReadOnlyList<McpHealthIssue> Check(IReadOnlyList<ResolvedMcpServer> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);

        var issues = new List<McpHealthIssue>();
        foreach (var server in servers)
        {
            if (server.Transport != McpTransport.Stdio) continue;
            if (string.IsNullOrWhiteSpace(server.Command)) continue;

            if (!Resolve(server.Command!))
            {
                issues.Add(new McpHealthIssue(
                    ServerId: server.Id,
                    DisplayName: server.DisplayName,
                    Kind: McpHealthIssueKind.CommandNotFound,
                    Detail: $"`{server.Command}` not found on PATH."));
            }
        }
        return issues;
    }

    private bool Resolve(string command)
    {
        // Absolute / relative path with a separator → just check existence
        // (with PATHEXT expansion).
        if (command.IndexOfAny(['\\', '/']) >= 0)
        {
            return ExistsWithExt(command);
        }

        var path = _readPath() ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), command);
            if (ExistsWithExt(candidate)) return true;
        }
        return false;
    }

    private bool ExistsWithExt(string pathBase)
    {
        if (_fileExists(pathBase)) return true;

        // On Windows, callers usually omit the .exe suffix. Probe PATHEXT.
        var pathExt = _readPathExt();
        if (string.IsNullOrWhiteSpace(pathExt)) return false;

        foreach (var ext in pathExt.Split(Path.PathSeparator))
        {
            var trimmed = ext.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (_fileExists(pathBase + trimmed)) return true;
        }
        return false;
    }
}

public enum McpHealthIssueKind
{
    CommandNotFound,
}

public sealed record McpHealthIssue(
    string ServerId,
    string DisplayName,
    McpHealthIssueKind Kind,
    string Detail);
