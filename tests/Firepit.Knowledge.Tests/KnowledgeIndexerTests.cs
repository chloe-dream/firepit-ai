using Firepit.Knowledge.Indexing;
using Firepit.Knowledge.Search;
using Firepit.Knowledge.Store;

namespace Firepit.Knowledge.Tests;

/// <summary>
/// Integration tests against real SQLite + sqlite-vec (native lib ships via
/// the NuGet package). Each test gets a fresh temp knowledge dir + db.
/// </summary>
public sealed class KnowledgeIndexerTests : IDisposable
{
    private readonly string _root;
    private readonly string _knowledgeDir;
    private readonly string _dbPath;
    private readonly FakeEmbeddingService _embeddings = new();

    public KnowledgeIndexerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "firepit-knowledge-tests", Guid.NewGuid().ToString("N"));
        _knowledgeDir = Path.Combine(_root, ".firepit", "knowledge");
        _dbPath = Path.Combine(_root, ".firepit", "knowledge.db");
        Directory.CreateDirectory(_knowledgeDir);
    }

    public void Dispose()
    {
        // SQLite pools connections per file; clear so the temp dir deletes.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private KnowledgeStore NewStore() => new(_knowledgeDir, _dbPath);

    private void WriteDoc(string name, string content) =>
        File.WriteAllText(Path.Combine(_knowledgeDir, name), content);

    [Fact]
    public async Task Reindex_IndexesFiles_SearchFindsThemHybrid()
    {
        WriteDoc("conpty.md", "# ConPTY resize quirks\n\nResizing the pseudo console mid-stream tears output.");
        WriteDoc("sqlite.md", "# SQLite WAL mode\n\nWrite-ahead logging lets readers run beside one writer.");

        var store = NewStore();
        var stats = await new KnowledgeIndexer(store, _embeddings).ReindexAsync();

        Assert.Equal(2, stats.Indexed);
        Assert.Equal(0, stats.PendingEmbedding);

        var search = new KnowledgeSearch(_embeddings);
        var result = await search.SearchAsync(store, "test", "ConPTY resize", limit: 5);

        Assert.False(result.Degraded);
        Assert.Equal("conpty.md", result.Hits[0].Path);
        Assert.Equal("ConPTY resize quirks", result.Hits[0].Title);
        Assert.Equal("test", result.Hits[0].Scope);
    }

    [Fact]
    public async Task Reindex_SkipsUnchangedFiles()
    {
        WriteDoc("a.md", "# Alpha\n\nContent.");
        var store = NewStore();
        var indexer = new KnowledgeIndexer(store, _embeddings);

        var first = await indexer.ReindexAsync();
        var second = await indexer.ReindexAsync();

        Assert.Equal(1, first.Indexed);
        Assert.Equal(0, second.Indexed);
        Assert.Equal(1, second.Unchanged);
    }

    [Fact]
    public async Task Reindex_RemovesDeletedFiles()
    {
        WriteDoc("gone.md", "# Doomed\n\nThis document will be deleted.");
        var store = NewStore();
        var indexer = new KnowledgeIndexer(store, _embeddings);
        await indexer.ReindexAsync();

        File.Delete(Path.Combine(_knowledgeDir, "gone.md"));
        var stats = await indexer.ReindexAsync();

        Assert.Equal(1, stats.Removed);
        var search = new KnowledgeSearch(_embeddings);
        var result = await search.SearchAsync(store, "test", "doomed deleted", limit: 5);
        Assert.Empty(result.Hits);
    }

    [Fact]
    public async Task Reindex_WithoutModel_IndexesFtsOnly_ThenBackfills()
    {
        WriteDoc("a.md", "# Alpha topic\n\nSome distinctive alpha content.");
        var store = NewStore();
        var indexer = new KnowledgeIndexer(store, _embeddings);

        _embeddings.Unavailable = true;
        var degradedStats = await indexer.ReindexAsync();
        Assert.Equal(1, degradedStats.PendingEmbedding);

        // FTS still finds it while the vector side is down.
        var search = new KnowledgeSearch(_embeddings);
        var degradedResult = await search.SearchAsync(store, "test", "distinctive alpha", limit: 5);
        Assert.True(degradedResult.Degraded);
        Assert.Single(degradedResult.Hits);

        // Model shows up → next pass backfills vectors without a file change.
        _embeddings.Unavailable = false;
        var backfill = await indexer.ReindexAsync();
        Assert.Equal(1, backfill.Indexed);
        Assert.Equal(0, backfill.PendingEmbedding);

        var hybridResult = await search.SearchAsync(store, "test", "distinctive alpha", limit: 5);
        Assert.False(hybridResult.Degraded);
        Assert.Single(hybridResult.Hits);
    }

    [Fact]
    public async Task DeletedDb_RebuildsFromMarkdownAlone()
    {
        // ROADMAP M9 acceptance criterion: the DB is a derived cache.
        WriteDoc("truth.md", "# The markdown is the truth\n\nIndexes are disposable.");
        var store = NewStore();
        await new KnowledgeIndexer(store, _embeddings).ReindexAsync();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);

        var freshStore = NewStore();
        var stats = await new KnowledgeIndexer(freshStore, _embeddings).ReindexAsync();
        Assert.Equal(1, stats.Indexed);

        var result = await new KnowledgeSearch(_embeddings)
            .SearchAsync(freshStore, "test", "markdown truth", limit: 5);
        Assert.Single(result.Hits);
    }

    [Fact]
    public async Task Reindex_MissingKnowledgeDirAndNoDb_StaysInert()
    {
        Directory.Delete(_knowledgeDir, recursive: true);
        var store = NewStore();

        var stats = await new KnowledgeIndexer(store, _embeddings).ReindexAsync();

        Assert.Equal(IndexStats.Empty, stats);
        Assert.False(File.Exists(_dbPath));
    }

    [Fact]
    public async Task Search_DedupesChunksToOneHitPerDocument()
    {
        WriteDoc("multi.md",
            "# Multi section\n\nkeyword here.\n\n## Second\n\nkeyword again.\n\n## Third\n\nkeyword once more.");
        var store = NewStore();
        await new KnowledgeIndexer(store, _embeddings).ReindexAsync();

        var result = await new KnowledgeSearch(_embeddings)
            .SearchAsync(store, "test", "keyword", limit: 10);

        Assert.Single(result.Hits);
    }
}
