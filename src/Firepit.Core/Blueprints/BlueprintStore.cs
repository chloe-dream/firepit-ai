using System.IO;
using System.Text.Json;
using Firepit.Core.ProjectConfig;

namespace Firepit.Core.Blueprints;

/// <summary>
/// Loads blueprints from <c>{metaProject}/blueprints/</c>. Blueprints live in
/// the <c>.firepit</c> meta project on purpose: they are user-editable data,
/// versioned with the meta project's own git repo, and the helper agent can
/// grow the catalogue without a Firepit release. <see cref="EnsureDefaults"/>
/// seeds the built-in "firepit" blueprint on first use; after that, disk is
/// the single source of truth.
/// </summary>
public sealed class BlueprintStore
{
    public const string BlueprintsDirName = "blueprints";
    public const string ManifestFileName = "blueprint.json";
    public const string FilesDirName = "files";

    private readonly string _blueprintsDir;

    public BlueprintStore(string projectsRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectsRoot);
        MetaProjectPath = Path.Combine(Path.GetFullPath(projectsRoot), ".firepit");
        _blueprintsDir = Path.Combine(MetaProjectPath, BlueprintsDirName);
    }

    public string MetaProjectPath { get; }

    public bool MetaProjectExists => Directory.Exists(MetaProjectPath);

    /// <summary>
    /// Seed the built-in "firepit" blueprint if the meta project exists and
    /// doesn't carry one yet. Never overwrites — once seeded, the on-disk
    /// copy is the user's to edit. Returns true when something was written.
    /// </summary>
    public bool EnsureDefaults()
    {
        if (!MetaProjectExists)
        {
            return false;
        }

        var dir = Path.Combine(_blueprintsDir, FirepitBlueprintDefaults.DefaultBlueprintName);
        if (File.Exists(Path.Combine(dir, ManifestFileName)))
        {
            return false;
        }

        var manifest = new BlueprintManifest(
            Version: 1,
            Description: FirepitBlueprintDefaults.Description,
            EnsureProjectConfig: true,
            Gitignore: ProjectScaffolding.GitignoreEntries,
            ClaudeMd:
            [
                new BlueprintManifestSection(
                    FirepitBlueprintDefaults.InboxSectionMarker,
                    FirepitBlueprintDefaults.InboxSection),
                new BlueprintManifestSection(
                    FirepitBlueprintDefaults.KnowledgeSectionMarker,
                    FirepitBlueprintDefaults.KnowledgeSection),
                new BlueprintManifestSection(
                    FirepitBlueprintDefaults.PinnedSectionMarker,
                    FirepitBlueprintDefaults.PinnedSection),
            ]);

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, ManifestFileName),
            JsonSerializer.Serialize(manifest, BlueprintJsonContext.Default.BlueprintManifest));

        SeedFile(dir, FirepitBlueprintDefaults.KnowledgeReadmePath, FirepitBlueprintDefaults.KnowledgeReadme);
        // Placeholder digest: apply copies it into projects that don't have
        // one yet, so the CLAUDE.md @import resolves immediately; Firepit's
        // knowledge service overwrites it with real pinned content.
        SeedFile(dir, FirepitBlueprintDefaults.PinnedDigestPath, FirepitBlueprintDefaults.PinnedDigestSeed);
        return true;
    }

    private static void SeedFile(string blueprintDir, string relativePath, string content)
    {
        var target = Path.Combine(
            blueprintDir, FilesDirName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content);
    }

    public IReadOnlyList<Blueprint> LoadAll()
    {
        if (!Directory.Exists(_blueprintsDir))
        {
            return [];
        }

        var result = new List<Blueprint>();
        foreach (var dir in Directory.EnumerateDirectories(_blueprintsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var bp = TryLoadDir(dir);
            if (bp is not null)
            {
                result.Add(bp);
            }
        }

        return result;
    }

    public Blueprint? TryLoad(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '.']) >= 0)
        {
            // Blueprint names are bare folder names — anything path-like is
            // either a typo or a traversal attempt from MCP input.
            return null;
        }

        return TryLoadDir(Path.Combine(_blueprintsDir, name));
    }

    private static Blueprint? TryLoadDir(string dir)
    {
        var manifestPath = Path.Combine(dir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        BlueprintManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath), BlueprintJsonContext.Default.BlueprintManifest);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is null)
        {
            return null;
        }

        var files = new List<BlueprintFile>();
        var filesRoot = Path.Combine(dir, FilesDirName);
        if (Directory.Exists(filesRoot))
        {
            foreach (var source in Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(filesRoot, source).Replace('\\', '/');
                files.Add(new BlueprintFile(rel, source));
            }
        }

        return new Blueprint(
            Name: Path.GetFileName(dir.TrimEnd('\\', '/')),
            Description: manifest.Description ?? string.Empty,
            SourceDir: dir,
            EnsureProjectConfig: manifest.EnsureProjectConfig,
            GitignoreLines: manifest.Gitignore ?? [],
            ClaudeMdSections: manifest.ClaudeMd?
                .Select(s => new BlueprintClaudeMdSection(s.Marker, s.Content))
                .ToArray() ?? [],
            Files: files);
    }
}
