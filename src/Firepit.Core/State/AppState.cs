namespace Firepit.Core.State;

public sealed record AppState(
    int Version,
    IReadOnlyList<TabState> Tabs,
    // True once the v0.5.0 ProjectConfigMigrator has stripped legacy
    // per-project entries from settings.json into per-project
    // .firepit/config.json files. Old state.json files load with the
    // default (false) and the migrator runs on next launch — safe because
    // it's idempotent (skips projects already migrated).
    bool ProjectConfigMigrationDone = false)
{
    public const int CurrentVersion = 1;

    public static readonly AppState Empty = new(CurrentVersion, []);
}

public sealed record TabState(
    string ProjectName,
    bool LastSessionResumable);
