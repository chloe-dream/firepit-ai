namespace Firepit.Core.Projects;

public sealed record ProjectContext(
    Project Project)
{
    public string Name => Project.Name;
    public string Path => Project.Path;
    public string AdapterId => Project.AdapterId;
}
