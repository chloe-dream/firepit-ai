using System.Text.Json;
using System.Text.Json.Serialization;

namespace Firepit.Core.State;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    WriteIndented = true)]
[JsonSerializable(typeof(AppState))]
internal partial class StateJsonContext : JsonSerializerContext
{
}
