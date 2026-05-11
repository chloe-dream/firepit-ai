using System.IO;

namespace Firepit.Core.Platform;

/// <summary>
/// Seeds the <c>.firepit</c> meta-project at the projects root. Idempotent
/// per file: pre-existing files are never overwritten (the user may have
/// curated them). Returns the list of files actually written.
/// </summary>
public sealed class MetaProjectBootstrapper
{
    public const string MetaProjectName = ".firepit";

    public string GetMetaProjectPath(string projectsRoot) =>
        Path.Combine(projectsRoot, MetaProjectName);

    public bool Exists(string projectsRoot) =>
        Directory.Exists(GetMetaProjectPath(projectsRoot));

    public IReadOnlyList<string> Bootstrap(string projectsRoot)
    {
        ArgumentNullException.ThrowIfNull(projectsRoot);
        var root = GetMetaProjectPath(projectsRoot);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ".claude"));
        Directory.CreateDirectory(Path.Combine(root, ".firepit"));
        Directory.CreateDirectory(Path.Combine(root, ".firepit", "inbox"));
        Directory.CreateDirectory(Path.Combine(root, "notes"));

        var written = new List<string>();
        WriteIfMissing(written, Path.Combine(root, "CLAUDE.md"),                 MetaProjectTemplates.ClaudeMd);
        WriteIfMissing(written, Path.Combine(root, "README.md"),                 MetaProjectTemplates.ReadmeMd);
        WriteIfMissing(written, Path.Combine(root, ".claude", "settings.json"),  MetaProjectTemplates.ClaudeSettingsJson);
        WriteIfMissing(written, Path.Combine(root, ".firepit", "config.json"),   MetaProjectTemplates.FirepitConfigJson);
        WriteIfMissing(written, Path.Combine(root, ".firepit", "inbox", ".gitkeep"), "");
        WriteIfMissing(written, Path.Combine(root, "notes", "README.md"),        MetaProjectTemplates.NotesReadmeMd);
        WriteIfMissing(written, Path.Combine(root, ".gitignore"),                MetaProjectTemplates.GitIgnore);

        CleanupDeadRootInbox(root);
        return written;
    }

    // v0.5.0 and earlier created a root-level `inbox/` that no code ever
    // wrote to — actual inbox traffic goes to `.firepit/inbox/`. Remove the
    // leftover empty directory on existing meta-projects. Only touches
    // directories that are empty except for the bootstrapped .gitkeep, so
    // user-curated content is safe.
    private static void CleanupDeadRootInbox(string root)
    {
        var deadInbox = Path.Combine(root, "inbox");
        if (!Directory.Exists(deadInbox)) return;

        var entries = Directory.GetFileSystemEntries(deadInbox);
        if (entries.Length == 0)
        {
            Directory.Delete(deadInbox);
            return;
        }

        if (entries.Length == 1 &&
            string.Equals(Path.GetFileName(entries[0]), ".gitkeep", StringComparison.OrdinalIgnoreCase) &&
            new FileInfo(entries[0]).Length == 0)
        {
            File.Delete(entries[0]);
            Directory.Delete(deadInbox);
        }
    }

    private static void WriteIfMissing(List<string> written, string path, string content)
    {
        if (File.Exists(path)) return;
        File.WriteAllText(path, content);
        written.Add(path);
    }
}
