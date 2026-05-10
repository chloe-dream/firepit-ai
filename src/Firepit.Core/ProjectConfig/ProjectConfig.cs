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
    ProjectSessionConfig? Session = null,
    IReadOnlyList<ProjectCommand>? Commands = null);

/// <summary>
/// User-defined toolbar buttons. Three variants:
/// <list type="bullet">
///   <item><c>type=shell</c>: spawn a process with <c>Command</c> + <c>Args</c> in the project dir</item>
///   <item><c>type=claude-prompt</c>: inject <c>Prompt</c> into the active session's PTY as if the user typed it</item>
///   <item><c>type=url</c>: open <c>Url</c> in the default browser (placeholder substitution applies)</item>
/// </list>
/// </summary>
public sealed record ProjectCommand(
    string Name,
    ProjectCommandType Type,
    string? Icon = null,
    // Type-specific fields. Only the ones relevant to Type matter; others ignored.
    string? Command = null,                // shell
    IReadOnlyList<string>? Args = null,    // shell
    string? Prompt = null,                 // claude-prompt
    string? Url = null,                    // url
    bool? Disabled = null);

public enum ProjectCommandType
{
    Shell,
    ClaudePrompt,
    Url,
}

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
