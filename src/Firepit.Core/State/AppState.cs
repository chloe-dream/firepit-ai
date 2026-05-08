namespace Firepit.Core.State;

public sealed record AppState(
    int Version,
    IReadOnlyList<TabState> Tabs)
{
    public const int CurrentVersion = 1;

    public static readonly AppState Empty = new(CurrentVersion, []);
}

public sealed record TabState(
    string ProjectName,
    bool LastSessionResumable);
