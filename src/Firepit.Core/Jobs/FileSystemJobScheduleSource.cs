using System.IO;
using Firepit.Core.ProjectConfig;

namespace Firepit.Core.Jobs;

/// <summary>
/// Enumerates scheduled jobs by scanning <c>&lt;projectsRoot&gt;/*/.firepit/config.json</c>
/// once per <see cref="Enumerate"/> call. Cheap enough at human-scale project
/// counts (one stat + one JSON parse per project). Hot-reload integration in
/// Phase 5 will call this again after a watcher tick.
///
/// Skips projects with no scheduled jobs and skips entries whose cron
/// expression cannot be parsed — see <see cref="CronEvaluator.TryParse"/>.
/// </summary>
public sealed class FileSystemJobScheduleSource : IJobScheduleSource
{
    private readonly string _projectsRoot;
    private readonly IProjectConfigStore _configStore;
    private readonly Action<string, string>? _warn;

    public FileSystemJobScheduleSource(
        string projectsRoot,
        IProjectConfigStore configStore,
        Action<string, string>? warn = null)
    {
        _projectsRoot = projectsRoot;
        _configStore  = configStore;
        _warn         = warn;
    }

    public IReadOnlyList<JobScheduleEntry> Enumerate()
    {
        var entries = new List<JobScheduleEntry>();
        if (!Directory.Exists(_projectsRoot)) return entries;

        IEnumerable<string> projectDirs;
        try
        {
            projectDirs = Directory.EnumerateDirectories(_projectsRoot, "*", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)        { return entries; }
        catch (UnauthorizedAccessException) { return entries; }

        foreach (var projectPath in projectDirs)
        {
            ProjectConfig.ProjectConfig? config;
            try { config = _configStore.Load(projectPath); }
            catch { continue; }
            if (config?.ScheduledJobs is not { Count: > 0 }) continue;

            var projectName = config.Id ?? Path.GetFileName(projectPath);

            foreach (var job in config.ScheduledJobs)
            {
                if (job.Enabled == false) continue;
                if (!CronEvaluator.TryParse(job.Schedule, out _))
                {
                    _warn?.Invoke(projectName, $"job '{job.Name}': invalid cron '{job.Schedule}' — skipped");
                    continue;
                }
                var tz = CronEvaluator.ResolveTimezone(job.Timezone);
                if (tz is null)
                {
                    _warn?.Invoke(projectName, $"job '{job.Name}': unknown timezone '{job.Timezone}' — skipped");
                    continue;
                }
                entries.Add(new JobScheduleEntry(projectPath, projectName, job, tz));
            }
        }
        return entries;
    }
}
