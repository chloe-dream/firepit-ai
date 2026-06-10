namespace Firepit.Core.ProjectConfig;

/// <summary>
/// Pure-function helpers for mutating a <see cref="ProjectConfig"/>'s commands
/// list. Kept in Firepit.Core so the MCP <c>firepit_add_command</c> handler
/// (and any other call-site that grows up later) shares one definition of
/// "upsert by name" — including the case-insensitive match.
/// </summary>
public static class ProjectCommandMutator
{
    /// <summary>
    /// Returns a new list with <paramref name="newCommand"/> appended, or
    /// replacing any existing entry whose <see cref="ProjectCommand.Name"/>
    /// matches (case-insensitive). The replacement keeps the original
    /// position; appended entries land at the end. Existing entries with
    /// different names are untouched.
    /// </summary>
    public static IReadOnlyList<ProjectCommand> Upsert(
        IReadOnlyList<ProjectCommand>? existing,
        ProjectCommand newCommand)
    {
        ArgumentNullException.ThrowIfNull(newCommand);
        var list = new List<ProjectCommand>(existing ?? Array.Empty<ProjectCommand>());
        var idx = list.FindIndex(c =>
            string.Equals(c.Name, newCommand.Name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) list[idx] = newCommand;
        else          list.Add(newCommand);
        return list;
    }

    /// <summary>
    /// Returns a new list with any entry whose <see cref="ProjectCommand.Name"/>
    /// matches <paramref name="name"/> (case-insensitive) removed, plus a
    /// boolean signalling whether anything actually changed. Removing a name
    /// that doesn't exist is a no-op — caller decides whether to surface that.
    /// </summary>
    public static (IReadOnlyList<ProjectCommand> commands, bool removed) RemoveByName(
        IReadOnlyList<ProjectCommand>? existing,
        string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (existing is null || existing.Count == 0)
            return (Array.Empty<ProjectCommand>(), false);

        var list = new List<ProjectCommand>(existing.Count);
        var hit  = false;
        foreach (var c in existing)
        {
            if (!hit && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                hit = true;
                continue;
            }
            list.Add(c);
        }
        return (list, hit);
    }
}
