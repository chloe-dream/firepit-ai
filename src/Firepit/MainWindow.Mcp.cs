using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using Firepit.Core.Settings;
using Firepit.Mcp;
using Firepit.Views;
using Serilog;

namespace Firepit;

/// <summary>
/// IMcpBackend implementation for MainWindow. Every member marshals onto
/// the WPF dispatcher when it touches UI state. The MCP host calls these
/// from background pipe threads.
/// </summary>
public partial class MainWindow : IMcpBackend
{
    private static readonly Regex SecretKeyRegex = new(
        @"(?i)(key|token|secret|password|api[_-]?key)$",
        RegexOptions.Compiled);

    public Task<IReadOnlyList<ProjectInfo>> ListProjectsAsync() =>
        OnDispatcherAsync(() =>
        {
            var openByPath = _openTabs.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Session,
                StringComparer.OrdinalIgnoreCase);
            return (IReadOnlyList<ProjectInfo>)_allProjects.Select(p =>
            {
                openByPath.TryGetValue(p.Path, out var session);
                return new ProjectInfo(
                    Name:         p.Name,
                    Path:         p.Path,
                    AdapterId:    p.AdapterId,
                    IsOpen:       session is not null,
                    SessionState: session?.State.ToString());
            }).ToArray();
        });

    public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync() =>
        OnDispatcherAsync(() =>
        {
            var selected = (Tabs.SelectedItem as TabItem)?.Tag as SessionTab;
            return (IReadOnlyList<SessionInfo>)_openTabs.Values
                .Select(t => new SessionInfo(
                    ProjectName: t.Session.Context.Name,
                    ProjectPath: t.Session.Context.Path,
                    State:       t.Session.State.ToString(),
                    IsActive:    ReferenceEquals(t.Session, selected)))
                .ToArray();
        });

    public Task<string> GetRedactedSettingsAsync() =>
        OnDispatcherAsync(() =>
        {
            string raw;
            using (var ms = new MemoryStream())
            {
                JsonSerializer.Serialize(ms, _settings, FirepitJsonContext.Default.FirepitSettings);
                raw = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            }
            var node = JsonNode.Parse(raw)!;
            RedactSecretsInPlace(node);
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        });

    public Task<ToolCallResult> OpenTabAsync(string projectName, bool resume) =>
        OnDispatcherAsync(() =>
        {
            var project = FindProjectByName(projectName);
            if (project is null) return new ToolCallResult(false, $"Unknown project: {projectName}");
            OpenSessionTab(project, resume);
            return new ToolCallResult(true, $"Opened tab for {projectName}");
        });

    public Task<ToolCallResult> FocusTabAsync(string projectName) =>
        OnDispatcherAsync(() =>
        {
            var project = FindProjectByName(projectName);
            if (project is null) return new ToolCallResult(false, $"Unknown project: {projectName}");
            if (!_openTabs.TryGetValue(project.Path, out var entry))
                return new ToolCallResult(false, $"{projectName} has no open tab");
            entry.TabItem.IsSelected = true;
            entry.Session.FocusTerminal();
            return new ToolCallResult(true, $"Focused {projectName}");
        });

    public Task<ToolCallResult> CloseTabAsync(string projectName) =>
        OnDispatcherAsync(async () =>
        {
            var project = FindProjectByName(projectName);
            if (project is null) return new ToolCallResult(false, $"Unknown project: {projectName}");
            if (!_openTabs.TryGetValue(project.Path, out var entry))
                return new ToolCallResult(false, $"{projectName} has no open tab");

            _openTabs.Remove(project.Path);
            DisposeConfigWatcher(project.Path);
            DisposeInboxWatcher(project.Path);
            DisposeRunsWatcher(project.Path);
            var index = Tabs.Items.IndexOf(entry.TabItem);
            Tabs.Items.Remove(entry.TabItem);
            if (Tabs.Items.Count == 0)
            {
                Tabs.Visibility = System.Windows.Visibility.Collapsed;
                HideAllTabContent();
            }
            else
            {
                var newIndex = Math.Min(index, Tabs.Items.Count - 1);
                ((TabItem)Tabs.Items[newIndex]!).IsSelected = true;
            }
            UnmountTabContent(entry.Session);
            try { await entry.Session.DisposeAsync(); } catch { /* ignored */ }
            return new ToolCallResult(true, $"Closed {projectName}");
        });

    public Task<ToolCallResult> ReloadAsync(string projectName, bool restart) =>
        OnDispatcherAsync(async () =>
        {
            var project = FindProjectByName(projectName);
            if (project is null) return new ToolCallResult(false, $"Unknown project: {projectName}");
            if (!_openTabs.TryGetValue(project.Path, out var entry))
                return new ToolCallResult(false, $"{projectName} has no open tab to reload");

            var newConfig = SafeLoadProjectConfig(project.Path);
            if (restart)
            {
                _ = entry.Session.RekindleAsync(resume: true, confirmIfBurning: false);
                return new ToolCallResult(true, $"Restarted {projectName} with new config");
            }
            await entry.Session.RefreshFromConfigAsync(newConfig);
            return new ToolCallResult(true, $"Reloaded {projectName} (quick-links applied; banner if restart needed)");
        });

    public Task<InboxListResult> ListInboxAsync(string projectName) =>
        OnDispatcherAsync(() =>
        {
            var project = FindProjectByName(projectName);
            if (project is null)
            {
                return new InboxListResult(projectName, Array.Empty<InboxMessage>());
            }

            var inboxDir = Path.Combine(project.Path, ".firepit", "inbox");
            if (!Directory.Exists(inboxDir))
            {
                return new InboxListResult(project.Name, Array.Empty<InboxMessage>());
            }

            var messages = new List<InboxMessage>();
            foreach (var file in Directory.EnumerateFiles(inboxDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var raw = File.ReadAllText(file);
                    var parsed = ParseFrontmatter(raw);
                    messages.Add(new InboxMessage(
                        Id:       Path.GetFileName(file),
                        From:     parsed.frontmatter.GetValueOrDefault("from"),
                        Subject:  parsed.frontmatter.GetValueOrDefault("subject"),
                        Priority: parsed.frontmatter.GetValueOrDefault("priority"),
                        Date:     parsed.frontmatter.GetValueOrDefault("date"),
                        Body:     parsed.body));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Inbox list: couldn't read {File}", file);
                }
            }
            // Stable order — by filename, which starts with an ISO date in
            // the firepit-mcp send_to convention, so newest sorts naturally.
            messages.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return new InboxListResult(project.Name, messages);
        });

    public Task<ToolCallResult> CompleteInboxAsync(string projectName, string id) =>
        OnDispatcherAsync(() =>
        {
            var project = FindProjectByName(projectName);
            if (project is null) return new ToolCallResult(false, $"Unknown project: {projectName}");

            // Guard against path traversal in the id — only accept a bare filename.
            if (id.Contains('/') || id.Contains('\\') || id.Contains("..", StringComparison.Ordinal))
            {
                return new ToolCallResult(false, $"Invalid id (must be a bare filename): {id}");
            }

            var inboxDir     = Path.Combine(project.Path, ".firepit", "inbox");
            var processedDir = Path.Combine(inboxDir, "processed");
            var source       = Path.Combine(inboxDir, id);
            var target       = Path.Combine(processedDir, id);

            if (!File.Exists(source))
            {
                return new ToolCallResult(false, $"No such message: {id}");
            }

            try
            {
                Directory.CreateDirectory(processedDir);
                // If a same-named processed file already exists (rare — same id
                // re-sent), append a -<timestamp> suffix rather than overwrite.
                if (File.Exists(target))
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
                    var ext   = Path.GetExtension(id);
                    var stem  = Path.GetFileNameWithoutExtension(id);
                    target    = Path.Combine(processedDir, $"{stem}-{stamp}{ext}");
                }
                File.Move(source, target);
                Log.Information("Inbox: completed {Id} for {Project} → {Target}", id, project.Name, target);
                return new ToolCallResult(true, $"Moved to processed: {Path.GetFileName(target)}");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Inbox complete failed for {Id} in {Project}", id, project.Name);
                return new ToolCallResult(false, ex.Message);
            }
        });

    /// <summary>
    /// Tiny YAML-frontmatter parser. Accepts the firepit_send_to format —
    /// <c>---\nkey: value\nkey: value\n---\n\nbody</c> — plus the looser legacy
    /// shape (no frontmatter at all). Values are trimmed; nested structures
    /// aren't supported because Firepit only emits flat key/value pairs.
    /// </summary>
    private static (Dictionary<string, string> frontmatter, string body) ParseFrontmatter(string raw)
    {
        var fm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!raw.StartsWith("---", StringComparison.Ordinal))
        {
            return (fm, raw);
        }
        var lines = raw.Split('\n');
        var endIdx = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r') == "---") { endIdx = i; break; }
        }
        if (endIdx < 0) return (fm, raw);

        for (var i = 1; i < endIdx; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var sep  = line.IndexOf(':');
            if (sep <= 0) continue;
            var key = line[..sep].Trim();
            var val = line[(sep + 1)..].Trim();
            if (key.Length > 0) fm[key] = val;
        }
        // Skip endIdx line itself, then strip a single leading blank line.
        var bodyStart = endIdx + 1;
        if (bodyStart < lines.Length && string.IsNullOrWhiteSpace(lines[bodyStart].TrimEnd('\r')))
        {
            bodyStart++;
        }
        var body = string.Join('\n', lines.Skip(bodyStart));
        return (fm, body);
    }

    public Task<InboxWriteResult> SendInboxAsync(string fromProject, string toProject,
                                                 string subject, string body, string priority) =>
        OnDispatcherAsync(() =>
        {
            var target = FindProjectByName(toProject);
            if (target is null)
                return new InboxWriteResult(false, Message: $"Unknown target project: {toProject}");

            var inboxDir = Path.Combine(target.Path, ".firepit", "inbox");
            try
            {
                Directory.CreateDirectory(inboxDir);
                var safeFrom = SafeSlug(fromProject);
                var safeSubj = SafeSlug(subject);
                var stamp    = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
                var fileName = $"{stamp}-from-{safeFrom}-{safeSubj}.md";
                var filePath = Path.Combine(inboxDir, fileName);

                var frontmatter =
                    "---\n" +
                    $"from: {fromProject}\n" +
                    $"to: {toProject}\n" +
                    $"subject: {subject.Replace("\n", " ")}\n" +
                    $"sentAt: {DateTime.UtcNow:O}\n" +
                    $"priority: {priority}\n" +
                    "---\n\n";
                File.WriteAllText(filePath, frontmatter + body);
                Log.Information("Inbox: {From} → {To} ({Subject})", fromProject, toProject, subject);
                return new InboxWriteResult(true, Path: filePath);
            }
            catch (Exception ex)
            {
                return new InboxWriteResult(false, Message: ex.Message);
            }
        });

    // --- helpers --------------------------------------------------------

    private Firepit.Core.Projects.Project? FindProjectByName(string name) =>
        _allProjects.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string SafeSlug(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var ch in input.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is '-' or '_') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 60) slug = slug[..60];
        return slug.Length == 0 ? "msg" : slug;
    }

    private static void RedactSecretsInPlace(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj.ToList())
                {
                    if (kvp.Value is null) continue;
                    if (kvp.Value is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        // Always mask values that look like raw secrets by key name.
                        if (SecretKeyRegex.IsMatch(kvp.Key) && !s.StartsWith("${"))
                        {
                            obj[kvp.Key] = "***";
                        }
                        // ${cred:...} stays opaque (Claude sees the reference, not the
                        // resolved value) — that's already the on-disk shape, no change.
                    }
                    else if (kvp.Value is JsonObject or JsonArray)
                    {
                        RedactSecretsInPlace(kvp.Value);
                    }
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is not null) RedactSecretsInPlace(item);
                }
                break;
        }
    }

    private Task<T> OnDispatcherAsync<T>(Func<T> work)
    {
        if (Dispatcher.CheckAccess()) return Task.FromResult(work());
        return Dispatcher.InvokeAsync(work).Task;
    }

    private Task<T> OnDispatcherAsync<T>(Func<Task<T>> work)
    {
        if (Dispatcher.CheckAccess()) return work();
        return Dispatcher.InvokeAsync(work).Task.Unwrap();
    }
}
