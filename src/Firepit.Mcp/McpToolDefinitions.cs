namespace Firepit.Mcp;

/// <summary>
/// Static catalogue of MCP tool definitions exposed by the Firepit host.
/// Schemas are inline JSON strings — small, easy to read, no extra plumbing.
/// </summary>
internal static class McpToolDefinitions
{
    public static readonly IReadOnlyList<McpToolDefinitionRaw> All =
    [
        new("firepit_list_projects",
            "List every project Firepit knows about (discovered + manual entries). " +
            "Returns name, path, adapter id, and whether the project currently has an open tab.",
            """
            { "type": "object", "properties": {}, "additionalProperties": false }
            """),

        new("firepit_open_tab",
            "Open a tab for a project (or focus the existing one if it's already open). " +
            "Optionally pass resume=true to launch with --continue.",
            """
            {
              "type": "object",
              "properties": {
                "projectName": { "type": "string", "description": "Name of the project to open." },
                "resume":      { "type": "boolean", "description": "Use --continue to resume the last session.", "default": false }
              },
              "required": ["projectName"],
              "additionalProperties": false
            }
            """),

        new("firepit_focus_tab",
            "Bring an existing tab into focus and hand keyboard focus to its terminal. " +
            "No-op if the project isn't open.",
            """
            {
              "type": "object",
              "properties": {
                "projectName": { "type": "string" }
              },
              "required": ["projectName"],
              "additionalProperties": false
            }
            """),

        new("firepit_close_tab",
            "Close the tab for a project, killing the running agent. No-op if not open.",
            """
            {
              "type": "object",
              "properties": {
                "projectName": { "type": "string" }
              },
              "required": ["projectName"],
              "additionalProperties": false
            }
            """),

        new("firepit_reload",
            "Re-read <project>/.firepit/config.json and apply hot-reloadable parts (quick-links). " +
            "Pass restart=true to also restart the agent (needed for MCP / agent / env changes).",
            """
            {
              "type": "object",
              "properties": {
                "projectName": { "type": "string" },
                "restart":     { "type": "boolean", "description": "Also restart the agent.", "default": false }
              },
              "required": ["projectName"],
              "additionalProperties": false
            }
            """),

        new("firepit_inbox_list",
            "List pending (un-processed) inbox messages for a project. " +
            "Defaults to the calling agent's own project. Each entry contains " +
            "the id (filename) plus parsed frontmatter (from / subject / priority / date) " +
            "and the markdown body. Files moved to inbox/processed/ are excluded.",
            """
            {
              "type": "object",
              "properties": {
                "projectName": { "type": "string", "description": "Project to inspect; omit for the caller's own project." }
              },
              "additionalProperties": false
            }
            """),

        new("firepit_inbox_complete",
            "Mark an inbox message as processed by moving the file from " +
            "<project>/.firepit/inbox/<id> into the sibling inbox/processed/ folder. " +
            "Pass the 'id' returned by firepit_inbox_list. Idempotent.",
            """
            {
              "type": "object",
              "properties": {
                "id":          { "type": "string", "description": "Filename of the inbox message (as returned by firepit_inbox_list)." },
                "projectName": { "type": "string", "description": "Project containing the message; omit for the caller's own project." }
              },
              "required": ["id"],
              "additionalProperties": false
            }
            """),

        new("firepit_send_to",
            "Drop a markdown message into <toProject>/.firepit/inbox/. The receiving project's " +
            "Claude reads its inbox at session start (per the seed CLAUDE.md convention). " +
            "The 'from' field is taken from the calling agent's FIREPIT_PROJECT_NAME env var.",
            """
            {
              "type": "object",
              "properties": {
                "toProject": { "type": "string", "description": "Project name (folder name) to deliver to." },
                "subject":   { "type": "string" },
                "body":      { "type": "string", "description": "Markdown body." },
                "priority":  { "type": "string", "enum": ["low", "normal", "high"], "default": "normal" }
              },
              "required": ["toProject", "subject", "body"],
              "additionalProperties": false
            }
            """),
    ];
}

internal sealed record McpToolDefinitionRaw(string Name, string Description, string InputSchemaJson);

internal static class McpResourceDefinitions
{
    public static readonly IReadOnlyList<McpResourceDefinition> All =
    [
        new("firepit://projects",
            "Projects",
            "Current project list with open/closed status and live session state.",
            "application/json"),
        new("firepit://sessions",
            "Open sessions",
            "Tabs currently open in Firepit, with their activity state (Burning/Embers/Dead).",
            "application/json"),
        new("firepit://settings",
            "Effective settings",
            "Global Firepit settings.json with secrets redacted (cred-references opaque, " +
            "key/token/secret/password keys masked).",
            "application/json"),
    ];
}
