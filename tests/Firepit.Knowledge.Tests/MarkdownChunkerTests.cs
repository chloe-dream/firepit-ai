using Firepit.Knowledge.Indexing;

namespace Firepit.Knowledge.Tests;

public class MarkdownChunkerTests
{
    [Fact]
    public void Chunk_DerivesTitleFromFirstH1()
    {
        var (title, _) = MarkdownChunker.Chunk("file.md", "# ConPTY resize quirks\n\nBody text.");

        Assert.Equal("ConPTY resize quirks", title);
    }

    [Fact]
    public void Chunk_FallsBackToFileNameWhenNoH1()
    {
        var (title, _) = MarkdownChunker.Chunk("conpty-resize_quirks.md", "Just text, no heading.");

        Assert.Equal("conpty resize quirks", title);
    }

    [Fact]
    public void Chunk_StripsYamlFrontmatter()
    {
        var md = "---\nname: test\ntags: [a, b]\n---\n\n# Title\n\nReal content.";

        var (title, chunks) = MarkdownChunker.Chunk("f.md", md);

        Assert.Equal("Title", title);
        var chunk = Assert.Single(chunks);
        Assert.DoesNotContain("tags:", chunk.Content, StringComparison.Ordinal);
        Assert.Contains("Real content.", chunk.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_SplitsSectionsAtHeadings()
    {
        var md = "# Doc\n\nIntro paragraph.\n\n## First\n\nAlpha.\n\n## Second\n\nBeta.";

        var (_, chunks) = MarkdownChunker.Chunk("f.md", md);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Doc", chunks[0].Heading);
        Assert.Contains("Intro paragraph.", chunks[0].Content, StringComparison.Ordinal);
        Assert.Equal("First", chunks[1].Heading);
        Assert.Contains("Alpha.", chunks[1].Content, StringComparison.Ordinal);
        Assert.Equal("Second", chunks[2].Heading);
        Assert.Contains("Beta.", chunks[2].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_IgnoresHashLinesInsideCodeFences()
    {
        var md = "# Doc\n\nBefore.\n\n```bash\n# this is a comment, not a heading\necho hi\n```\n\nAfter.";

        var (_, chunks) = MarkdownChunker.Chunk("f.md", md);

        var chunk = Assert.Single(chunks);
        Assert.Contains("# this is a comment", chunk.Content, StringComparison.Ordinal);
        Assert.Contains("After.", chunk.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_SplitsOversizedSectionsAtParagraphs()
    {
        var paragraph = new string('x', 900);
        var md = $"# Doc\n\n{paragraph}\n\n{paragraph}\n\n{paragraph}";

        var (_, chunks) = MarkdownChunker.Chunk("f.md", md);

        Assert.True(chunks.Count > 1, "oversized section should split");
        Assert.All(chunks, c => Assert.True(c.Content.Length <= MarkdownChunker.MaxChunkChars));
    }

    [Fact]
    public void Chunk_HardSplitsSingleGiantParagraph()
    {
        var md = "# Doc\n\n" + new string('y', MarkdownChunker.MaxChunkChars * 3);

        var (_, chunks) = MarkdownChunker.Chunk("f.md", md);

        Assert.Equal(3, chunks.Count);
    }

    [Fact]
    public void Chunk_EmptyFileYieldsNoChunks()
    {
        var (title, chunks) = MarkdownChunker.Chunk("empty.md", "");

        Assert.Equal("empty", title);
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_OrdinalsAreSequential()
    {
        var md = "# A\n\none\n\n## B\n\ntwo\n\n## C\n\nthree";

        var (_, chunks) = MarkdownChunker.Chunk("f.md", md);

        Assert.Equal([0, 1, 2], chunks.Select(c => c.Ordinal).ToArray());
    }
}
