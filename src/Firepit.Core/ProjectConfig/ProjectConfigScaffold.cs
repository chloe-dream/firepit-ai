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
  // "commands": [
  //   { "name": "Tests",   "type": "shell",         "command": "pwsh", "args": ["-c", "dotnet test"] },
  //   { "name": "Refactor","type": "claude-prompt", "prompt": "Look for code smells in src/ and propose fixes." },
  //   { "name": "Issues",  "type": "url",           "url": "https://github.com/<you>/{projectName}/issues" }
  // ]
}
""";
}
