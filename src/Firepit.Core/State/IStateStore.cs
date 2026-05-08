namespace Firepit.Core.State;

public interface IStateStore
{
    string StatePath { get; }

    AppState Load();

    void Save(AppState state);
}
