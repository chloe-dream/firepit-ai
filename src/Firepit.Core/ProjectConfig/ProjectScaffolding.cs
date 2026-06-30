using System.IO;
using System.Text;

namespace Firepit.Core.ProjectConfig;

/// <summary>
/// Outcome of <see cref="ProjectScaffolding.EnsureProjectScaffold"/>. Lets the
/// shell surface what the first-scaffold hardening did — and, crucially, warn
/// (and offer migration) when the project's <c>.gitignore</c> blanket-ignores
/// the shared Firepit/Claude config.
/// </summary>
public sealed record ProjectScaffoldResult(
    string ConfigPath,
    bool ScaffoldCreated,
    bool GitignoreUpdated,
    bool ClaudeMdSeeded,
    IReadOnlyList<string> BlanketIgnores);

/// <summary>
/// First-scaffold git hygiene for a project. When Firepit creates a project's
/// <c>.firepit/config.json</c> for the first time it also (idempotently):
/// <list type="number">
///   <item>ensures a root <c>.gitignore</c> block that versions the shared
///   config (config.json, .claude/mcp.json, settings.json, commands/, agents/)
///   while ignoring the ephemeral/personal bits (inbox, runs, *.local.json,
///   *.lock, agent-memory);</item>
///   <item>seeds the "read the inbox on session start" convention into the
///   project's CLAUDE.md — the piece that was missing in the field;</item>
///   <item>detects blanket <c>.firepit/</c> / <c>.claude/</c> ignores that
///   swallow the shared config so the shell can warn and offer a fix.</item>
/// </list>
/// The hardening only fires on the INITIAL scaffold, so existing repos are
/// left untouched (they're handled out-of-band).
/// </summary>
public static class ProjectScaffolding
{
    // The shared-vs-local split — see docs/ARCHITECTURE.md §9. Tracked: the
    // declarative, shareable config (no plaintext secrets — only ${cred:...}
    // references resolved via Windows Credential Manager). Ignored below: the
    // ephemeral / per-machine / personal runtime state.
    private static readonly string[] GitignoreEntries =
    {
        ".firepit/inbox/",
        ".firepit/runs/",
        "!.firepit/config.json",
        ".claude/settings.local.json",
        ".claude/*.lock",
        ".claude/agent-memory/",
    };

    private const string GitignoreHeader =
        "# Firepit + Claude Code — shared config versioned, runtime/personal local";

    // Idempotency marker for the CLAUDE.md inbox section. If the file already
    // mentions the completion tool we assume the convention is documented.
    private const string InboxConventionMarker = "firepit_inbox_complete";

    private static readonly string[] BlanketIgnorePatterns =
    {
        ".firepit", ".firepit/", "/.firepit", "/.firepit/",
        ".claude", ".claude/", "/.claude", "/.claude/",
    };

    /// <summary>
    /// Ensure <c>.firepit/config.json</c> exists for the project, and — only
    /// when that file is created for the FIRST time — set up git hygiene and
    /// seed the inbox convention. Idempotent: safe to call on every config
    /// open; the hardening is gated on the fresh-scaffold transition.
    /// </summary>
    public static ProjectScaffoldResult EnsureProjectScaffold(string projectPath, string projectId)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(projectId);

        var configPath = ProjectConfigScaffold.GetConfigPath(projectPath);
        var fresh = !File.Exists(configPath);
        ProjectConfigScaffold.EnsureScaffold(projectPath, projectId);

        if (!fresh)
        {
            // Existing project — leave its git setup alone (per-repo briefing
            // is handled out-of-band). Nothing to warn about here.
            return new ProjectScaffoldResult(configPath, false, false, false, Array.Empty<string>());
        }

