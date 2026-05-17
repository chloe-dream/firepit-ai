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

// Resource definition returned by resources/list. Tool definitions live as
// internal records next to the catalog (McpToolDefinitionRaw) — they carry
// the inline JSON schema string rather than a parsed JsonElement.

public sealed record McpResourceDefinition(
    string Uri,
    string Name,
    string Description,
    string MimeType);
