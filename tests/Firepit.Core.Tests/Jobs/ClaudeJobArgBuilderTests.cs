using Firepit.Core.Jobs;

namespace Firepit.Core.Tests.Jobs;

public class ClaudeJobArgBuilderTests
{
    private static JobRunRequest MakeRequest(
        string prompt = "/check-mails",
        IReadOnlyList<string>? allowedTools = null,
        int? maxTurns = null,
        decimal? maxBudgetUsd = null,
        bool skipPermissions = false) =>
        new(
            ProjectPath: @"C:\projects\demo",
            ProjectName: "demo",
            JobName: "check-mails",
            Prompt: prompt,
            Trigger: JobTrigger.Scheduled,
            AllowedTools: allowedTools,
            MaxTurns: maxTurns,
            MaxBudgetUsd: maxBudgetUsd,
            SkipPermissions: skipPermissions);

    [Fact]
    public void MinimalRequest_ProducesPromptAndJsonOutputOnly()
    {
        var argv = ClaudeJobArgBuilder.Build(MakeRequest());
        Assert.Equal(new[] { "-p", "/check-mails", "--output-format", "json" }, argv);
    }

    [Fact]
    public void AllowedTools_JoinedByComma()
    {
        var argv = ClaudeJobArgBuilder.Build(MakeRequest(allowedTools: new[] { "Read", "Bash", "Edit" }));
        Assert.Contains("--allowed-tools", argv);
        var i = argv.ToList().IndexOf("--allowed-tools");
        Assert.Equal("Read,Bash,Edit", argv[i + 1]);
    }

    [Fact]
    public void EmptyAllowedTools_OmitsTheFlag()
    {
        var argv = ClaudeJobArgBuilder.Build(MakeRequest(allowedTools: Array.Empty<string>()));
        Assert.DoesNotContain("--allowed-tools", argv);
    }

    [Fact]
    public void MaxTurns_RenderedAsSeparateArgs()
    {
        var argv = ClaudeJobArgBuilder.Build(MakeRequest(maxTurns: 7));
        var idx = argv.ToList().IndexOf("--max-turns");
        Assert.True(idx >= 0);
        Assert.Equal("7", argv[idx + 1]);
    }

    [Fact]
    public void MaxBudget_UsesInvariantCulture()
    {
        // 0.25 must serialize with a dot, not a comma, regardless of CurrentCulture.
        var prior = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var argv = ClaudeJobArgBuilder.Build(MakeRequest(maxBudgetUsd: 0.25m));
            var idx = argv.ToList().IndexOf("--max-budget-usd");
            Assert.True(idx >= 0);
            Assert.Equal("0.25", argv[idx + 1]);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prior;
        }
    }

    [Fact]
    public void SkipPermissions_AddsDangerousFlagOnlyWhenTrue()
    {
        Assert.DoesNotContain("--dangerously-skip-permissions",
            ClaudeJobArgBuilder.Build(MakeRequest(skipPermissions: false)));
        Assert.Contains("--dangerously-skip-permissions",
            ClaudeJobArgBuilder.Build(MakeRequest(skipPermissions: true)));
    }

    [Fact]
    public void Render_QuotesArgsContainingSpaces()
    {
        var line = ClaudeJobArgBuilder.Render("claude", new[] { "-p", "do the thing", "--output-format", "json" });
        Assert.Equal("claude -p \"do the thing\" --output-format json", line);
    }
}
