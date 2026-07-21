namespace Firepit.Knowledge.Tests;

/// <summary>
/// Service-level tests. The real EmbeddingService has no model on disk in
/// the temp data root, so everything here exercises the degraded (FTS-only)
/// path — which is exactly what a fresh install looks like before the
/// background download finishes.
/// </summary>
public sealed class KnowledgeServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _projectA;
    private readonly string _global;
    private readonly KnowledgeService _service;

    public KnowledgeServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "firepit-knowledge-tests", Guid.NewGuid().ToString("N"));
        _projectA = Path.Combine(_root, "project-a");
        _global = Path.Combine(_root, ".firepit");
        Directory.CreateDirectory(_projectA);
        Directory.CreateDirectory(_global);

        _service = new KnowledgeService(modelDataRoot: Path.Combine(_root, "data"));
        _service.SyncScopes(
        [
            new KnowledgeScopeRegistration("project-a", _projectA),
            new KnowledgeScopeRegistration(KnowledgeService.GlobalScopeName, _global),
        ]);
    }

    public void Dispose()
    {
        _service.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task AddDocument_ThenSearch_FindsItInItsScope()
    {
        await _service.AddDocumentAsync(
            "project-a", "ConPTY resize quirks", "Resizing mid-stream tears output.");

        var result = await _service.SearchAsync("conpty resize", ["project-a"], limit: 5);

        Assert.True(result.Degraded); // no model on disk → FTS-only
        var hit = Assert.Single(result.Hits);
        Assert.Equal("project-a", hit.Scope);
        Assert.Equal("conpty-resize-quirks.md", hit.Path);
    }

    [Fact]
    public async Task AddDocument_WritesSluggedFileUnderDotFirepit()
    {
        var doc = await _service.AddDocumentAsync(
            "project-a", "C# Lock vs Monitor!", "Prefer the Lock type on .NET 9+.");

        Assert.Equal("c-lock-vs-monitor.md", doc.Path);
        var expected = Path.Combine(_projectA, ".firepit", "knowledge", "c-lock-vs-monitor.md");
        Assert.True(File.Exists(expected));
        Assert.StartsWith("# C# Lock vs Monitor!", File.ReadAllText(expected), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddDocument_DuplicateTitleGetsSuffix()
    {
        var first = await _service.AddDocumentAsync("project-a", "Same title", "one");
        var second = await _service.AddDocumentAsync("project-a", "Same title", "two");

        Assert.Equal("same-title.md", first.Path);
        Assert.Equal("same-title-2.md", second.Path);
    }

    [Fact]
    public async Task Search_MergesProjectAndGlobalScopes()
    {
        await _service.AddDocumentAsync("project-a", "Project fact", "The tab bar uses WPF drag reorder.");
        await _service.AddDocumentAsync(KnowledgeService.GlobalScopeName, "Global fact", "The tab key indents code.");

        var result = await _service.SearchAsync("tab", null, limit: 10);

        Assert.Equal(2, result.Hits.Count);
        Assert.Contains(result.Hits, h => h.Scope == "project-a");
        Assert.Contains(result.Hits, h => h.Scope == KnowledgeService.GlobalScopeName);
    }

    [Fact]
    public async Task GetDocument_ReturnsFullContent()
    {
        await _service.AddDocumentAsync("project-a", "Round trip", "Full body survives.");

        var doc = await _service.GetDocumentAsync("project-a", "round-trip.md");

        Assert.NotNull(doc);
        Assert.Equal("Round trip", doc.Title);
        Assert.Contains("Full body survives.", doc.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDocument_BlocksPathTraversal()
    {
        File.WriteAllText(Path.Combine(_projectA, "secret.txt"), "not knowledge");

        var doc = await _service.GetDocumentAsync("project-a", "../../secret.txt");

        Assert.Null(doc);
    }

    [Fact]
    public async Task GetDocument_UnknownScopeThrowsArgument()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.GetDocumentAsync("no-such-scope", "x.md"));
    }

    [Fact]
    public async Task SyncScopes_DroppedScopeStopsBeingSearched()
    {
        await _service.AddDocumentAsync("project-a", "Vanishing", "scoped knowledge");

        _service.SyncScopes([new KnowledgeScopeRegistration(KnowledgeService.GlobalScopeName, _global)]);
        var result = await _service.SearchAsync("vanishing scoped", null, limit: 5);

        Assert.Empty(result.Hits);
    }

    [Fact]
    public async Task Search_UnknownScopeNamesAreIgnored()
    {
        await _service.AddDocumentAsync(KnowledgeService.GlobalScopeName, "Known", "searchable text");

        var result = await _service.SearchAsync(
            "searchable", ["ghost-project", KnowledgeService.GlobalScopeName], limit: 5);

        var hit = Assert.Single(result.Hits);
        Assert.Equal(KnowledgeService.GlobalScopeName, hit.Scope);
    }

    [Fact]
    public async Task UpdateDocument_ReplacesContent_OldPhrasingStopsMatching()
    {
        await _service.AddDocumentAsync(
            "project-a", "Deploy target", "The deploy target is staging-seven.");

        var updated = await _service.UpdateDocumentAsync(
            "project-a", "deploy-target.md", "The deploy target is prod-two.");

        Assert.NotNull(updated);
        var doc = await _service.GetDocumentAsync("project-a", "deploy-target.md");
        Assert.NotNull(doc);
        Assert.Contains("prod-two", doc.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("staging-seven", doc.Content, StringComparison.Ordinal);

        var newHits = await _service.SearchAsync("prod-two", ["project-a"], limit: 5);
        Assert.Single(newHits.Hits);
        var oldHits = await _service.SearchAsync("staging-seven", ["project-a"], limit: 5);
        Assert.Empty(oldHits.Hits);
    }

    [Fact]
    public async Task UpdateDocument_MissingOrTraversalPathReturnsNull()
    {
        Assert.Null(await _service.UpdateDocumentAsync("project-a", "nope.md", "content"));
        Assert.Null(await _service.UpdateDocumentAsync("project-a", "../../evil.md", "content"));
    }

    [Fact]
    public async Task DeleteDocument_RemovesFileAndSearchHits()
    {
        await _service.AddDocumentAsync("project-a", "Doomed doc", "Contains zanzibar facts.");

        Assert.True(await _service.DeleteDocumentAsync("project-a", "doomed-doc.md"));

        Assert.False(File.Exists(Path.Combine(_projectA, ".firepit", "knowledge", "doomed-doc.md")));
        var result = await _service.SearchAsync("zanzibar", ["project-a"], limit: 5);
        Assert.Empty(result.Hits);
        // Second delete of the same doc: gone is gone.
        Assert.False(await _service.DeleteDocumentAsync("project-a", "doomed-doc.md"));
    }

    [Fact]
    public async Task AddDocument_Pinned_LandsInPinnedDigest()
    {
        await _service.AddDocumentAsync(
            "project-a", "Reflex rule", "Always use they-them by default.", pinned: true);
        await _service.AddDocumentAsync(
            "project-a", "Plain fact", "The build takes four minutes.");

        var file = Path.Combine(_projectA, ".firepit", "knowledge", "reflex-rule.md");
        Assert.StartsWith("---\npin: true\n---", File.ReadAllText(file), StringComparison.Ordinal);

        var digest = File.ReadAllText(Path.Combine(_projectA, ".firepit", "knowledge-pinned.md"));
        Assert.Contains("Always use they-them", digest, StringComparison.Ordinal);
        Assert.DoesNotContain("four minutes", digest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateDocument_KeepsPinUnlessToldOtherwise()
    {
        await _service.AddDocumentAsync(
            "project-a", "Sticky rule", "Original reflex.", pinned: true);
        var file = Path.Combine(_projectA, ".firepit", "knowledge", "sticky-rule.md");
        var digestPath = Path.Combine(_projectA, ".firepit", "knowledge-pinned.md");

        // Content update with no pin argument — pin survives.
        await _service.UpdateDocumentAsync("project-a", "sticky-rule.md", "Corrected reflex.");
        var text = File.ReadAllText(file);
        Assert.StartsWith("---\npin: true\n---", text, StringComparison.Ordinal);
        Assert.Contains("Corrected reflex.", text, StringComparison.Ordinal);
        Assert.Contains("Corrected reflex.", File.ReadAllText(digestPath), StringComparison.Ordinal);

        // Explicit unpin — frontmatter and digest entry disappear.
        await _service.UpdateDocumentAsync(
            "project-a", "sticky-rule.md", "Corrected reflex.", pinned: false);
        text = File.ReadAllText(file);
        Assert.DoesNotContain("pin: true", text, StringComparison.Ordinal);
        var digest = File.ReadAllText(digestPath);
        Assert.DoesNotContain("Corrected reflex.", digest, StringComparison.Ordinal);
        Assert.Contains("No pinned documents yet", digest, StringComparison.Ordinal);
    }
}
