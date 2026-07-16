using System.Text;

namespace Firepit.Knowledge.Indexing;

internal sealed record MarkdownChunk(string? Heading, string Content, int Ordinal);

// Splits a Markdown knowledge file into embedding-sized chunks along its
// heading structure. Heading-scoped chunks keep semantically-related text
// together, which matters twice: MiniLM only sees the first ~128 tokens of
// whatever it embeds, and search results quote the chunk's heading so the
// reader can jump to the right section.
//
// Rules:
//  - YAML frontmatter (leading `--- … ---`) is metadata, not knowledge —
//    stripped before chunking.
//  - A new section starts at every ATX heading (`#` … `######`) outside a
//    fenced code block. `#` inside ``` fences is code, not structure.
//  - Sections longer than MaxChunkChars split at blank lines; a single
//    oversized paragraph hard-splits at the limit.
//  - Title = first H1, else the file name with dashes/underscores spaced.
internal static class MarkdownChunker
{
    internal const int MaxChunkChars = 1600;

    public static (string Title, IReadOnlyList<MarkdownChunk> Chunks) Chunk(string fileName, string markdown)
    {
        var lines = StripFrontmatter(markdown ?? string.Empty);

        string? title = null;
        var chunks = new List<MarkdownChunk>();
        var section = new StringBuilder();
        string? currentHeading = null;
        var inFence = false;
        var ordinal = 0;

        void FlushSection()
        {
            var text = section.ToString().Trim();
            section.Clear();
            if (text.Length == 0)
            {
                return;
            }

            foreach (var piece in SplitOversized(text))
            {
                chunks.Add(new MarkdownChunk(currentHeading, piece, ordinal++));
            }
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            if (!inFence && TryParseHeading(line, out var level, out var headingText))
            {
                FlushSection();
                currentHeading = headingText;
                if (level == 1)
                {
                    title ??= headingText;
                }

                continue;
            }

            section.AppendLine(line);
        }

        FlushSection();

        title ??= TitleFromFileName(fileName);
        return (title, chunks);
    }

    private static IReadOnlyList<string> StripFrontmatter(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd() != "---")
        {
            return lines;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var end = lines[i].TrimEnd();
            if (end == "---" || end == "...")
            {
                return lines[(i + 1)..];
            }
        }

        // Unterminated frontmatter — treat the whole file as content rather
        // than silently dropping everything.
        return lines;
    }

    private static bool TryParseHeading(string line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;

        var i = 0;
        while (i < line.Length && line[i] == '#')
        {
            i++;
        }

        if (i is 0 or > 6 || i >= line.Length || line[i] != ' ')
        {
            return false;
        }

        level = i;
        text = line[(i + 1)..].Trim().TrimEnd('#').Trim();
        return text.Length > 0;
    }

    private static IEnumerable<string> SplitOversized(string text)
    {
        if (text.Length <= MaxChunkChars)
        {
            yield return text;
            yield break;
        }

        var current = new StringBuilder();
        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var para = paragraph.Trim();
            if (para.Length == 0)
            {
                continue;
            }

            if (current.Length > 0 && current.Length + para.Length + 2 > MaxChunkChars)
            {
                yield return current.ToString().Trim();
                current.Clear();
            }

            if (para.Length > MaxChunkChars)
            {
                // A single paragraph past the limit (giant table, minified
                // blob) — hard-split; a seam mid-sentence beats a chunk the
                // embedder truncates anyway.
                for (var offset = 0; offset < para.Length; offset += MaxChunkChars)
                {
                    var len = Math.Min(MaxChunkChars, para.Length - offset);
                    yield return para.Substring(offset, len).Trim();
                }

                continue;
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(para);
        }

        if (current.Length > 0)
        {
            yield return current.ToString().Trim();
        }
    }

    private static string TitleFromFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return "Untitled";
        }

        return stem.Replace('-', ' ').Replace('_', ' ').Trim();
    }
}
