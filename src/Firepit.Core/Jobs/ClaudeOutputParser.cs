using System.Text.Json;

namespace Firepit.Core.Jobs;

/// <summary>
/// Best-effort extraction of token usage, cost, and assistant message from
/// Claude Code's <c>--output-format json</c> stdout. Forgiving by design —
/// Claude's output shape is not Firepit's contract. Unknown shapes return null
/// metadata and the raw stdout is preserved on the outcome.
///
/// Looks for, in order of preference:
/// <list type="bullet">
///   <item>top-level <c>usage.input_tokens</c> / <c>usage.output_tokens</c></item>
///   <item><c>total_cost_usd</c></item>
///   <item><c>result</c> as the assistant message</item>
///   <item><c>summary</c> as the cross-Claude inbox payload (custom convention)</item>
/// </list>
/// </summary>
public static class ClaudeOutputParser
{
    public static ClaudeResultMetadata? TryParse(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return null;

        // Claude prints a single JSON object on stdout. Trim before parsing —
        // some shells slip a trailing newline or stray BOM character.
        var trimmed = stdout.AsSpan().Trim().ToString();
        if (trimmed.Length == 0 || trimmed[0] is not ('{' or '[')) return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            // If Claude streams an array of messages, take the last "result"-typed
            // entry. The non-streaming default returns one object.
            if (root.ValueKind == JsonValueKind.Array)
            {
                root = FindLastResultObject(root);
                if (root.ValueKind != JsonValueKind.Object) return null;
            }

            return new ClaudeResultMetadata(
                TokensInput:      TryReadInt(root, "usage", "input_tokens"),
                TokensOutput:     TryReadInt(root, "usage", "output_tokens"),
                CostUsd:          TryReadDecimal(root, "total_cost_usd"),
                Summary:          TryReadString(root, "summary"),
                AssistantMessage: TryReadString(root, "result"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement FindLastResultObject(JsonElement array)
    {
        JsonElement? last = null;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (item.TryGetProperty("type", out var typeProp) &&
                typeProp.ValueKind == JsonValueKind.String &&
                typeProp.GetString() == "result")
            {
                last = item;
            }
        }
        return last ?? default;
    }

    private static int? TryReadInt(JsonElement root, params string[] path)
    {
        if (!TryFollow(root, path, out var elem)) return null;
        return elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out var v) ? v : null;
    }

    private static decimal? TryReadDecimal(JsonElement root, params string[] path)
    {
        if (!TryFollow(root, path, out var elem)) return null;
        return elem.ValueKind == JsonValueKind.Number && elem.TryGetDecimal(out var v) ? v : null;
    }

    private static string? TryReadString(JsonElement root, params string[] path)
    {
        if (!TryFollow(root, path, out var elem)) return null;
        return elem.ValueKind == JsonValueKind.String ? elem.GetString() : null;
    }

    private static bool TryFollow(JsonElement root, string[] path, out JsonElement leaf)
    {
        leaf = root;
        foreach (var key in path)
        {
            if (leaf.ValueKind != JsonValueKind.Object || !leaf.TryGetProperty(key, out leaf))
            {
                leaf = default;
                return false;
            }
        }
        return true;
    }
}
