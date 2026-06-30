using System.IO;
using Firepit.Core.ProjectConfig;

namespace Firepit.Core.Tests.ProjectConfig;

public class ProjectScaffoldingTests : IDisposable
{
    private readonly string _projectPath;

    public ProjectScaffoldingTests()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), "firepit-hygiene-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectPath, recursive: true); } catch { /* best effort */ }
    }

    private string GitignorePath => Path.Combine(_projectPath, ".gitignore");
    private string ClaudeMdPath  => Path.Combine(_projectPath, "CLAUDE.md");

    [Fact]
    public void EnsureProjectScaffold_FreshProject_WritesConfigGitignoreAndClaudeMd()
    {
        var result = ProjectScaffolding.EnsureProjectScaffold(_projectPath, "demo");

        Assert.True(result.ScaffoldCreated);
        Assert.True(result.GitignoreUpdated);
        Assert.True(result.ClaudeMdSeeded);
        Assert.True(File.Exists(result.ConfigPath));

        var gitignore = File.ReadAllText(GitignorePath);
        Assert.Contains(".firepit/inbox/", gitignore);
        Assert.Contains(".firepit/runs/", gitignore);
        Assert.Contains("!.firepit/config.json", gitignore);
        Assert.Contains(".claude/settings.local.json", gitignore);
        Assert.Contains(".claude/*.lock", gitignore);
        Assert.Contains(".claude/agent-memory/", gitignore);

        Assert.Contains("firepit_inbox_complete", File.ReadAllText(ClaudeMdPath));
    }

    [Fact]
    public void EnsureProjectScaffold_ExistingConfig_LeavesGitSetupUntouched()
    {
        // Pre-create config.json so the scaffold is NOT fresh.
        ProjectConfigScaffold.EnsureScaffold(_projectPath, "demo");

        var result = ProjectScaffolding.EnsureProjectScaffold(_projectPath, "demo");

        Assert.False(result.ScaffoldCreated);
        Assert.False(result.GitignoreUpdated);
        Assert.False(result.ClaudeMdSeeded);
        // No .gitignore / CLAUDE.md materialised for an existing project.
        Assert.False(File.Exists(GitignorePath));
        Assert.False(File.Exists(ClaudeMdPath));
    }

    [Fact]
    public void EnsureGitignoreBlock_IsIdempotent_AndPreservesExistingLines()
    {
        File.WriteAllText(GitignorePath, "bin/\n.firepit/inbox/\n");

        var first = ProjectScaffolding.EnsureGitignoreBlock(_projectPath);
        var second = ProjectScaffolding.EnsureGitignoreBlock(_projectPath);

        Assert.True(first);    // appended the missing entries
        Assert.False(second);  // nothing left to add
        var gitignore = File.ReadAllText(GitignorePath);
        Assert.Contains("bin/", gitignore);
        // The already-present line is not duplicated.
        var inboxCount = gitignore.Split(".firepit/inbox/").Length - 1;
        Assert.Equal(1, inboxCount);
        Assert.Contains(".claude/agent-memory/", gitignore);
    }

    [Fact]
    public void DetectBlanketIgnores_FlagsBareDirIgnores_IgnoresGranularAndComments()
    {
        File.WriteAllText(GitignorePath,
            "# a comment\n.claude/\n.firepit/inbox/\nbin/\n  .firepit  \n");

        var blanket = ProjectScaffolding.DetectBlanketIgnores(_projectPath);

        Assert.Contains(".claude/", blanket);
        Assert.Contains(".firepit", blanket);   // trimmed
        Assert.DoesNotContain(".firepit/inbox/", blanket);
        Assert.DoesNotContain("bin/", blanket);
    }

    [Fact]
    public void MigrateBlanketIgnores_CommentsOutBlanketLines_AndAddsGranularBlock()
    {
        File.WriteAllText(GitignorePath, ".firepit/\n.claude/\n");

        var changed = ProjectScaffolding.MigrateBlanketIgnores(_projectPath);

        Assert.True(changed);
        var gitignore = File.ReadAllText(GitignorePath);
        // Blanket lines are now commented out...
        Assert.Contains("# .firepit/", gitignore);
        Assert.Contains("# .claude/", gitignore);
        // ...and the granular re-include is present.
        Assert.Contains("!.firepit/config.json", gitignore);
        // After migration nothing reads as a blanket ignore anymore.
        Assert.Empty(ProjectScaffolding.DetectBlanketIgnores(_projectPath));
    }

    [Fact]
    public void EnsureInboxConvention_AppendsToExistingClaudeMd_WithoutDuplicating()
    {
        File.WriteAllText(ClaudeMdPath, "# My Project\n\nSome existing guidance.\n");

        var first = ProjectScaffolding.EnsureInboxConvention(_projectPath);
        var second = ProjectScaffolding.EnsureInboxConvention(_projectPath);

        Assert.True(first);
        Assert.False(second);
        var content = File.ReadAllText(ClaudeMdPath);
        Assert.Contains("Some existing guidance.", content);
        var markerCount = content.Split("firepit_inbox_complete").Length - 1;
        Assert.Equal(1, markerCount);
    }
}
