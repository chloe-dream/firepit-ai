using Firepit.Core.ProjectConfig;

namespace Firepit.Core.Tests.ProjectConfig;

public class ShellCommandLauncherTests
{
    [Fact]
    public void BuildInnerCommandLine_JoinsArgsWithSpaces()
    {
        var inner = ShellCommandLauncher.BuildInnerCommandLine("npm", new[] { "run", "dev" });
        Assert.Equal("npm run dev", inner);
    }

    [Fact]
    public void BuildInnerCommandLine_NoArgs_ReturnsBareCommand()
    {
        Assert.Equal("build.cmd", ShellCommandLauncher.BuildInnerCommandLine("build.cmd", null));
        Assert.Equal("build.cmd", ShellCommandLauncher.BuildInnerCommandLine("build.cmd", Array.Empty<string>()));
    }

    [Fact]
    public void BuildKeepOpenOnErrorArguments_WrapsWithConditionalPause()
    {
        var args = ShellCommandLauncher.BuildKeepOpenOnErrorArguments("dotnet", new[] { "build" });
        Assert.Equal("/d /c dotnet build & if errorlevel 1 pause", args);
    }

    [Fact]
    public void BuildKeepOpenOnErrorArguments_PreservesInnerQuotingVerbatim()
    {
        // The user's own quoting must reach cmd untouched — we add no outer quotes.
        var args = ShellCommandLauncher.BuildKeepOpenOnErrorArguments("pwsh", new[] { "-c", "\"dotnet test\"" });
        Assert.Equal("/d /c pwsh -c \"dotnet test\" & if errorlevel 1 pause", args);
    }
}
