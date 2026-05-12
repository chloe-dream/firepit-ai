using System.IO;

namespace Firepit.Core.Settings;

public sealed record FirepitSettings(
    string ProjectsRoot,
    string DefaultAgent,
    string Theme,
    TabSettings Tabs,
    ShellsSettings Shells,
    TerminalThemeSettings? Terminal = null,
    UiSettings? Ui = null,
    IReadOnlyDictionary<string, McpServerSettings>? McpServers = null,
    IReadOnlyList<QuickLinkSettings>? QuickLinks = null,
    IReadOnlyList<ProjectSettings>? Projects = null,
    PlatformSettings? Platform = null)
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
        Ui: UiSettings.Defaults,
        Platform: PlatformSettings.Defaults);
    // QuickLinks are intentionally empty by default — they're user-specific
    // (GitHub org/user, internal tools, personal dashboards). The per-project
    // .firepit/config.json scaffold shows commented examples of how to add
    // them. settings.json globals also work; configure via Settings dialog.
}

/// <summary>
/// V0.5.0+ flags grouped under the "platform" key. Kept separate from
/// existing nested records so the legacy schema stays clean.
/// </summary>
public sealed record PlatformSettings(
    bool MetaProjectPromptShown = false,
    bool InboxBadgesEnabled = true)
{
    public static readonly PlatformSettings Defaults = new();
}

/// <summary>
/// One knob — UI chrome and the embedded terminal font scale together.
/// Range clamp [10, 22] is enforced in <see cref="ResolvedFontSize"/>.
/// </summary>
public sealed record UiSettings(int FontSize)
{
    public const int MinFontSize = 10;
    public const int MaxFontSize = 22;
    public const int DefaultFontSize = 12;

    public static readonly UiSettings Defaults = new(DefaultFontSize);

    // Computed property — must NOT be serialized. Without [JsonIgnore] the
    // source-gen serializer treats it as a public read-only property worth
    // writing, and settings.json ends up with a redundant "resolvedFontSize"
    // field that confuses round-tripping.
    [System.Text.Json.Serialization.JsonIgnore]
    public int ResolvedFontSize => Math.Clamp(FontSize, MinFontSize, MaxFontSize);
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

public sealed record TabSettings(
    bool PersistAcrossRestarts,
    int ActivityIdleThresholdMs,
    // Phase 2: when true, FileSystemWatcher re-applies <project>/.firepit/config.json
    // edits live. Off by default in v0.5.0 — the explicit firepit_reload MCP tool is
    // the canonical reload path. Field-test the swap-file behaviour first, flip default
    // in a follow-up release.
    bool AutoReloadOnConfigChange = false)
{
    public static readonly TabSettings Defaults = new(
        PersistAcrossRestarts: true,
        ActivityIdleThresholdMs: 1500,
        AutoReloadOnConfigChange: false);
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
