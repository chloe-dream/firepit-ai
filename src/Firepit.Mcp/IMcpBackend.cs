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

    /// <summary>Hard-delete an inbox message file (not the processed-move).
    /// Used by the Inbox wizard's Delete action. Idempotent — missing files
    /// return Ok=true with a "nothing to delete" note. Same path-traversal
    /// guard as Complete: only bare filenames accepted.</summary>
    Task<ToolCallResult>              DeleteInboxMessageAsync(string projectName, string id);

    /// <summary>Append (or replace by name) a toolbar command in
    /// &lt;projectPath&gt;/.firepit/config.json. The existing FileSystemWatcher
    /// picks up the change and hot-reloads the toolbar via
    /// SessionTab.RefreshFromConfigAsync — no extra plumbing needed here.</summary>
    Task<ToolCallResult>              AddProjectCommandAsync(string projectName, AddCommandSpec spec);

    /// <summary>Return the current toolbar commands list for a project (empty if
    /// none configured). Read-only; lets agents discover what's already wired
    /// without parsing the JSONC file themselves.</summary>
    Task<CommandListResult>           ListProjectCommandsAsync(string projectName);

    /// <summary>Remove a toolbar command by name (case-insensitive). Idempotent —
    /// if the name doesn't exist, returns Ok with a "not found" message. Same
    /// hot-reload path as Add.</summary>
    Task<ToolCallResult>              RemoveProjectCommandAsync(string projectName, string commandName);
}
