using System.IO;
using Firepit.Core.Projects;

namespace Firepit.Core.Tests;

public class ProjectRegistryHygieneTests : IDisposable
{
    private readonly string _root;

    public ProjectRegistryHygieneTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "firepit-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Classify_ExistingDirectory_IsAlive()
    {
        Assert.Equal(ManualEntryStatus.Alive, ProjectRegistryHygiene.Classify(_root));
    }

    [Fact]
    public void Classify_MissingFolderUnderExistingParent_IsOrphaned()
    {
        // The parent (_root) exists; the project folder was renamed/deleted.
        var gone = Path.Combine(_root, "renamed-away");
        Assert.Equal(ManualEntryStatus.Orphaned, ProjectRegistryHygiene.Classify(gone));
    }

    [Fact]
    public void Classify_MissingParent_IsUnavailable()
    {
        // Parent doesn't exist either → looks like an offline drive/mount. Keep.
        var offline = Path.Combine("C:\\", Guid.NewGuid().ToString("N"), "project");
        Assert.Equal(ManualEntryStatus.Unavailable, ProjectRegistryHygiene.Classify(offline));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_BlankPath_IsOrphaned(string? path)
    {
        Assert.Equal(ManualEntryStatus.Orphaned, ProjectRegistryHygiene.Classify(path));
    }
}
