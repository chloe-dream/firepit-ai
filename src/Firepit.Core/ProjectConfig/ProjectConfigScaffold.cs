using System.IO;

namespace Firepit.Core.ProjectConfig;

/// <summary>
/// Generates a commented JSONC scaffold for a project's <c>.firepit/config.json</c>.
/// All sections are present but empty/disabled so the file parses unchanged
/// and the user has a tour of every knob without surprises.
///
/// The reader (<see cref="ProjectConfigJsonContext"/>) is configured with
/// <c>ReadCommentHandling=Skip</c>, so the inline comments survive the
/// parse round-trip.
/// </summary>
public static class ProjectConfigScaffold
{
    /// <summary>
    /// Path to the per-project Firepit config file for the given project root.
    /// Does not check existence.
    /// </summary>
    public static string GetConfigPath(string projectPath) =>
        Path.Combine(projectPath, ".firepit", "config.json");

    /// <summary>
    /// Ensure <c>.firepit/config.json</c> exists for the given project. If
    /// missing, writes the scaffold (creating <c>.firepit/</c> as needed).
    /// Returns the absolute path either way. Never overwrites an existing file.
    /// </summary>
    public static string EnsureScaffold(string projectPath, string projectId)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(projectId);

        var target = GetConfigPath(projectPath);
        if (File.Exists(target)) return target;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, BuildScaffold(projectId));
        return target;
    }

    public static string BuildScaffold(string projectId) => $$"""
{
  // Firepit per-project config — see docs/ARCHITECTURE.md §9 for the schema.
  // JSONC: comments and trailing commas are allowed.
  // Edit and save — quickLinks and commands hot-reload; mcpActivations,
  // agent, and session.envOverrides need a session restart (Firepit shows a
  // banner when that happens).

  "version": 1,
  "id": "{{projectId}}",

  // Toolbar URL buttons. {projectName} and {projectPath} are substituted.
  // "quickLinks": [
  //   { "name": "GitHub", "url": "https://github.com/<you>/{projectName}" },
  //   { "name": "Docs",   "url": "https://your-docs.example.com/{projectName}" }
  // ],

  // Activate MCP servers from the global registry (settings.json -> mcpServers).
  // Optional per-server overrides for args/env/headers.
  // "mcpActivations": [
  //   { "id": "firepit" },
  //   { "id": "fishbowl",
  //     "headerOverrides": { "Authorization": "Bearer ${cred:firepit/fishbowl-{{projectId}}}" } }
  // ],

  // Per-project agent override. If omitted, falls back to settings.defaultAgent.
  // "agent": {
  //   "command": "claude",
  //   "args": ["--model", "sonnet"],
  //   "envOverrides": { "ANTHROPIC_LOG": "info" }
  // },

  // Extra env vars on the PTY child (the agent + everything it spawns).
  // "session": {
  //   "envOverrides": { "MY_VAR": "value" }
  // },

  // Custom toolbar commands. Three types:
  //   "shell"         — spawn Command + Args in the project dir (new window)
  //   "claude-prompt" — paste Prompt into the live session as if you typed it
  //   "url"           — open Url in the default browser
  //
  // Shell-only knobs (issue #11):
  //   cwd          — working dir (default: project root)
  //   env          — extra env vars on the child
  //   elevated     — Windows UAC prompt (run as administrator)
  //   confirm      — modal "Run?" prompt before spawning
  //   window       — "new" (default) | "reuse:<id>" | "inline"
  //                  reuse:<id>  → second click focuses the existing window
  //                  inline      → write the command into THIS tab's PTY
  //   longRunning  — true keeps a live indicator on the button + right-click
  //                  "Stop" kills the process tree
  //   keepOpenOnError — true: the spawned console closes on success but stays
  //                  open (pauses) on a non-zero exit so you can read the
  //                  error. Replaces blanket "-NoExit". Windowed shell only.
  // "commands": [
  //   { "name": "Tests",   "type": "shell",         "command": "pwsh", "args": ["-c", "dotnet test"] },
  //   { "name": "Dev",     "type": "shell",         "command": "npm",  "args": ["run", "dev"],
  //     "window": "reuse:dev", "longRunning": true },
  //   { "name": "Build",   "type": "shell",         "command": "dotnet", "args": ["build"], "window": "inline" },
  //   { "name": "Refactor","type": "claude-prompt", "prompt": "Look for code smells in src/ and propose fixes." },
  //   { "name": "Issues",  "type": "url",           "url": "https://github.com/<you>/{projectName}/issues" }
  // ],

  // Scheduled headless Claude runs. Each entry fires on its cron schedule
  // and spawns `claude -p "<prompt>" --output-format json` in this project's
  // directory. Output lands under .firepit/runs/<name>/<utc>.json and shows
  // up as a tab badge.
  //   name              — unique within this project
  //   prompt            — what gets passed to `claude -p` (slash-commands work)
  //   schedule          — standard 5-field cron expression
  //   enabled           — toggle without deleting (default true)
  //   timeoutSeconds    — kill after N seconds (default 300)
  //   timezone          — IANA tz name (default = local)
  //   onConcurrent      — skip | queue | killAndRestart (default skip)
  //   notify            — always | onChange | never (default onChange)
  //                       controls cross-Claude inbox messaging
  //   allowedTools      — passed through as --allowedTools
  //   maxTurns          — passed through as --max-turns
  //   maxBudgetUsd      — passed through as --max-budget-usd
  //   skipPermissions   — --dangerously-skip-permissions (default false)
  // "scheduledJobs": [
  //   { "name": "check-mails", "prompt": "/check-mails",
  //     "schedule": "*/30 * * * *", "notify": "onChange" },
  //   { "name": "weekly-review", "prompt": "/weekly-review",
  //     "schedule": "0 8 * * 1", "timeoutSeconds": 900 }
  // ],

  // Per-project overrides for the runs feature. Null → inherit from
  // global settings.platform.
  // "runs": {
  //   "badgePolicy": "all",         // "all" | "failuresOnly"
  //   "retentionDays": 30
  // }
}
""";
}
