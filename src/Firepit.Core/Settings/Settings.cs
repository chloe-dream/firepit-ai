using System.IO;

namespace Firepit.Core.Settings;

public sealed record FirepitSettings(
    string ProjectsRoot,
    string DefaultAgent,
    string Theme,
    TabSettings Tabs,
    ShellsSettings Shells,
    TerminalThemeSettings? Terminal = null,
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
        Terminal: TerminalThemeSettings.Defaults,
        QuickLinks:
        [
            new QuickLinkSettings("GitHub",   "https://github.com/chloe-dream/{projectName}", QuickLinkTargetSetting.External, Icon: "github"),
            new QuickLinkSettings("Fishbowl", "https://localhost:7180/p/{projectName}",       QuickLinkTargetSetting.External, Icon: "fishbowl"),
        ]);
}

/// <summary>
/// Hand-edit terminal palette tokens in settings.json. Null fields fall back
/// to the brand-warm defaults. Hex strings — short or long form, both work.
/// </summary>
public sealed record TerminalThemeSettings(
    string? Background = null,
    string? Foreground = null,
    string? Cursor = null,
    string? SelectionBackground = null,
    string? SelectionForeground = null,
    string? SelectionInactiveBackground = null)
{
    public static readonly TerminalThemeSettings Defaults = new(
        Background:                  "#1A1612",
        Foreground:                  "#E8E2D8",
        Cursor:                      "#E8E2D8",
        SelectionBackground:         "#7A6855",
        SelectionForeground:         "#15110D",
        SelectionInactiveBackground: "#3A3026");

    public TerminalThemeSettings Resolved() => new(
        Background:                  Background                  ?? Defaults.Background,
        Foreground:                  Foreground                  ?? Defaults.Foreground,
        Cursor:                      Cursor                      ?? Defaults.Cursor,
        SelectionBackground:         SelectionBackground         ?? Defaults.SelectionBackground,
        SelectionForeground:         SelectionForeground         ?? Defaults.SelectionForeground,
        SelectionInactiveBackground: SelectionInactiveBackground ?? Defaults.SelectionInactiveBackground);
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
