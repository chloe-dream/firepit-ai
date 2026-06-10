using Firepit.Core.ProjectConfig;

namespace Firepit.Core.Tests.ProjectConfig;

public class ProjectCommandMutatorTests
{
    private static ProjectCommand Shell(string name, string command) =>
        new(Name: name, Type: ProjectCommandType.Shell, Command: command);

    [Fact]
    public void Upsert_AppendsWhenNameIsNew()
    {
        var existing = new[] { Shell("Tests", "pwsh") };
        var added    = Shell("Dev", "npm");

        var result = ProjectCommandMutator.Upsert(existing, added);

        Assert.Equal(2, result.Count);
        Assert.Equal("Tests", result[0].Name);
        Assert.Equal("Dev",   result[1].Name);
    }

    [Fact]
    public void Upsert_ReplacesByName_PreservingPosition()
    {
        var existing = new[]
        {
            Shell("Tests", "pwsh"),
            Shell("Dev",   "npm"),
            Shell("Build", "dotnet"),
        };
        // Replacing the middle entry — should keep index 1, not jump to the end.
        var updated = Shell("Dev", "pnpm");

        var result = ProjectCommandMutator.Upsert(existing, updated);

        Assert.Equal(3, result.Count);
        Assert.Equal("Tests", result[0].Name);
        Assert.Equal("Dev",   result[1].Name);
        Assert.Equal("pnpm",  result[1].Command);
        Assert.Equal("Build", result[2].Name);
    }

    [Fact]
    public void Upsert_NameMatchIsCaseInsensitive()
    {
        var existing = new[] { Shell("Dev", "npm") };
        var updated  = Shell("DEV", "pnpm");

        var result = ProjectCommandMutator.Upsert(existing, updated);

        Assert.Single(result);
        Assert.Equal("DEV",  result[0].Name);
        Assert.Equal("pnpm", result[0].Command);
    }

    [Fact]
    public void Upsert_NullExistingList_ReturnsSingletonWithNewCommand()
    {
        var added  = Shell("Tests", "pwsh");
        var result = ProjectCommandMutator.Upsert(null, added);
        Assert.Single(result);
        Assert.Equal("Tests", result[0].Name);
    }

    [Fact]
    public void Upsert_EmptyExistingList_ReturnsSingletonWithNewCommand()
    {
        var result = ProjectCommandMutator.Upsert(Array.Empty<ProjectCommand>(), Shell("Tests", "pwsh"));
        Assert.Single(result);
    }
}
