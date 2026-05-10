using System.Text.Json;
using System.Text.Json.Serialization;

namespace Firepit.Core.ProjectConfig;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ProjectConfig))]
internal partial class ProjectConfigJsonContext : JsonSerializerContext
{
}
