using System.IO;
using System.Threading.Tasks;
using Firepit.Knowledge;
using Firepit.Mcp;
using Serilog;
using SerilogLoggerFactory = Serilog.Extensions.Logging.SerilogLoggerFactory;

namespace Firepit;

/// <summary>
/// Knowledge subsystem wiring (ROADMAP M9): one KnowledgeService for the app,
/// scopes synced from the project list, plus the IMcpBackend knowledge
/// members. Unlike the other backend members these never marshal onto the
/// dispatcher — KnowledgeService is thread-safe and touches no UI state.
/// </summary>
public partial class MainWindow
{
    private KnowledgeService? _knowledgeService;
    private SerilogLoggerFactory? _knowledgeLoggerFactory;

    // Discovery names the meta project after its folder (".firepit"), but its
    // knowledge registers as the "global" scope. Remembered here so tool
    // calls originating *inside* the meta project resolve to "global" too.
    private string? _metaProjectName;

    private void InitializeKnowledgeService()
    {
        try
        {
            var dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Firepit");
            _knowledgeLoggerFactory = new SerilogLoggerFactory(Log.Logger);
            _knowledgeService = new KnowledgeService(dataRoot, _knowledgeLoggerFactory);
            SyncKnowledgeScopes();
            _knowledgeService.StartModelDownload();
            Log.Information(
                "Knowledge service started: {Count} scope(s)",
                _knowledgeService.ScopeNames.Count);
        }
        catch (Exception ex)
        {
            // Knowledge is an assist feature — a failure here must never
            // block the shell from starting.
            Log.Error(ex, "Failed to start knowledge service");
            _knowledgeService = null;
        }
    }

