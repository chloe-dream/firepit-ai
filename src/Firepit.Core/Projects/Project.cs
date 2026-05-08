namespace Firepit.Core.Projects;

public sealed record Project(
    string Name,
    string Path,
    string AdapterId,
    string? AgentCommandOverride = null,
    IReadOnlyList<string>? AgentArgsOverride = null);
