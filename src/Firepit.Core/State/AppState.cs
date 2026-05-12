namespace Firepit.Core.State;

public sealed record AppState(
    int Version,
    IReadOnlyList<TabState> Tabs,
    // True once the v0.5.0 ProjectConfigMigrator has stripped legacy
    // per-project entries from settings.json into per-project
    // .firepit/config.json files. Old state.json files load with the
    // default (false) and the migrator runs on next launch — safe because
    // it's idempotent (skips projects already migrated).
    bool ProjectConfigMigrationDone = false,
    // Name of the tab that was active when the app last closed. On restore,
    // this tab gets focus and is the only one whose session starts eagerly;
    // others stay cold until the user clicks them. Null = legacy/no
    // preference, fall back to last in the Tabs list (pre-v0.5.3 behaviour).
    string? ActiveTabProjectName = null,
    // Window position and size from the previous session. Null on first
    // launch and on legacy state.json files; MainWindow falls back to the
    // XAML defaults (CenterScreen, 1180x700) in that case.
    WindowPlacement? Window = null)
{
    public const int CurrentVersion = 1;

    public static readonly AppState Empty = new(CurrentVersion, []);
}

public sealed record TabState(
    string ProjectName,
    bool LastSessionResumable);

/// <summary>
/// Persisted window bounds. <see cref="Left"/> and <see cref="Top"/> are in
/// virtual-screen device-independent pixels. When the user closed the app
/// with the window maximized, <see cref="IsMaximized"/> is true and the
/// L/T/W/H values come from <c>Window.RestoreBounds</c> so the restore-down
/// rect is preserved.
/// </summary>
public sealed record WindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);
