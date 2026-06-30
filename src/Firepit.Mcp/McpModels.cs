namespace Firepit.Mcp;

// DTOs the host serialises out as MCP results / resources. Plain records,
// camelCase via the source-gen context.

public sealed record ProjectInfo(
    string Name,
    string Path,
    string AdapterId,
    bool IsOpen,
    string? SessionState);

public sealed record SessionInfo(
    string ProjectName,
    string ProjectPath,
    string State,
    bool IsActive);

public sealed record ToolCallResult(
    bool Ok,
    string? Message = null);

public sealed record InboxWriteResult(
    bool Ok,
    string? Path = null,
    string? Message = null);

/// <summary>
/// One pending inbox message as returned by firepit_inbox_list. <see cref="Id"/>
/// is the filename (no path) — agents pass it back to firepit_inbox_complete.
/// </summary>
public sealed record InboxMessage(
    string Id,
    string? From,
    string? Subject,
    string? Priority,
    string? Date,
    string Body);

public sealed record InboxListResult(
    string Project,
    IReadOnlyList<InboxMessage> Messages);

/// <summary>One toolbar command as exposed by firepit_list_commands. Mirrors
/// ProjectCommand but flattens the type discriminator to a string and drops
/// fields that don't apply to the current type so agents see a clean payload.</summary>
public sealed record CommandSummary(
    string Name,
    string Type,
    string? Icon,
    string? Command,
    IReadOnlyList<string>? Args,
    string? Prompt,
    string? Url,
    string? Cwd,
    IReadOnlyDictionary<string, string?>? Env,
    bool? Elevated,
    bool? Confirm,
    string? Window,
    bool? LongRunning,
    bool? KeepOpenOnError,
    bool? Disabled);

public sealed record CommandListResult(
    string Project,
    IReadOnlyList<CommandSummary> Commands);

/// <summary>
/// Payload for firepit_add_command. Mirrors ProjectCommand 1:1 but uses string
/// for the type discriminator so the wire layer doesn't carry a Core enum.
/// The handler validates type + required fields per type.
/// </summary>
public sealed record AddCommandSpec(
    string Name,
    string Type,
    string? Icon = null,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    string? Prompt = null,
    string? Url = null,
    string? Cwd = null,
    IReadOnlyDictionary<string, string?>? Env = null,
    bool? Elevated = null,
    bool? Confirm = null,
    string? Window = null,
    bool? LongRunning = null,
    bool? KeepOpenOnError = null);

// Resource definition returned by resources/list. Tool definitions live as
// internal records next to the catalog (McpToolDefinitionRaw) — they carry
// the inline JSON schema string rather than a parsed JsonElement.

public sealed record McpResourceDefinition(
    string Uri,
    string Name,
    string Description,
    string MimeType);
