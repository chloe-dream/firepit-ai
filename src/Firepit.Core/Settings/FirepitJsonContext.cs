using System.Text.Json;
using System.Text.Json.Serialization;

namespace Firepit.Core.Settings;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(FirepitSettings))]
public partial class FirepitJsonContext : JsonSerializerContext
{
}
