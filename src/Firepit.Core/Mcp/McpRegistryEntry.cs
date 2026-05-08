namespace Firepit.Core.Mcp;

public sealed record McpRegistryEntry(
    string Id,
    string DisplayName,
    McpTransport Transport,
    string? Description = null,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    string? Url = null,
    IReadOnlyDictionary<string, string?>? Headers = null);

public sealed record McpProjectActivation(
    IReadOnlyList<string> ActiveIds,
    IReadOnlyDictionary<string, McpOverride>? Overrides = null);

public sealed record McpOverride(
    IReadOnlyList<string>? Args = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    IReadOnlyDictionary<string, string?>? Headers = null);

public sealed record ResolvedMcpServer(
    string Id,
    string DisplayName,
    McpTransport Transport,
    string? Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Environment,
    string? Url,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<string> ResolutionWarnings);
