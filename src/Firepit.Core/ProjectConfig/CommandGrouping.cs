namespace Firepit.Core.ProjectConfig;

/// <summary>
/// One rendered toolbar entry: either a standalone <see cref="Single"/> command
/// or a collapsed group (<see cref="GroupLabel"/> + <see cref="GroupMembers"/>).
/// </summary>
public sealed class CommandRenderUnit
{
    public ProjectCommand? Single { get; }
    public string? GroupLabel { get; }
    public IReadOnlyList<ProjectCommand>? GroupMembers { get; }

    public bool IsGroup => GroupLabel is not null;

    private CommandRenderUnit(ProjectCommand single) => Single = single;

    private CommandRenderUnit(string label, IReadOnlyList<ProjectCommand> members)
    {
        GroupLabel = label;
        GroupMembers = members;
    }

    public static CommandRenderUnit ForCommand(ProjectCommand command) => new(command);

    public static CommandRenderUnit ForGroup(string label, IReadOnlyList<ProjectCommand> members) =>
        new(label, members);
}

/// <summary>
/// Lays out toolbar commands with opt-in grouping. Commands that share a
/// <see cref="ProjectCommand.Group"/> label collapse into a single dropdown
/// unit (for multi-target projects — Build &amp; Run / Debug / Release); every
/// other command renders standalone. Grouping is strictly opt-in, so a project
/// with no group labels — or a genuine multi-command project that just doesn't
/// use them — is never auto-collapsed.
/// </summary>
public static class CommandGrouping
{
    /// <summary>
    /// Enabled commands in config order, with each group of 2+ members collapsed
    /// into one unit positioned at the group's first member. A group of one
    /// renders standalone (a dropdown of one is noise); blank/whitespace group
    /// labels count as ungrouped; disabled commands are dropped.
    /// </summary>
    public static IReadOnlyList<CommandRenderUnit> Plan(IReadOnlyList<ProjectCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var enabled = commands.Where(c => c.Disabled != true).ToList();

        var groupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in enabled)
        {
            var g = Normalize(c.Group);
            if (g is not null)
            {
                groupCounts[g] = groupCounts.GetValueOrDefault(g) + 1;
            }
        }

        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var units = new List<CommandRenderUnit>();
        foreach (var c in enabled)
        {
            var g = Normalize(c.Group);
            if (g is null || groupCounts[g] < 2)
            {
                units.Add(CommandRenderUnit.ForCommand(c));
                continue;
            }
            if (!emitted.Add(g))
            {
                continue; // group already emitted at its first member
            }
            var members = enabled
                .Where(m => string.Equals(Normalize(m.Group), g, StringComparison.OrdinalIgnoreCase))
                .ToList();
            units.Add(CommandRenderUnit.ForGroup(g, members));
        }
        return units;
    }

    private static string? Normalize(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();
}
