using Firepit.Core.Projects;

namespace Firepit.Core.Mcp;

public interface IAgentMcpProjector
{
    Task ApplyAsync(
        ProjectContext context,
        IReadOnlyList<ResolvedMcpServer> activeServers,
        CancellationToken ct);
}
