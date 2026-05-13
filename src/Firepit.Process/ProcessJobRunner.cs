using System.Diagnostics;
using System.IO;
using System.Text;
using Firepit.Core.Jobs;

namespace Firepit.Process;

/// <summary>
/// Spawns headless Claude runs via <see cref="System.Diagnostics.Process"/>
/// — no ConPTY, no terminal. stdout / stderr are captured as text. Timeout
/// kills the whole child tree (Process.Kill with entireProcessTree=true).
///
/// stdout above <see cref="JobRunOutcome.InlineStdoutLimitBytes"/> is spilled
/// to a sibling file under the runs directory rather than held in memory
/// or stuffed into the JSON record. <c>StdoutInline</c> always contains the
/// first 1 MiB so the history UI has something to show without reading the
/// spillover.
/// </summary>
public sealed class ProcessJobRunner : IJobRunner
{
    private readonly Func<JobRunRequest, string?> _spilloverPathFactory;

    /// <param name="spilloverPathFactory">
    /// Given the full request, returns an absolute path where oversized stdout
    /// should be written, or <c>null</c> to discard the excess silently. The
    /// default factory writes to
    /// <c>&lt;projectPath&gt;/.firepit/runs/&lt;jobName&gt;/stdout-&lt;guid&gt;.log</c>.
    /// </param>
    public ProcessJobRunner(Func<JobRunRequest, string?>? spilloverPathFactory = null)
    {
        _spilloverPathFactory = spilloverPathFactory ?? DefaultSpilloverFactory;
    }

    public async Task<JobRunOutcome> RunAsync(JobRunRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var argv = ClaudeJobArgBuilder.Build(request);
        var commandLine = ClaudeJobArgBuilder.Render(request.ClaudeExecutable, argv);
        var startedAt = DateTimeOffset.UtcNow;

        using var process = new System.Diagnostics.Process
        {
            StartInfo = BuildStartInfo(request, argv),
            EnableRaisingEvents = true,
        };

        var stdoutBuilder = new BoundedStringBuilder(JobRunOutcome.InlineStdoutLimitBytes);
        var stderrBuilder = new BoundedStringBuilder(JobRunOutcome.InlineStdoutLimitBytes);
        var spilloverBuffer = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdoutBuilder.AppendLine(e.Data, overflow =>
            {
                if (overflow is not null) spilloverBuffer.AppendLine(overflow);
            });
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderrBuilder.AppendLine(e.Data, _ => { /* stderr overflow dropped */ });
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            var endedAt = DateTimeOffset.UtcNow;
            return new JobRunOutcome(
                Status: JobRunStatus.Failure,
                ExitCode: null,
                StartedAt: startedAt,
                EndedAt: endedAt,
                CommandLine: commandLine,
                StdoutInline: "",
                StdoutTruncated: false,
                StdoutSpilloverPath: null,
                Stderr: $"failed to spawn '{request.ClaudeExecutable}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var didTimeout = false;
        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            didTimeout = true;
            TryKill(process);
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* best effort */ }
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        // Drain any stdout buffered after the exit signal but before
        // OutputDataReceived flushed its tail. WaitForExit (without timeout)
        // forces the async readers to drain.
        process.WaitForExit();

        var endedAtFinal = DateTimeOffset.UtcNow;
        var stdoutInline = stdoutBuilder.ToString();
        var stdoutTruncated = stdoutBuilder.Truncated;
        var spilloverPath = stdoutTruncated ? WriteSpillover(request, stdoutInline, spilloverBuffer.ToString()) : null;

        var status = didTimeout
            ? JobRunStatus.Timeout
            : process.ExitCode == 0 ? JobRunStatus.Success : JobRunStatus.Failure;

        var metadata = status == JobRunStatus.Success ? ClaudeOutputParser.TryParse(stdoutInline) : null;

        return new JobRunOutcome(
            Status: status,
            ExitCode: didTimeout ? null : process.ExitCode,
            StartedAt: startedAt,
            EndedAt: endedAtFinal,
            CommandLine: commandLine,
            StdoutInline: stdoutInline,
            StdoutTruncated: stdoutTruncated,
            StdoutSpilloverPath: spilloverPath,
            Stderr: stderrBuilder.ToString(),
            ClaudeMetadata: metadata);
    }

    private static ProcessStartInfo BuildStartInfo(JobRunRequest request, IReadOnlyList<string> argv)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.ClaudeExecutable,
            WorkingDirectory = request.ProjectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in argv) psi.ArgumentList.Add(a);

        psi.Environment["FIREPIT_PROJECT_NAME"] = request.ProjectName;
        psi.Environment["FIREPIT_JOB_NAME"]     = request.JobName;
        psi.Environment["FIREPIT_JOB_TRIGGER"]  = request.Trigger.ToString().ToLowerInvariant();

        if (request.EnvOverrides is not null)
        {
            foreach (var (key, value) in request.EnvOverrides)
            {
                if (value is null) psi.Environment.Remove(key);
                else psi.Environment[key] = value;
            }
        }
        return psi;
    }

    private static void TryKill(System.Diagnostics.Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* race with normal exit — acceptable */ }
    }

    private string? WriteSpillover(JobRunRequest request, string inlinePortion, string overflowPortion)
    {
        var path = _spilloverPathFactory(request);
        if (path is null) return null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, inlinePortion + overflowPortion);
            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? DefaultSpilloverFactory(JobRunRequest request)
    {
        // Caller can override for tests / alternate layouts. Production: drop
        // the spillover next to the run's JSON record so the history UI can
        // find both without extra plumbing.
        var dir = Path.Combine(request.ProjectPath, ".firepit", "runs", SanitizeJobName(request.JobName));
        return Path.Combine(dir, $"stdout-{Guid.NewGuid():N}.log");
    }

    private static string SanitizeJobName(string jobName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Concat(jobName.Select(ch => Array.IndexOf(invalid, ch) >= 0 ? '_' : ch));
        return clean.Length == 0 ? "_" : clean;
    }
}

/// <summary>
/// Append-only string builder with a hard byte ceiling. Once the ceiling is
/// crossed every subsequent <c>AppendLine</c> calls the overflow sink instead
/// of growing the inline buffer. UTF-8 byte length is approximated as char
/// count + 1 per line break — good enough for the 1 MiB cap; we err on the
/// safe side by counting chars as worst-case 4 bytes when crossing the line.
/// </summary>
internal sealed class BoundedStringBuilder
{
    private readonly StringBuilder _builder = new();
    private readonly int _maxBytes;
    private int _approxBytes;

    public BoundedStringBuilder(int maxBytes)
    {
        _maxBytes = maxBytes;
    }

    public bool Truncated { get; private set; }

    public void AppendLine(string line, Action<string?> overflowSink)
    {
        ArgumentNullException.ThrowIfNull(line);
        var costBytes = line.Length + 1;
        if (Truncated || _approxBytes + costBytes > _maxBytes)
        {
            Truncated = true;
            overflowSink(line);
            return;
        }
        _builder.AppendLine(line);
        _approxBytes += costBytes;
        overflowSink(null);
    }

    public override string ToString() => _builder.ToString();
}
