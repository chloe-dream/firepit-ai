namespace Firepit.Mcp;

/// <summary>
/// Surface the MCP host calls into the running Firepit GUI through.
/// MainWindow implements this. All members are async — the host always
/// awaits. Implementations are responsible for marshalling onto the WPF
/// dispatcher when they touch UI state.
/// </summary>
public interface IMcpBackend
{
    Task<IReadOnlyList<ProjectInfo>>  ListProjectsAsync();
    Task<IReadOnlyList<SessionInfo>>  ListSessionsAsync();

    /// <summary>Effective settings as a JSON object string with secret values redacted.</summary>
    Task<string>                      GetRedactedSettingsAsync();

    Task<ToolCallResult>              OpenTabAsync(string projectName, bool resume);
    Task<ToolCallResult>              FocusTabAsync(string projectName);
    Task<ToolCallResult>              CloseTabAsync(string projectName);
    Task<ToolCallResult>              ReloadAsync(string projectName, bool restart);

    /// <summary>Phase 5: write an inbox file under &lt;toProject&gt;/.firepit/inbox/.</summary>
    Task<InboxWriteResult>            SendInboxAsync(string fromProject, string toProject,
                                                     string subject, string body, string priority);

    /// <summary>List pending (un-processed) inbox messages for a project — top-level
    /// .md files under &lt;projectPath&gt;/.firepit/inbox/, with frontmatter parsed.</summary>
    Task<InboxListResult>             ListInboxAsync(string projectName);

    /// <summary>Move one inbox message from &lt;projectPath&gt;/.firepit/inbox/&lt;id&gt;
    /// into the sibling processed/ subfolder. Idempotent — already-processed
    /// (or missing) files return Ok=false with a Message.</summary>
    Task<ToolCallResult>              CompleteInboxAsync(string projectName, string id);

    /// <summary>Append (or replace by name) a toolbar command in
    /// &lt;projectPath&gt;/.firepit/config.json. The existing FileSystemWatcher
    /// picks up the change and hot-reloads the toolbar via
    /// SessionTab.RefreshFromConfigAsync — no extra plumbing needed here.</summary>
    Task<ToolCallResult>              AddProjectCommandAsync(string projectName, AddCommandSpec spec);
}
