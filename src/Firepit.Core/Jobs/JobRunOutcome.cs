namespace Firepit.Core.Jobs;

/// <summary>
/// Result of one runner invocation. The runner returns one of <c>Success</c>,
/// <c>Failure</c>, or <c>Timeout</c>; the other <see cref="JobRunStatus"/>
/// values are set by higher layers (scheduler for <c>Skipped</c>, the
/// startup recovery pass for <c>Interrupted</c>).
///
/// <c>StdoutInline</c> holds up to <see cref="InlineStdoutLimitBytes"/>.
/// Anything beyond that is spilled to <c>StdoutSpilloverPath</c> and
/// <c>StdoutTruncated</c> goes true.
/// </summary>
public sealed record JobRunOutcome(
    JobRunStatus Status,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string CommandLine,
    string StdoutInline,
    bool StdoutTruncated,
    string? StdoutSpilloverPath,
    string Stderr,
    ClaudeResultMetadata? ClaudeMetadata = null)
{
    public const int InlineStdoutLimitBytes = 1_048_576; // 1 MiB

    public TimeSpan Duration => EndedAt - StartedAt;
}

/// <summary>
/// Best-effort parse of the JSON produced by
/// <c>claude -p &lt;prompt&gt; --output-format json</c>. Any field can be
/// null — Claude's output shape is not part of Firepit's contract, so the
/// parser is forgiving. Raw stdout is always preserved on the outcome.
/// </summary>
public sealed record ClaudeResultMetadata(
    int? TokensInput,
    int? TokensOutput,
    decimal? CostUsd,
    string? Summary,
    string? AssistantMessage);
