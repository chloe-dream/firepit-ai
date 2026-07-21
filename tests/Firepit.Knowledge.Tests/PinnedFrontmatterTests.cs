using Firepit.Knowledge.Indexing;

namespace Firepit.Knowledge.Tests;

public sealed class PinnedFrontmatterTests
{
    [Fact]
    public void IsPinned_TrueOnlyForPinTrueInFrontmatter()
    {
        Assert.True(PinnedFrontmatter.IsPinned("---\npin: true\n---\n# Doc\n"));
        Assert.True(PinnedFrontmatter.IsPinned("---\nPIN: TRUE\n---\nbody"));
        Assert.False(PinnedFrontmatter.IsPinned("---\npin: false\n---\nbody"));
        Assert.False(PinnedFrontmatter.IsPinned("---\ntags: a\n---\nbody"));
        // `pin: true` in the body is content, not metadata.
        Assert.False(PinnedFrontmatter.IsPinned("# Doc\n\npin: true\n"));
        // Unterminated frontmatter doesn't count (mirrors MarkdownChunker).
        Assert.False(PinnedFrontmatter.IsPinned("---\npin: true\nbody"));
    }

    [Fact]
    public void SetPinned_AddsMinimalBlockWhenNoneExists()
    {
        var result = PinnedFrontmatter.SetPinned("# Doc\n\nBody.\n", true);

        Assert.StartsWith("---\npin: true\n---\n\n# Doc", result, StringComparison.Ordinal);
        Assert.True(PinnedFrontmatter.IsPinned(result));
    }

    [Fact]
    public void SetPinned_PreservesOtherFrontmatterKeys()
    {
        var pinnedDoc = PinnedFrontmatter.SetPinned("---\ntags: a, b\n---\n\nBody.\n", true);
        Assert.True(PinnedFrontmatter.IsPinned(pinnedDoc));
        Assert.Contains("tags: a, b", pinnedDoc, StringComparison.Ordinal);

        var unpinnedDoc = PinnedFrontmatter.SetPinned(pinnedDoc, false);
        Assert.False(PinnedFrontmatter.IsPinned(unpinnedDoc));
        Assert.Contains("tags: a, b", unpinnedDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void SetPinned_False_DropsBlockWhenPinWasTheOnlyKey()
    {
        var result = PinnedFrontmatter.SetPinned("---\npin: true\n---\n\n# Doc\n\nBody.\n", false);

        Assert.StartsWith("# Doc", result, StringComparison.Ordinal);
        Assert.False(PinnedFrontmatter.IsPinned(result));
    }

    [Fact]
    public void SetPinned_IsIdempotent()
    {
        var once = PinnedFrontmatter.SetPinned("Body.", true);
        Assert.Equal(once, PinnedFrontmatter.SetPinned(once, true));

        Assert.Equal("Body.", PinnedFrontmatter.SetPinned("Body.", false));
    }

    [Fact]
    public void StripFrontmatterBlock_RemovesOnlyTheLeadingBlock()
    {
        Assert.Equal(
            "# Doc\nBody.",
            PinnedFrontmatter.StripFrontmatterBlock("---\npin: true\n---\n# Doc\nBody."));
        Assert.Equal("plain", PinnedFrontmatter.StripFrontmatterBlock("plain"));
    }
}
