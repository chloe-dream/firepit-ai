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
    public void BuildWrappedArguments_KeepOpenOnly_WrapsWithConditionalPause()
    {
        var args = ShellCommandLauncher.BuildWrappedArguments("dotnet", new[] { "build" }, workingDirectory: null, keepOpenOnError: true);
        Assert.Equal("/d /c dotnet build & if errorlevel 1 pause", args);
    }

    [Fact]
    public void BuildWrappedArguments_PlainWrap_NoCwdNoPause()
    {
        var args = ShellCommandLauncher.BuildWrappedArguments("dotnet", new[] { "build" }, workingDirectory: null, keepOpenOnError: false);
        Assert.Equal("/d /c dotnet build", args);
    }

    [Fact]
    public void BuildWrappedArguments_PreservesInnerQuotingVerbatim()
    {
        // The user's own quoting must reach cmd untouched — we add no outer quotes.
        var args = ShellCommandLauncher.BuildWrappedArguments("pwsh", new[] { "-c", "\"dotnet test\"" }, workingDirectory: null, keepOpenOnError: true);
        Assert.Equal("/d /c pwsh -c \"dotnet test\" & if errorlevel 1 pause", args);
    }

    [Fact]
    public void BuildWrappedArguments_WithWorkingDirectory_PrependsCdShim()
    {
        // The elevated-command fix: cd into the project root before the command
        // so relative paths resolve (ShellExecute drops runas spawns in system32).
        var args = ShellCommandLauncher.BuildWrappedArguments(
            "powershell",
            new[] { "-NoExit", "-Command", "./tools/capture-off.ps1" },
            workingDirectory: @"D:\repos\bumblebeee",
            keepOpenOnError: false);
        Assert.Equal("/d /c cd /d \"D:\\repos\\bumblebeee\" && powershell -NoExit -Command ./tools/capture-off.ps1", args);
    }

    [Fact]
    public void BuildWrappedArguments_CwdAndKeepOpen_Compose()
    {
        var args = ShellCommandLauncher.BuildWrappedArguments("dotnet", new[] { "build" }, workingDirectory: @"C:\proj", keepOpenOnError: true);
        Assert.Equal("/d /c cd /d \"C:\\proj\" && dotnet build & if errorlevel 1 pause", args);
    }

    [Fact]
    public void BuildWrappedArguments_TrimsTrailingSeparatorButKeepsDriveRoot()
    {
        var trimmed = ShellCommandLauncher.BuildWrappedArguments("x", null, workingDirectory: @"D:\repos\proj\", keepOpenOnError: false);
        Assert.Equal("/d /c cd /d \"D:\\repos\\proj\" && x", trimmed);

        var driveRoot = ShellCommandLauncher.BuildWrappedArguments("x", null, workingDirectory: @"C:\", keepOpenOnError: false);
        Assert.Equal("/d /c cd /d \"C:\\\" && x", driveRoot);
    }
}
