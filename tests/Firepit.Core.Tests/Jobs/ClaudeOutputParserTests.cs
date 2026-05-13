using Firepit.Core.Jobs;

namespace Firepit.Core.Tests.Jobs;

public class ClaudeOutputParserTests
{
    [Fact]
    public void ExtractsTokensCostAndResult_FromTypicalShape()
    {
        const string json = """
        {
          "type": "result",
          "subtype": "success",
          "is_error": false,
          "result": "0 new important mails.",
          "total_cost_usd": 0.0042,
          "usage": {
            "input_tokens": 1234,
            "output_tokens": 56
          }
        }
        """;

        var md = ClaudeOutputParser.TryParse(json);

        Assert.NotNull(md);
        Assert.Equal(1234, md!.TokensInput);
        Assert.Equal(56, md.TokensOutput);
        Assert.Equal(0.0042m, md.CostUsd);
        Assert.Equal("0 new important mails.", md.AssistantMessage);
    }

    [Fact]
    public void ExtractsCustomSummaryField_WhenPresent()
    {
        const string json = """
        { "result": "long output", "summary": "2 mails archived, 1 needs reply" }
        """;
        var md = ClaudeOutputParser.TryParse(json);
        Assert.Equal("2 mails archived, 1 needs reply", md!.Summary);
    }

    [Fact]
    public void HandlesArrayWrapper_PicksLastResultEntry()
    {
        const string json = """
        [
          { "type": "assistant", "content": "thinking..." },
          { "type": "result", "result": "done", "total_cost_usd": 0.01 }
        ]
        """;
        var md = ClaudeOutputParser.TryParse(json);
        Assert.Equal("done", md!.AssistantMessage);
        Assert.Equal(0.01m, md.CostUsd);
    }

    [Fact]
    public void ReturnsNullForMalformedInput()
    {
        Assert.Null(ClaudeOutputParser.TryParse(""));
        Assert.Null(ClaudeOutputParser.TryParse("   "));
        Assert.Null(ClaudeOutputParser.TryParse("not json at all"));
        Assert.Null(ClaudeOutputParser.TryParse("{ malformed"));
    }

    [Fact]
    public void MissingFieldsBecomeNullsButObjectStillReturned()
    {
        var md = ClaudeOutputParser.TryParse("""{ "foo": "bar" }""");
        Assert.NotNull(md);
        Assert.Null(md!.TokensInput);
        Assert.Null(md.CostUsd);
        Assert.Null(md.AssistantMessage);
    }
}
