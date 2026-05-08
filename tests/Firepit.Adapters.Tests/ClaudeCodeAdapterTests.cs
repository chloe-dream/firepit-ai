using Firepit.Adapters;
using Firepit.Core.Agents;
using Firepit.Core.Projects;

namespace Firepit.Adapters.Tests;

public class ClaudeCodeAdapterTests
{
    private static ProjectContext Ctx(
        string? command = null,
        IReadOnlyList<string>? args = null)
        => new(new Project(
            Name: "lighthouse",
            Path: @"D:\Code\lighthouse",
            AdapterId: ClaudeCodeAdapter.AdapterId,
            AgentCommandOverride: command,
            AgentArgsOverride: args));

    [Fact]
    public void DefaultLaunch_UsesClaudeAndProjectPath()
    {
        var adapter = new ClaudeCodeAdapter();
        var spec = adapter.BuildLaunchSpec(Ctx(), new AgentLaunchOptions());

        Assert.Equal("claude", spec.Executable);
        Assert.Empty(spec.Arguments);
        Assert.Equal(@"D:\Code\lighthouse", spec.WorkingDirectory);
    }

    [Fact]
    public void Resume_AddsContinueFlag()
    {
        var adapter = new ClaudeCodeAdapter();
        var spec = adapter.BuildLaunchSpec(Ctx(), new AgentLaunchOptions(Resume: true));

        Assert.Equal(["--continue"], spec.Arguments);
    }

    [Fact]
    public void SessionId_AddsResumeFlag()
    {
        var adapter = new ClaudeCodeAdapter();
        var spec = adapter.BuildLaunchSpec(Ctx(), new AgentLaunchOptions(SessionId: "abc123"));

        Assert.Equal(["--resume", "abc123"], spec.Arguments);
    }

    [Fact]
    public void ProjectOverrides_TakePrecedence()
    {
        var adapter = new ClaudeCodeAdapter();
        var spec = adapter.BuildLaunchSpec(
            Ctx(command: "claude.cmd", args: ["--model", "sonnet"]),
            new AgentLaunchOptions(Resume: true));

        Assert.Equal("claude.cmd", spec.Executable);
        Assert.Equal(["--model", "sonnet", "--continue"], spec.Arguments);
    }

    [Fact]
    public void Markers_ReportsClaudeFiles()
    {
        var adapter = new ClaudeCodeAdapter();
        Assert.Contains("CLAUDE.md", adapter.ProjectMarkers);
        Assert.Contains(".claude", adapter.ProjectMarkers);
    }
}
