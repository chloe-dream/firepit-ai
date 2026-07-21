namespace Firepit.Knowledge.Indexing;

// The one frontmatter key the knowledge system owns: `pin`. A doc whose
// leading YAML block contains `pin: true` belongs to the always-on tier and
// is compiled into the generated `.firepit/knowledge-pinned.md` digest
// (PinnedDigest). Everything else in the frontmatter is passed through
// untouched — this is a targeted key edit, not a YAML round-tripper.
internal static class PinnedFrontmatter
{
    public static bool IsPinned(string markdown)
    {
        var lines = SplitLines(markdown);
        var block = FindFrontmatter(lines);
        if (block is null)
        {
            return false;
        }

        var (start, end) = block.Value;
        for (var i = start + 1; i < end; i++)
        {
            if (TryParsePinLine(lines[i], out var value))
            {
                return value;
            }
        }

        return false;
    }

    /// <summary>Returns the markdown with `pin: true` added to / removed from
    /// its frontmatter. Adding creates a minimal block when none exists;
    /// removing drops the block entirely when `pin` was its only key.</summary>
    public static string SetPinned(string markdown, bool pinned)
    {
        var lines = new List<string>(SplitLines(markdown));
        var block = FindFrontmatter(lines);

        if (block is null)
        {
            return pinned
                ? "---\npin: true\n---\n\n" + markdown.TrimStart('\r', '\n')
                : markdown;
        }

        var (start, end) = block.Value;
        var pinLine = -1;
        for (var i = start + 1; i < end; i++)
        {
            if (TryParsePinLine(lines[i], out _))
            {
                pinLine = i;
                break;
            }
        }

        if (pinned)
        {
            if (pinLine >= 0)
            {
                lines[pinLine] = "pin: true";
            }
            else
            {
                lines.Insert(start + 1, "pin: true");
            }
        }
        else if (pinLine >= 0)
        {
            lines.RemoveAt(pinLine);
            end--;
            var blockEmpty = true;
            for (var i = start + 1; i < end; i++)
            {
                if (lines[i].Trim().Length > 0)
                {
                    blockEmpty = false;
                    break;
                }
            }

            if (blockEmpty)
            {
                lines.RemoveRange(start, end - start + 1);
                while (lines.Count > start && lines[start].Trim().Length == 0)
                {
                    lines.RemoveAt(start);
                }
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>The markdown body with any leading frontmatter block removed —
    /// what the digest quotes.</summary>
    public static string StripFrontmatterBlock(string markdown)
    {
        var lines = SplitLines(markdown);
        var block = FindFrontmatter(lines);
        if (block is null)
        {
            return markdown;
        }

        return string.Join('\n', lines[(block.Value.End + 1)..]).TrimStart('\n');
    }

    private static string[] SplitLines(string markdown) =>
        (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    // (startLine, endLine) of the delimiters, or null. Same recognition rules
    // as MarkdownChunker.StripFrontmatter: block must open on line 0, close
    // on `---` or `...`; unterminated blocks don't count.
    private static (int Start, int End)? FindFrontmatter(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || lines[0].TrimEnd() != "---")
        {
            return null;
        }

        for (var i = 1; i < lines.Count; i++)
        {
            var end = lines[i].TrimEnd();
            if (end == "---" || end == "...")
            {
                return (0, i);
            }
        }

        return null;
    }

    private static bool TryParsePinLine(string line, out bool value)
    {
        value = false;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("pin:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = trimmed[4..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        return true;
    }
}
