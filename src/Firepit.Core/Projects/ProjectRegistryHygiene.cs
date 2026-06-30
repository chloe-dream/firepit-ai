using System.IO;

namespace Firepit.Core.Projects;

/// <summary>How a manual (settings.json <c>projects[]</c>) entry maps to disk.</summary>
public enum ManualEntryStatus
{
    /// <summary>The project directory exists — keep it and show it.</summary>
    Alive,

    /// <summary>
    /// The directory is gone but its PARENT still exists — the folder was
    /// renamed or deleted, leaving a dead registry block. Safe to prune.
    /// </summary>
    Orphaned,

    /// <summary>
    /// Neither the directory nor its parent exists — the drive / parent is
    /// probably just offline (network or cloud-sync mount). Keep the entry; it
    /// may come back, and pruning it would lose a legitimate project.
    /// </summary>
    Unavailable,
}

/// <summary>
/// Classifies manual project-registry entries so a renamed/deleted project
/// folder (an orphan) can be pruned without nuking entries that merely live on
/// a temporarily-offline drive. The shell prunes <see cref="ManualEntryStatus.Orphaned"/>
/// entries on project-list reload and persists the cleaned registry.
/// </summary>
public static class ProjectRegistryHygiene
{
    public static ManualEntryStatus Classify(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            // A blank path can never resolve to a project — treat as orphaned.
            return ManualEntryStatus.Orphaned;
        }

        if (Directory.Exists(projectPath))
        {
            return ManualEntryStatus.Alive;
        }

        string? parent;
        try
        {
            parent = Path.GetDirectoryName(Path.GetFullPath(projectPath.TrimEnd('\\', '/')));
        }
        catch
        {
            // Unparseable path — it can't be a live project, so it's an orphan.
            return ManualEntryStatus.Orphaned;
        }

        // Parent present but the folder gone = a genuine rename/delete (prune).
        // Parent also gone = the whole mount is unavailable (keep, may return).
        return !string.IsNullOrEmpty(parent) && Directory.Exists(parent)
            ? ManualEntryStatus.Orphaned
            : ManualEntryStatus.Unavailable;
    }
}
