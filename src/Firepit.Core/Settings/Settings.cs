using System.IO;

namespace Firepit.Core.Settings;

public sealed record FirepitSettings(
    string ProjectsRoot,
    string DefaultAgent,
    string Theme,
    TabSettings Tabs,
    ShellsSettings Shells,
    IReadOnlyDictionary<string, McpServerSettings>? McpServers = null,
    IReadOnlyList<QuickLinkSettings>? QuickLinks = null,
    IReadOnlyList<ProjectSettings>? Projects = null)
{
    public static readonly FirepitSettings Defaults = new(
        ProjectsRoot: Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "SynologyDrive", "PROJECTS"),
        DefaultAgent: "claude",
        Theme: "dark",
        Tabs: TabSettings.Defaults,
        Shells: ShellsSettings.Defaults,
        QuickLinks:
        [
            new QuickLinkSettings("GitHub",   "https://github.com/chloe-dream/{projectName}", QuickLinkTargetSetting.External),
            new QuickLinkSettings("Fishbowl", "https://localhost:7180/p/{projectName}",       QuickLinkTargetSetting.External),
        ]);
}

public sealed record TabSettings(bool PersistAcrossRestarts, int ActivityIdleThresholdMs)
{
    public static readonly TabSettings Defaults = new(PersistAcrossRestarts: true, ActivityIdleThresholdMs: 1500);
}

public sealed record ShellsSettings(string Preferred)
{
    public static readonly ShellsSettings Defaults = new(Preferred: "wt");
}

public sealed record ProjectSettings(
    string Name,
    string Path,
    string? AgentCommand = null,
    IReadOnlyList<string>? AgentArgs = null,
    IReadOnlyList<string>? McpServers = null,
    IReadOnlyDictionary<string, McpOverrideSettings>? McpOverrides = null,
    IReadOnlyList<QuickLinkSettings>? QuickLinks = null);

public sealed record QuickLinkSettings(
    string Name,
    string Url,
    QuickLinkTargetSetting Target = QuickLinkTargetSetting.External,
    string? Icon = null,
    bool? Disabled = null);

public enum QuickLinkTargetSetting
{
    External,
    SubTab,
}

public sealed record McpServerSettings(
    string DisplayName,
    string Transport,
    string? Description = null,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    string? Url = null,
    IReadOnlyDictionary<string, string?>? Headers = null);

public sealed record McpOverrideSettings(
    IReadOnlyList<string>? Args = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    IReadOnlyDictionary<string, string?>? Headers = null);
