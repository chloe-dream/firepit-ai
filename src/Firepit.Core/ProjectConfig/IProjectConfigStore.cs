namespace Firepit.Core.ProjectConfig;

/// <summary>
/// Reads and writes <c>&lt;projectPath&gt;/.firepit/config.json</c>.
/// Returns <c>null</c> if no file exists for that project.
/// </summary>
public interface IProjectConfigStore
{
    ProjectConfig? Load(string projectPath);

    void Save(string projectPath, ProjectConfig config);
}
