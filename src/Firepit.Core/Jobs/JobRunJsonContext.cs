using System.Text.Json;
using System.Text.Json.Serialization;

namespace Firepit.Core.Jobs;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(JobRunRecord))]
internal partial class JobRunJsonContext : JsonSerializerContext
{
}
