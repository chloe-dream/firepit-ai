using System.IO;
using Firepit.Core.Agents;
using Firepit.Core.Projects;

namespace Firepit.Core.Tests;

public class ProjectDiscoveryTests : IDisposable
{
    private readonly string _root;

    public ProjectDiscoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "firepit-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignored */ }
    }

    [Fact]
    public void Scan_FindsClaudeMdMarker()
    {
        var dir = Path.Combine(_root, "lighthouse");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), "# project");

        var discovery = new ProjectDiscovery([new FakeAdapter("a", ["CLAUDE.md", ".claude"])]);
        var result = discovery.Scan(_root);

        var p = Assert.Single(result);
        Assert.Equal("lighthouse", p.Name);
        Assert.Equal("a", p.AdapterId);
    }

    [Fact]
    public void Scan_FindsClaudeDirectoryMarker()
    {
        var dir = Path.Combine(_root, "tinderbox");
        Directory.CreateDirectory(Path.Combine(dir, ".claude"));

        var discovery = new ProjectDiscovery([new FakeAdapter("a", ["CLAUDE.md", ".claude"])]);
        var result = discovery.Scan(_root);

        Assert.Single(result);
        Assert.Equal("tinderbox", result[0].Name);
    }

    [Fact]
    public void Scan_SkipsDirectoriesWithoutMarker()
    {
        Directory.CreateDirectory(Path.Combine(_root, "no-claude"));
        var discovery = new ProjectDiscovery([new FakeAdapter("a", ["CLAUDE.md"])]);
        Assert.Empty(discovery.Scan(_root));
    }

    [Fact]
    public void Scan_ManualEntriesTakePrecedenceAndDeduplicate()
    {
        var dir = Path.Combine(_root, "lighthouse");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), "# project");

        var discovery = new ProjectDiscovery([new FakeAdapter("a", ["CLAUDE.md"])]);
        var manual = new[]
        {
            new Project("lighthouse-renamed", dir, "a"),
        };

        var result = discovery.Scan(_root, manual);

        Assert.Single(result);
        Assert.Equal("lighthouse-renamed", result[0].Name);
    }

    [Fact]
    public void Scan_MissingRoot_ReturnsManualOnly()
    {
        var discovery = new ProjectDiscovery([new FakeAdapter("a", ["CLAUDE.md"])]);
        var manual = new[] { new Project("manual", @"C:\does\not\exist", "a") };

        var result = discovery.Scan(@"C:\definitely\not\here", manual);

        Assert.Single(result);
        Assert.Equal("manual", result[0].Name);
    }

    private sealed class FakeAdapter : IAgentAdapter
    {
        public FakeAdapter(string id, IReadOnlyList<string> markers)
        {
            Id = id;
            ProjectMarkers = markers;
        }
        public string Id { get; }
        public string DisplayName => Id;
        public IReadOnlyList<string> ProjectMarkers { get; }

        public AgentLaunchSpec BuildLaunchSpec(ProjectContext context, AgentLaunchOptions options)
            => new("claude", [], context.Path);
    }
}
