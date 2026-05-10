using System.Text.Json;
using System.Text.Json.Serialization;

namespace Firepit.Mcp;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(ProjectInfo))]
[JsonSerializable(typeof(IReadOnlyList<ProjectInfo>))]
[JsonSerializable(typeof(SessionInfo))]
[JsonSerializable(typeof(IReadOnlyList<SessionInfo>))]
[JsonSerializable(typeof(ToolCallResult))]
[JsonSerializable(typeof(InboxWriteResult))]
[JsonSerializable(typeof(McpToolDefinition))]
[JsonSerializable(typeof(IReadOnlyList<McpToolDefinition>))]
[JsonSerializable(typeof(McpResourceDefinition))]
[JsonSerializable(typeof(IReadOnlyList<McpResourceDefinition>))]
[JsonSerializable(typeof(JsonElement))]
internal partial class McpJsonContext : JsonSerializerContext
{
}
