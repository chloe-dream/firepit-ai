namespace Firepit.Core.ProjectConfig;

/// <summary>
/// Watches a project's .firepit/config.json and fires when the parsed content
/// changes. Implementations debounce filesystem events and skip parse failures
/// (logged, swallowed) so partial saves from editors don't crash the consumer.
/// </summary>
public interface IProjectConfigWatcher : IDisposable
{
    /// <summary>The project path being watched.</summary>
    string ProjectPath { get; }

    /// <summary>Fires with the new parsed config after a settled change.</summary>
    event EventHandler<ProjectConfig>? ConfigChanged;

    /// <summary>Begin watching. Idempotent.</summary>
    void Start();

    /// <summary>Stop watching. Idempotent.</summary>
    void Stop();
}
