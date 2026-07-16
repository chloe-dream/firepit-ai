using Firepit.Core.Blueprints;
using Firepit.Core.ProjectConfig;

namespace Firepit.Core.Tests.Blueprints;

public sealed class BlueprintStoreTests : IDisposable
{
    private readonly string _root;

    public BlueprintStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "firepit-blueprint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, ".firepit"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void EnsureDefaults_SeedsFirepitBlueprintOnce()
    {
        var store = new BlueprintStore(_root);

        Assert.True(store.EnsureDefaults());
        Assert.False(store.EnsureDefaults());

        var dir = Path.Combine(_root, ".firepit", "blueprints", "firepit");
        Assert.True(File.Exists(Path.Combine(dir, "blueprint.json")));
        Assert.True(File.Exists(Path.Combine(dir, "files", ".firepit", "knowledge", "README.md")));
    }

    [Fact]
    public void EnsureDefaults_WithoutMetaProject_DoesNothing()
    {
        Directory.Delete(Path.Combine(_root, ".firepit"), recursive: true);
        var store = new BlueprintStore(_root);

        Assert.False(store.EnsureDefaults());
        Assert.False(Directory.Exists(Path.Combine(_root, ".firepit")));
    }

    [Fact]
    public void LoadAll_ReadsTheSeededBlueprint()
    {
        var store = new BlueprintStore(_root);
        store.EnsureDefaults();

        var blueprint = Assert.Single(store.LoadAll());

        Assert.Equal("firepit", blueprint.Name);
        Assert.True(blueprint.EnsureProjectConfig);
        Assert.Equal(ProjectScaffolding.GitignoreEntries, blueprint.GitignoreLines);
        Assert.Equal(2, blueprint.ClaudeMdSections.Count);
        var file = Assert.Single(blueprint.Files);
        Assert.Equal(".firepit/knowledge/README.md", file.RelativePath);
    }

    [Fact]
    public void EnsureDefaults_NeverOverwritesAnEditedManifest()
    {
        var store = new BlueprintStore(_root);
        store.EnsureDefaults();
        var manifestPath = Path.Combine(_root, ".firepit", "blueprints", "firepit", "blueprint.json");
        var edited = """{ "version": 1, "description": "my edit", "ensureProjectConfig": false }""";
        File.WriteAllText(manifestPath, edited);

        store.EnsureDefaults();

        Assert.Equal(edited, File.ReadAllText(manifestPath));
        var blueprint = store.TryLoad("firepit");
        Assert.NotNull(blueprint);
        Assert.Equal("my edit", blueprint.Description);
        Assert.False(blueprint.EnsureProjectConfig);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("name.with.dots")]
    [InlineData("")]
    public void TryLoad_RejectsPathLikeNames(string name)
    {
        var store = new BlueprintStore(_root);
        store.EnsureDefaults();

        Assert.Null(store.TryLoad(name));
    }
}
