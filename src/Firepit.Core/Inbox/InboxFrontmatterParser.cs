namespace Firepit.Core.Inbox;

/// <summary>
/// Tiny YAML-frontmatter parser. Accepts the firepit_send_to format —
/// <c>---\nkey: value\nkey: value\n---\n\nbody</c> — plus the looser legacy
/// shape (no frontmatter at all → empty dict, body is the whole input).
/// Values are trimmed; nested structures aren't supported because Firepit
/// only emits flat key/value pairs in its inbox messages.
/// </summary>
public static class InboxFrontmatterParser
{
    public static (IReadOnlyDictionary<string, string> Frontmatter, string Body) Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var fm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!raw.StartsWith("---", StringComparison.Ordinal))
        {
            return (fm, raw);
        }
        var lines = raw.Split('\n');
        var endIdx = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r') == "---") { endIdx = i; break; }
        }
        if (endIdx < 0) return (fm, raw);

        for (var i = 1; i < endIdx; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var sep  = line.IndexOf(':');
            if (sep <= 0) continue;
            var key = line[..sep].Trim();
            var val = line[(sep + 1)..].Trim();
            if (key.Length > 0) fm[key] = val;
        }
        var bodyStart = endIdx + 1;
        if (bodyStart < lines.Length && string.IsNullOrWhiteSpace(lines[bodyStart].TrimEnd('\r')))
        {
            bodyStart++;
        }
        var body = string.Join('\n', lines.Skip(bodyStart));
        return (fm, body);
    }
}
