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
        Directory.CreateDirectory(Path.Combine(root, "notes"));
        Directory.CreateDirectory(Path.Combine(root, "inbox"));

        var written = new List<string>();
        WriteIfMissing(written, Path.Combine(root, "CLAUDE.md"),                 MetaProjectTemplates.ClaudeMd);
        WriteIfMissing(written, Path.Combine(root, "README.md"),                 MetaProjectTemplates.ReadmeMd);
        WriteIfMissing(written, Path.Combine(root, ".claude", "settings.json"),  MetaProjectTemplates.ClaudeSettingsJson);
        WriteIfMissing(written, Path.Combine(root, ".firepit", "config.json"),   MetaProjectTemplates.FirepitConfigJson);
        WriteIfMissing(written, Path.Combine(root, "notes", "README.md"),        MetaProjectTemplates.NotesReadmeMd);
        WriteIfMissing(written, Path.Combine(root, "inbox", ".gitkeep"),         "");
        WriteIfMissing(written, Path.Combine(root, ".gitignore"),                MetaProjectTemplates.GitIgnore);
        return written;
    }

    private static void WriteIfMissing(List<string> written, string path, string content)
    {
        if (File.Exists(path)) return;
        File.WriteAllText(path, content);
        written.Add(path);
    }
}
