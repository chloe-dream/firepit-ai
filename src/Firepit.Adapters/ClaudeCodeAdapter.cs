using Firepit.Core.Agents;
using Firepit.Core.Projects;

namespace Firepit.Adapters;

public sealed class ClaudeCodeAdapter : IAgentAdapter
{
    public const string AdapterId = "claude-code";

    private readonly string _defaultExecutable;

    public ClaudeCodeAdapter(string defaultExecutable = "claude")
    {
        _defaultExecutable = defaultExecutable;
    }

    public string Id => AdapterId;

    public string DisplayName => "Claude Code";

    public IReadOnlyList<string> ProjectMarkers { get; } = ["CLAUDE.md", ".claude"];

    public AgentLaunchSpec BuildLaunchSpec(ProjectContext context, AgentLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var executable = context.Project.AgentCommandOverride ?? _defaultExecutable;
        var arguments = new List<string>();

        if (context.Project.AgentArgsOverride is { } overrides)
        {
            arguments.AddRange(overrides);
        }

        if (options.Resume)
        {
            arguments.Add("--continue");
        }

        if (!string.IsNullOrEmpty(options.SessionId))
        {
            arguments.Add("--resume");
            arguments.Add(options.SessionId);
        }

        return new AgentLaunchSpec(
            Executable: executable,
            Arguments: arguments,
            WorkingDirectory: context.Path);
    }
}
