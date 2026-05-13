namespace Firepit.Core.Jobs;

/// <summary>
/// Everything the runner needs to spawn one headless Claude run.
/// Built by the scheduler (for scheduled / catchup triggers) or by the
/// UI / MCP handler (for manual triggers).
///
/// Defaults mirror the agreed Firepit job semantics:
/// <c>claude -p "&lt;prompt&gt;" --output-format json</c>, timeout 300s,
/// no permission skip.
/// </summary>
public sealed record JobRunRequest(
    string ProjectPath,
    string ProjectName,
    string JobName,
    string Prompt,
    JobTrigger Trigger,
    string ClaudeExecutable = "claude",
    int TimeoutSeconds = 300,
    IReadOnlyList<string>? AllowedTools = null,
    int? MaxTurns = null,
    decimal? MaxBudgetUsd = null,
    bool SkipPermissions = false,
    IReadOnlyDictionary<string, string?>? EnvOverrides = null);
