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
    string? ActiveTabProjectName = null)
{
    public const int CurrentVersion = 1;

    public static readonly AppState Empty = new(CurrentVersion, []);
}

public sealed record TabState(
    string ProjectName,
    bool LastSessionResumable);
