namespace Firepit.Core.Projects;

public interface IProjectDiscovery
{
    IReadOnlyList<Project> Scan(string projectsRoot, IEnumerable<Project>? manualEntries = null);
}
