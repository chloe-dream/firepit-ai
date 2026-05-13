namespace Firepit.Core.Jobs;

/// <summary>
/// Persisted form of one run. Written to
/// <c>&lt;projectPath&gt;/.firepit/runs/&lt;jobName&gt;/&lt;utc&gt;.json</c>.
/// File names are sortable and human-readable timestamps.
///
/// Fields that the runner did not produce (e.g. cost for a failure) are
/// null. <c>EndedAt</c> being null means "still in flight" — the startup
/// recovery pass rewrites those to <see cref="JobRunStatus.Interrupted"/>.
/// </summary>
public sealed record JobRunRecord(
    int Version,
    string JobName,
    string ProjectName,
    JobTrigger Trigger,
    JobRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    long? DurationMs,
    int? ExitCode,
    string Prompt,
    string CommandLine,
    string StdoutInline,
    bool StdoutTruncated,
    string? StdoutSpilloverPath,
    string Stderr,
    int? TokensInput,
    int? TokensOutput,
    decimal? CostUsd,
    string? Summary,
    string? AssistantMessage)
{
    public const int CurrentVersion = 1;
}
