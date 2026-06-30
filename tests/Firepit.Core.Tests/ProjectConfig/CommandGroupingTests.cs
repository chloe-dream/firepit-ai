using Firepit.Core.ProjectConfig;

namespace Firepit.Core.Tests.ProjectConfig;

public class CommandGroupingTests
{
    private static ProjectCommand Cmd(string name, string? group = null, bool? disabled = null) =>
        new(name, ProjectCommandType.Shell, Command: "echo", Args: new[] { name }, Group: group, Disabled: disabled);

    [Fact]
    public void Plan_NoGroups_AllRenderStandaloneInOrder()
    {
        var units = CommandGrouping.Plan(new[] { Cmd("A"), Cmd("B"), Cmd("C") });

        Assert.Equal(3, units.Count);
        Assert.All(units, u => Assert.False(u.IsGroup));
        Assert.Equal(new[] { "A", "B", "C" }, units.Select(u => u.Single!.Name));
    }

    [Fact]
    public void Plan_GroupOfTwoOrMore_CollapsesToOneUnitAtFirstMember()
    {
        var units = CommandGrouping.Plan(new[]
        {
            Cmd("Build & Run", "Run"),
            Cmd("Debug", "Run"),
            Cmd("Release", "Run"),
        });

        var unit = Assert.Single(units);
        Assert.True(unit.IsGroup);
        Assert.Equal("Run", unit.GroupLabel);
        Assert.Equal(new[] { "Build & Run", "Debug", "Release" }, unit.GroupMembers!.Select(m => m.Name));
    }

    [Fact]
    public void Plan_GroupOfOne_RendersStandalone()
    {
        var units = CommandGrouping.Plan(new[] { Cmd("Solo", "Run") });

        var unit = Assert.Single(units);
        Assert.False(unit.IsGroup);
        Assert.Equal("Solo", unit.Single!.Name);
    }

    [Fact]
    public void Plan_MixedOrder_KeepsUngroupedInPlace_GroupAtFirstMember()
    {
        var units = CommandGrouping.Plan(new[]
        {
            Cmd("Lint"),            // standalone
            Cmd("Build", "Run"),    // group first member
            Cmd("Tests"),           // standalone
            Cmd("Debug", "Run"),    // folds into the Run group above
        });

        Assert.Equal(3, units.Count);
        Assert.Equal("Lint", units[0].Single!.Name);
        Assert.True(units[1].IsGroup);
        Assert.Equal(new[] { "Build", "Debug" }, units[1].GroupMembers!.Select(m => m.Name));
        Assert.Equal("Tests", units[2].Single!.Name);
    }

    [Fact]
    public void Plan_DropsDisabledCommands()
    {
        var units = CommandGrouping.Plan(new[]
        {
            Cmd("A"),
            Cmd("B", disabled: true),
        });

        var unit = Assert.Single(units);
        Assert.Equal("A", unit.Single!.Name);
    }

    [Fact]
    public void Plan_BlankGroupLabel_TreatedAsUngrouped()
    {
        var units = CommandGrouping.Plan(new[] { Cmd("A", "   "), Cmd("B", "") });

        Assert.Equal(2, units.Count);
        Assert.All(units, u => Assert.False(u.IsGroup));
    }

    [Fact]
    public void Plan_DisabledMemberDoesNotCountTowardGroupCollapse()
    {
        // One enabled + one disabled member sharing a label → only one enabled,
        // so it renders standalone, not as a dropdown of one.
        var units = CommandGrouping.Plan(new[]
        {
            Cmd("Build", "Run"),
            Cmd("Debug", "Run", disabled: true),
        });

        var unit = Assert.Single(units);
        Assert.False(unit.IsGroup);
        Assert.Equal("Build", unit.Single!.Name);
    }
}
