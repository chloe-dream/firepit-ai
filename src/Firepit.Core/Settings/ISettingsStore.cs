namespace Firepit.Core.Settings;

public interface ISettingsStore
{
    string SettingsPath { get; }

    FirepitSettings Load();

    void Save(FirepitSettings settings);
}
