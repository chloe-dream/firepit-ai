using Firepit.Core.Projects;
using Firepit.Core.QuickLinks;

namespace Firepit.Core.Tests;

public class QuickLinkServiceTests
{
    private static ProjectContext Ctx(string name = "lighthouse", string path = @"D:\Code\lighthouse")
        => new(new Project(name, path, AdapterId: "claude-code"));

    [Fact]
    public void Resolve_TemplatesProjectName()
    {
        var svc = new QuickLinkService([
            new QuickLinkEntry("GitHub", "https://github.com/owner/{projectName}"),
        ]);
        var resolved = svc.ResolveForProject(Ctx());
        var link = Assert.Single(resolved);
        Assert.Equal("https://github.com/owner/lighthouse", link.Url);
        Assert.True(link.Available);
    }

    [Fact]
    public void Resolve_MarksUnknownPlaceholderUnavailable()
    {
        var svc = new QuickLinkService([
            new QuickLinkEntry("Wat", "https://example.test/{nope}"),
        ]);
        var link = Assert.Single(svc.ResolveForProject(Ctx()));
        Assert.False(link.Available);
        Assert.Contains("nope", link.UnavailableReason);
    }

    [Fact]
    public void Resolve_ProjectOverrideReplacesGlobalByName()
    {
        var svc = new QuickLinkService(
            globalDefaults: [
                new QuickLinkEntry("Fishbowl", "https://localhost:7180/p/{projectName}"),
            ],
            projectOverrides: ctx => ctx.Name == "tinderbox"
                ? [new QuickLinkEntry("Fishbowl", "https://localhost:7180/p/tinderbox-staging")]
                : []);

        var lighthouse = Assert.Single(svc.ResolveForProject(Ctx("lighthouse", @"D:\Code\lighthouse")));
        Assert.Equal("https://localhost:7180/p/lighthouse", lighthouse.Url);

        var tinderbox = Assert.Single(svc.ResolveForProject(Ctx("tinderbox", @"D:\Code\tinderbox")));
        Assert.Equal("https://localhost:7180/p/tinderbox-staging", tinderbox.Url);
    }

    [Fact]
    public void Resolve_DisabledOverrideHidesGlobal()
    {
        var svc = new QuickLinkService(
            globalDefaults: [
                new QuickLinkEntry("GitHub", "https://github.com/owner/{projectName}"),
            ],
            projectOverrides: _ => [new QuickLinkEntry("GitHub", "", Disabled: true)]);

        Assert.Empty(svc.ResolveForProject(Ctx()));
    }

    [Fact]
    public void Resolve_SubTabTargetUnavailableInV1()
    {
        var svc = new QuickLinkService([
            new QuickLinkEntry("Future", "https://example.test/{projectName}", QuickLinkTarget.SubTab),
        ]);
        var link = Assert.Single(svc.ResolveForProject(Ctx()));
        Assert.False(link.Available);
        Assert.Equal("(V2)", link.UnavailableReason);
    }
}
