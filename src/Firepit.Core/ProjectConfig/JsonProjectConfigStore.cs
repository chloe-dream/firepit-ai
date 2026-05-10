using System.IO;
using System.Text.Json;

namespace Firepit.Core.ProjectConfig;

public sealed class JsonProjectConfigStore : IProjectConfigStore
{
    public const string DirectoryName = ".firepit";
    public const string FileName      = "config.json";

    public ProjectConfig? Load(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        var path = ResolvePath(projectPath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, ProjectConfigJsonContext.Default.ProjectConfig);
        }
        catch (JsonException)
        {
            // Malformed file — surface as "no config" rather than crashing the
            // session. Phase 2's watcher will log + skip on hot-reload failures.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save(string projectPath, ProjectConfig config)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(config);

        var path = ResolvePath(projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, config, ProjectConfigJsonContext.Default.ProjectConfig);
    }

    public static string ResolvePath(string projectPath) =>
        Path.Combine(projectPath, DirectoryName, FileName);
}
