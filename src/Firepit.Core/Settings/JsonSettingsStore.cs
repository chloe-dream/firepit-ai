using System.IO;
using System.Text.Json;

namespace Firepit.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    public JsonSettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? DefaultPath();
    }

    public string SettingsPath { get; }

    public FirepitSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return FirepitSettings.Defaults;
        }

        try
        {
            using var stream = File.OpenRead(SettingsPath);
            var loaded = JsonSerializer.Deserialize(stream, FirepitJsonContext.Default.FirepitSettings);
            return loaded ?? FirepitSettings.Defaults;
        }
        catch (JsonException)
        {
            return FirepitSettings.Defaults;
        }
        catch (IOException)
        {
            return FirepitSettings.Defaults;
        }
    }

    public void Save(FirepitSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        using var stream = File.Create(SettingsPath);
        JsonSerializer.Serialize(stream, settings, FirepitJsonContext.Default.FirepitSettings);
    }

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Firepit",
        "settings.json");
}
