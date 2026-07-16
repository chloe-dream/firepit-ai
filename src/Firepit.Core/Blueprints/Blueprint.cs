using System.Text.Json.Serialization;

namespace Firepit.Core.Blueprints;

/// <summary>
/// A loaded blueprint: the declarative "this must exist" manifest for a
/// project (ROADMAP M9). Blueprints are data, not code — a folder under
/// <c>{metaProject}/blueprints/{name}/</c> holding a <c>blueprint.json</c>
/// manifest plus a <c>files/</c> tree that is copied file-by-file where
/// missing. The single operation over a blueprint is the idempotent apply:
/// missing → created, present → untouched. No template DSL, no migrations.
/// </summary>
public sealed record Blueprint(
    string Name,
    string Description,
    string SourceDir,
    bool EnsureProjectConfig,
    IReadOnlyList<string> GitignoreLines,
    IReadOnlyList<BlueprintClaudeMdSection> ClaudeMdSections,
    IReadOnlyList<BlueprintFile> Files);

/// <summary>A CLAUDE.md section that must exist. <see cref="Marker"/> is the
/// idempotency probe: if CLAUDE.md already contains it, the section counts
/// as present and is never rewritten.</summary>
public sealed record BlueprintClaudeMdSection(string Marker, string Content);

/// <summary>One file the blueprint guarantees: <see cref="RelativePath"/>
/// inside the target project (forward slashes), copied from
/// <see cref="SourcePath"/> only when the target doesn't exist.</summary>
public sealed record BlueprintFile(string RelativePath, string SourcePath);

/// <summary>On-disk shape of <c>blueprint.json</c>.</summary>
public sealed record BlueprintManifest(
    int Version,
    string? Description,
    bool EnsureProjectConfig,
    IReadOnlyList<string>? Gitignore,
    IReadOnlyList<BlueprintManifestSection>? ClaudeMd);

public sealed record BlueprintManifestSection(string Marker, string Content);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(BlueprintManifest))]
[JsonSerializable(typeof(BlueprintManifestSection))]
internal partial class BlueprintJsonContext : JsonSerializerContext
{
}
