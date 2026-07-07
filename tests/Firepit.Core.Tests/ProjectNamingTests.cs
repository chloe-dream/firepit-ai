using Firepit.Core.Projects;

namespace Firepit.Core.Tests;

public class ProjectNamingTests
{
    [Theory]
    [InlineData(@"C:\repos\foo", "foo")]
    [InlineData(@"C:\repos\foo\", "foo")]
    [InlineData(@"\\nas\music", "music")]          // UNC share root — the bug
    [InlineData(@"\\nas\music\", "music")]
    [InlineData(@"\\nas\share\proj", "proj")]      // UNC subdir already worked
    [InlineData(@"\\nas", "nas")]
    [InlineData("/mnt/data/proj", "proj")]         // forward slashes
    public void DeriveName_ReturnsLastSegment(string path, string expected)
    {
        Assert.Equal(expected, ProjectNaming.DeriveName(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeriveName_BlankPath_ReturnsEmpty(string? path)
    {
        Assert.Equal(string.Empty, ProjectNaming.DeriveName(path));
    }
}