        var gitignoreUpdated = EnsureGitignoreBlock(projectPath);
        var claudeSeeded     = EnsureInboxConvention(projectPath);
        var blanket          = DetectBlanketIgnores(projectPath);
        return new ProjectScaffoldResult(configPath, true, gitignoreUpdated, claudeSeeded, blanket);
    }

    /// <summary>
    /// Append any missing entries from the shared/local split to the project's
    /// root <c>.gitignore</c>, under a single Firepit header. Returns true if
    /// the file was created or appended to; false if everything was present.
    /// </summary>
    public static bool EnsureGitignoreBlock(string projectPath)
    {
        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        var existing = File.Exists(gitignorePath) ? File.ReadAllText(gitignorePath) : null;

        var present = new HashSet<string>(StringComparer.Ordinal);
        if (existing is not null)
        {
            foreach (var raw in existing.Replace("\r\n", "\n").Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length > 0) present.Add(t);
            }
        }

        var missing = GitignoreEntries.Where(e => !present.Contains(e)).ToList();
        if (missing.Count == 0)
        {
            return false;
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(existing))
        {
            sb.Append(existing.TrimEnd('\r', '\n'));
            sb.Append('\n').Append('\n');
        }
        sb.Append(GitignoreHeader).Append('\n');
        foreach (var entry in missing)
        {
            sb.Append(entry).Append('\n');
        }
        File.WriteAllText(gitignorePath, sb.ToString());
        return true;
    }

    /// <summary>
    /// Seed the "read .firepit/inbox on session start" convention into the
    /// project's CLAUDE.md (appending if the file exists and lacks it, or
    /// creating a minimal CLAUDE.md if there is none). Idempotent via a marker.
    /// Returns true if CLAUDE.md was created or appended to.
    /// </summary>
    public static bool EnsureInboxConvention(string projectPath)
    {
        var claudeMdPath = Path.Combine(projectPath, "CLAUDE.md");
        var section =
            "## Firepit inbox\n\n" +
            "At the start of a session, read any pending messages in " +
            "`.firepit/inbox/*.md` — cross-project notes Firepit routes here. " +
            "Act on each, then mark it done with the `firepit_inbox_complete` " +
            "MCP tool, passing the message's filename as the `id`.\n";

        if (File.Exists(claudeMdPath))
        {
            var content = File.ReadAllText(claudeMdPath);
            if (content.Contains(InboxConventionMarker, StringComparison.Ordinal))
            {
                return false;
            }
            var sb = new StringBuilder(content.TrimEnd('\r', '\n'));
            sb.Append('\n').Append('\n').Append(section);
            File.WriteAllText(claudeMdPath, sb.ToString());
            return true;
        }

        var title = Path.GetFileName(projectPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(title)) title = "Project";
        File.WriteAllText(claudeMdPath, $"# {title}\n\n{section}");
        return true;
    }

    /// <summary>
    /// Find blanket <c>.firepit/</c> / <c>.claude/</c> ignore lines — a bare
    /// directory ignore with no negation swallows the shared config
    /// (config.json, mcp.json, commands, agents). Returns the offending lines.
    /// </summary>
    public static IReadOnlyList<string> DetectBlanketIgnores(string projectPath)
    {
        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        if (!File.Exists(gitignorePath))
        {
            return Array.Empty<string>();
        }

        var found = new List<string>();
        foreach (var raw in File.ReadLines(gitignorePath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            if (Array.IndexOf(BlanketIgnorePatterns, line) >= 0)
            {
                found.Add(line);
            }
        }
        return found;
    }

    /// <summary>
    /// Disable blanket <c>.firepit/</c> / <c>.claude/</c> ignores (comment them
    /// out) and ensure the granular block is present, so the shared config
    /// becomes trackable again. Returns true if any blanket line was disabled.
    /// </summary>
    public static bool MigrateBlanketIgnores(string projectPath)
    {
        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        if (!File.Exists(gitignorePath))
        {
            EnsureGitignoreBlock(projectPath);
            return false;
        }

        var lines = File.ReadAllText(gitignorePath).Replace("\r\n", "\n").Split('\n').ToList();
        var changed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (Array.IndexOf(BlanketIgnorePatterns, lines[i].Trim()) >= 0)
            {
                lines[i] = "# " + lines[i] + "   # Firepit: blanket ignore disabled — it hid the shared config";
                changed = true;
            }
        }
        if (changed)
        {
            File.WriteAllText(gitignorePath, string.Join('\n', lines));
        }
        EnsureGitignoreBlock(projectPath);
        return changed;
    }
}
