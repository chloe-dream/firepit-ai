using System.IO;
using Firepit.Core.ProjectConfig;

namespace Firepit.Core.Tests.ProjectConfig;

public class ProjectConfigScaffoldTests : IDisposable
{
    private readonly string _projectPath;

    public ProjectConfigScaffoldTests()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), "firepit-scaffold-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectPath, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void EnsureScaffold_CreatesFileWhenMissing()
    {
        var path = ProjectConfigScaffold.EnsureScaffold(_projectPath, "demo");

        Assert.True(File.Exists(path));
        Assert.Equal(Path.Combine(_projectPath, ".firepit", "config.json"), path);
    }

    [Fact]
    public void EnsureScaffold_DoesNotOverwriteExistingFile()
    {
        var configDir = Path.Combine(_projectPath, ".firepit");
        Directory.CreateDirectory(configDir);
        var existing = Path.Combine(configDir, "config.json");
        File.WriteAllText(existing, "MY CUSTOM CONTENT");

        ProjectConfigScaffold.EnsureScaffold(_projectPath, "demo");

        Assert.Equal("MY CUSTOM CONTENT", File.ReadAllText(existing));
    }

    [Fact]
    public void EnsureScaffold_WrittenFileIsParseableByProjectConfigStore()
    {
        var path = ProjectConfigScaffold.EnsureScaffold(_projectPath, "demo");

        var store = new JsonProjectConfigStore();
        var loaded = store.Load(_projectPath);

        Assert.NotNull(loaded);
        Assert.Equal("demo", loaded!.Id);
        Assert.Equal(1, loaded.Version);
    }

    [Fact]
    public void BuildScaffold_EmbedsTheGivenProjectId()
    {
        var content = ProjectConfigScaffold.BuildScaffold("my-cool-project");
        Assert.Contains("\"id\": \"my-cool-project\"", content);
    }
}
