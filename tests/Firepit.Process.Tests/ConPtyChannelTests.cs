using System.Text;
using Firepit.Core.Terminal;
using Firepit.Process;

namespace Firepit.Process.Tests;

public class ConPtyChannelTests
{
    [Fact]
    public async Task Cmd_EchoCommand_ProducesExpectedOutput()
    {
        await using var channel = await ConPtyLauncher.SpawnAsync(
            executable: "cmd.exe",
            arguments: ["/c", "echo hi"],
            workingDirectory: Environment.SystemDirectory,
            environmentOverrides: null,
            initialSize: TerminalSize.Default);

        Assert.True(channel.Pid > 0);

        var output = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await foreach (var chunk in channel.ReadAsync(cts.Token))
            {
                output.Append(Encoding.UTF8.GetString(chunk.Span));
                if (output.ToString().Contains("hi"))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // assertion below carries the failure message
        }

        Assert.Contains("hi", output.ToString());
    }
}
