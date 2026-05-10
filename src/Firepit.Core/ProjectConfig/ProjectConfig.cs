using Firepit.Core.Settings;

namespace Firepit.Core.ProjectConfig;

/// <summary>
/// Per-project Firepit config. Lives at <c>&lt;projectPath&gt;/.firepit/config.json</c>.
/// Travels with the repo; the user decides per-section what to gitignore.
///
/// Resolution at session start:
///   <list type="number">
///     <item>FirepitSettings.Defaults</item>
///     <item>Global %APPDATA%\Firepit\settings.json</item>
///     <item>This file (wins on conflict for the four sections)</item>
///   </list>
/// </summary>
public sealed record ProjectConfig(
    int Version = 1,
    string? Id = null,
    IReadOnlyList<ProjectQuickLink>? QuickLinks = null,
    IReadOnlyList<ProjectMcpActivation>? McpActivations = null,
    ProjectAgentConfig? Agent = null,
    ProjectSessionConfig? Session = null);

public sealed record ProjectQuickLink(
    string Name,
    string Url,
    QuickLinkTargetSetting Target = QuickLinkTargetSetting.External,
    string? Icon = null,
    bool? Disabled = null);

public sealed record ProjectMcpActivation(
    string Id,
    IReadOnlyList<string>? ArgOverrides = null,
    IReadOnlyDictionary<string, string?>? EnvOverrides = null,
    IReadOnlyDictionary<string, string?>? HeaderOverrides = null);

public sealed record ProjectAgentConfig(
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IReadOnlyDictionary<string, string?>? EnvOverrides = null);

public sealed record ProjectSessionConfig(
    IReadOnlyDictionary<string, string?>? EnvOverrides = null);