    /// <summary>Reconcile knowledge scopes with the current project list.
    /// Safe to call any time; no-op before InitializeKnowledgeService.</summary>
    private void SyncKnowledgeScopes()
    {
        var svc = _knowledgeService;
        if (svc is null)
        {
            return;
        }

        try
        {
            var metaPath = Path.GetFullPath(Path.Combine(_settings.ProjectsRoot, ".firepit"));
            var registrations = new List<KnowledgeScopeRegistration>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in _allProjects)
            {
                var isMeta = string.Equals(
                    Path.GetFullPath(project.Path), metaPath, StringComparison.OrdinalIgnoreCase);
                if (isMeta)
                {
                    _metaProjectName = project.Name;
                }

                var name = isMeta ? KnowledgeService.GlobalScopeName : project.Name;
                if (seen.Add(name))
                {
                    registrations.Add(new KnowledgeScopeRegistration(name, project.Path));
                }
            }

            svc.SyncScopes(registrations);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Knowledge scope sync failed");
        }
    }

    private void DisposeKnowledgeService()
    {
        try { _knowledgeService?.Dispose(); } catch { /* ignored */ }
        _knowledgeService = null;
        try { _knowledgeLoggerFactory?.Dispose(); } catch { /* ignored */ }
        _knowledgeLoggerFactory = null;
    }

    /// <summary>A session inside the meta project calls its scope "global".</summary>
    private string MapToScopeName(string projectOrScopeName) =>
        _metaProjectName is not null &&
        string.Equals(projectOrScopeName, _metaProjectName, StringComparison.OrdinalIgnoreCase)
            ? KnowledgeService.GlobalScopeName
            : projectOrScopeName;

    // --- IMcpBackend knowledge members -----------------------------------

    public async Task<Firepit.Mcp.KnowledgeSearchResult> SearchKnowledgeAsync(
        string? projectScopeName, string scope, string query, int limit)
    {
        var svc = _knowledgeService;
        if (svc is null)
        {
            return new Firepit.Mcp.KnowledgeSearchResult(
                false, "Knowledge service is not running", [], false);
        }

        var scopes = new List<string>();
        var project = projectScopeName is null ? null : MapToScopeName(projectScopeName);
        switch (scope.ToLowerInvariant())
        {
            case "global":
                scopes.Add(KnowledgeService.GlobalScopeName);
                break;
            case "project":
                if (string.IsNullOrEmpty(project))
                {
                    return new Firepit.Mcp.KnowledgeSearchResult(
                        false,
                        "No project context — pass projectName or use scope 'global'.",
                        [], false);
                }

                scopes.Add(project);
                break;
            default: // "both" (and anything unrecognised collapses to it)
                if (!string.IsNullOrEmpty(project))
                {
                    scopes.Add(project);
                }

                scopes.Add(KnowledgeService.GlobalScopeName);
                break;
        }

        try
        {
            var result = await svc.SearchAsync(query, scopes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), limit);
            var hits = result.Hits
                .Select(h => new KnowledgeHitInfo(
                    h.Scope, h.Path, h.Title, h.Heading, h.Snippet, Math.Round(h.Score, 4)))
                .ToArray();
            var message = result.Degraded
                ? "Vector search unavailable (embedding model not ready) — results are full-text only."
                : null;
            return new Firepit.Mcp.KnowledgeSearchResult(true, message, hits, result.Degraded);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "firepit_knowledge_search failed");
            return new Firepit.Mcp.KnowledgeSearchResult(false, ex.Message, [], false);
        }
    }

    public async Task<KnowledgeDocumentResult> GetKnowledgeDocumentAsync(string scopeName, string path)
    {
        var svc = _knowledgeService;
        if (svc is null)
        {
            return new KnowledgeDocumentResult(false, "Knowledge service is not running");
        }

        try
        {
            var doc = await svc.GetDocumentAsync(MapToScopeName(scopeName), path);
            return doc is null
                ? new KnowledgeDocumentResult(false, $"No document '{path}' in scope '{scopeName}'.")
                : new KnowledgeDocumentResult(true, null, doc.Scope, doc.Path, doc.Title, doc.Content);
        }
        catch (ArgumentException ex)
        {
            return new KnowledgeDocumentResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "firepit_knowledge_get failed");
            return new KnowledgeDocumentResult(false, ex.Message);
        }
    }

    public async Task<KnowledgeDocumentResult> AddKnowledgeDocumentAsync(
        string scopeName, string title, string content, bool pinned)
    {
        var svc = _knowledgeService;
        if (svc is null)
        {
            return new KnowledgeDocumentResult(false, "Knowledge service is not running");
        }

        try
        {
            var doc = await svc.AddDocumentAsync(MapToScopeName(scopeName), title, content, pinned);
            var message = pinned
                ? "Saved, indexed and pinned (auto-injected at session start). Remember to commit the file."
                : "Saved and indexed. Remember to commit the file.";
            return new KnowledgeDocumentResult(
                true, message, doc.Scope, doc.Path, doc.Title, doc.Content);
        }
        catch (ArgumentException ex)
        {
            return new KnowledgeDocumentResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "firepit_knowledge_add failed");
            return new KnowledgeDocumentResult(false, ex.Message);
        }
    }

    public async Task<KnowledgeDocumentResult> UpdateKnowledgeDocumentAsync(
        string scopeName, string path, string content, string? title, bool? pinned)
    {
        var svc = _knowledgeService;
        if (svc is null)
        {
            return new KnowledgeDocumentResult(false, "Knowledge service is not running");
        }

        try
        {
            var doc = await svc.UpdateDocumentAsync(MapToScopeName(scopeName), path, content, title, pinned);
            return doc is null
                ? new KnowledgeDocumentResult(
                    false,
                    $"No document '{path}' in scope '{scopeName}' — use firepit_knowledge_add for new docs.")
                : new KnowledgeDocumentResult(
                    true, "Replaced and re-indexed. Remember to commit the change.",
                    doc.Scope, doc.Path, doc.Title, doc.Content);
        }
        catch (ArgumentException ex)
        {
            return new KnowledgeDocumentResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "firepit_knowledge_update failed");
            return new KnowledgeDocumentResult(false, ex.Message);
        }
    }

    public async Task<ToolCallResult> DeleteKnowledgeDocumentAsync(string scopeName, string path)
    {
        var svc = _knowledgeService;
        if (svc is null)
        {
            return new ToolCallResult(false, "Knowledge service is not running");
        }

        try
        {
            var deleted = await svc.DeleteDocumentAsync(MapToScopeName(scopeName), path);
            return deleted
                ? new ToolCallResult(true, "Deleted and removed from the index. Remember to commit the deletion.")
                : new ToolCallResult(false, $"No document '{path}' in scope '{scopeName}'.");
        }
        catch (ArgumentException ex)
        {
            return new ToolCallResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "firepit_knowledge_delete failed");
            return new ToolCallResult(false, ex.Message);
        }
    }
}
