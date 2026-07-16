using Firepit.Knowledge.Search;

namespace Firepit.Knowledge.Tests;

public class KnowledgeSearchScoringTests
{
    [Fact]
    public void NormaliseAscending_MapsBestToOneWorstToZero()
    {
        var rows = new List<(string Id, double Raw)> { ("best", 1.0), ("mid", 2.0), ("worst", 3.0) };

        var norm = KnowledgeSearch.NormaliseAscending(rows);

        Assert.Equal(1.0, norm["best"], precision: 9);
        Assert.Equal(0.5, norm["mid"], precision: 9);
        Assert.Equal(0.0, norm["worst"], precision: 9);
    }

    [Fact]
    public void NormaliseAscending_AllEqualCollapsesToOne()
    {
        var rows = new List<(string Id, double Raw)> { ("a", 2.0), ("b", 2.0) };

        var norm = KnowledgeSearch.NormaliseAscending(rows);

        Assert.All(norm.Values, v => Assert.Equal(1.0, v, precision: 9));
    }

    [Fact]
    public void MergeScores_WeightsVectorSideHigher()
    {
        // "vecwin" tops the vector list only; "ftswin" tops FTS only. With
        // 70/30 the vector-side winner must outrank the FTS-side winner.
        var vec = new List<(string Id, double Distance)> { ("vecwin", 0.1), ("other", 0.9) };
        var fts = new List<(string Id, double Bm25)> { ("ftswin", -5.0), ("other", -1.0) };

        var merged = KnowledgeSearch.MergeScores(vec, fts, degraded: false).ToList();

        Assert.Equal("vecwin", merged[0].Id);
        Assert.True(
            merged.Single(m => m.Id == "vecwin").Score >
            merged.Single(m => m.Id == "ftswin").Score);
    }

    [Fact]
    public void MergeScores_HitOnBothSidesBeatsSingleSide()
    {
        var vec = new List<(string Id, double Distance)> { ("both", 0.1), ("veconly", 0.1) };
        var fts = new List<(string Id, double Bm25)> { ("both", -5.0), ("ftsonly", -5.0) };

        var merged = KnowledgeSearch.MergeScores(vec, fts, degraded: false).ToList();

        Assert.Equal("both", merged[0].Id);
    }

    [Fact]
    public void MergeScores_DegradedGivesFtsFullWeight()
    {
        var fts = new List<(string Id, double Bm25)> { ("hit", -5.0), ("weak", -1.0) };

        var merged = KnowledgeSearch.MergeScores([], fts, degraded: true).ToList();

        Assert.Equal("hit", merged[0].Id);
        Assert.Equal(1.0, merged[0].Score, precision: 9);
    }
}
