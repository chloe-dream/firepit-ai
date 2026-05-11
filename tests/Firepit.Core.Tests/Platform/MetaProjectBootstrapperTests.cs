using System.IO;
using Firepit.Core.Platform;

namespace Firepit.Core.Tests.Platform;

public class MetaProjectBootstrapperTests : IDisposable
{
    private readonly string _projectsRoot;
    private readonly MetaProjectBootstrapper _bootstrapper = new();

    public MetaProjectBootstrapperTests()
    {
        _projectsRoot = Path.Combine(Path.GetTempPath(), "firepit-meta-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectsRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectsRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Exists_FalseBeforeBootstrap()
    {
        Assert.False(_bootstrapper.Exists(_projectsRoot));
    }

    [Fact]
    public void Bootstrap_CreatesExpectedFiles()
    {
        var written = _bootstrapper.Bootstrap(_projectsRoot);
        Assert.True(_bootstrapper.Exists(_projectsRoot));

        var meta = _bootstrapper.GetMetaProjectPath(_projectsRoot);
        Assert.True(File.Exists(Path.Combine(meta, "CLAUDE.md")));
        Assert.True(File.Exists(Path.Combine(meta, "README.md")));
        Assert.True(File.Exists(Path.Combine(meta, ".gitignore")));
        Assert.True(File.Exists(Path.Combine(meta, ".claude", "settings.json")));
        Assert.True(File.Exists(Path.Combine(meta, ".firepit", "config.json")));
        Assert.True(File.Exists(Path.Combine(meta, ".firepit", "inbox", ".gitkeep")));
        Assert.True(File.Exists(Path.Combine(meta, "notes", "README.md")));

        Assert.False(Directory.Exists(Path.Combine(meta, "inbox")),
            "root-level inbox/ was retired in favour of .firepit/inbox/ — bootstrapper must not recreate it.");

        Assert.Equal(7, written.Count);
    }

    [Fact]
    public void Bootstrap_CleansUpEmptyLegacyRootInbox()
    {
        var meta = _bootstrapper.GetMetaProjectPath(_projectsRoot);
        var legacyInbox = Path.Combine(meta, "inbox");
        Directory.CreateDirectory(legacyInbox);
        File.WriteAllText(Path.Combine(legacyInbox, ".gitkeep"), "");

        _bootstrapper.Bootstrap(_projectsRoot);

        Assert.False(Directory.Exists(legacyInbox),
            "an empty legacy root inbox/ should be removed on bootstrap to clear post-upgrade drift.");
    }

    [Fact]
    public void Bootstrap_PreservesNonEmptyLegacyRootInbox()
    {
        var meta = _bootstrapper.GetMetaProjectPath(_projectsRoot);
        var legacyInbox = Path.Combine(meta, "inbox");
        Directory.CreateDirectory(legacyInbox);
        File.WriteAllText(Path.Combine(legacyInbox, "user-note.md"), "important");

        _bootstrapper.Bootstrap(_projectsRoot);

        Assert.True(Directory.Exists(legacyInbox),
            "a legacy root inbox/ with user-curated content must not be deleted.");
        Assert.True(File.Exists(Path.Combine(legacyInbox, "user-note.md")));
    }

    [Fact]
    public void Bootstrap_RegistersFirepitMcpInClaudeSettings()
    {
        _bootstrapper.Bootstrap(_projectsRoot);
        var settings = File.ReadAllText(
            Path.Combine(_bootstrapper.GetMetaProjectPath(_projectsRoot), ".claude", "settings.json"));
        Assert.Contains("\"firepit\"", settings);
        Assert.Contains("\"command\": \"firepit-mcp\"", settings);
    }

    [Fact]
    public void Bootstrap_FirepitConfigActivatesFirepitMcp()
    {
        _bootstrapper.Bootstrap(_projectsRoot);
        var config = File.ReadAllText(
            Path.Combine(_bootstrapper.GetMetaProjectPath(_projectsRoot), ".firepit", "config.json"));
        Assert.Contains("\"id\": \"firepit\"", config);
    }

    [Fact]
    public void Bootstrap_DoesNotClobberExistingFiles()
    {
        var meta = _bootstrapper.GetMetaProjectPath(_projectsRoot);
        Directory.CreateDirectory(meta);
        File.WriteAllText(Path.Combine(meta, "CLAUDE.md"), "MY CUSTOM CONTENT");

        var written = _bootstrapper.Bootstrap(_projectsRoot);

        Assert.Equal("MY CUSTOM CONTENT", File.ReadAllText(Path.Combine(meta, "CLAUDE.md")));
        // Other files still wrote
        Assert.Contains(written, p => p.EndsWith("README.md"));
        Assert.DoesNotContain(written, p => p.EndsWith("CLAUDE.md"));
    }

    [Fact]
    public void Bootstrap_IsIdempotent()
    {
        _bootstrapper.Bootstrap(_projectsRoot);
        var second = _bootstrapper.Bootstrap(_projectsRoot);
        // Second run writes nothing — every file already exists.
        Assert.Empty(second);
    }

    [Fact]
    public void GetMetaProjectPath_PointsAtDotFirepitUnderRoot()
    {
        Assert.Equal(
            Path.Combine(_projectsRoot, ".firepit"),
            _bootstrapper.GetMetaProjectPath(_projectsRoot));
    }
}
