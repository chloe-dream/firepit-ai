using Firepit.Core.Projects;

namespace Firepit.Core.Mcp;

public interface IMcpRegistry
{
    IReadOnlyList<McpRegistryEntry> All { get; }

    McpRegistryEntry? Find(string id);

    IReadOnlyList<ResolvedMcpServer> ResolveForProject(ProjectContext context);
}
