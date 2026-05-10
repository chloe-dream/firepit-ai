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
}
