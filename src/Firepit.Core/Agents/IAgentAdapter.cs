using Firepit.Core.Projects;

namespace Firepit.Core.Agents;

public interface IAgentAdapter
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<string> ProjectMarkers { get; }

    AgentLaunchSpec BuildLaunchSpec(ProjectContext context, AgentLaunchOptions options);
}
