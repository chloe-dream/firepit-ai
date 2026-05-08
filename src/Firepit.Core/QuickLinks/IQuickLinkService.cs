using Firepit.Core.Projects;

namespace Firepit.Core.QuickLinks;

public interface IQuickLinkService
{
    IReadOnlyList<ResolvedQuickLink> ResolveForProject(ProjectContext context);
}
