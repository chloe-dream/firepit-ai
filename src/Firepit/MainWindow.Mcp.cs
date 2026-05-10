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
            var index = Tabs.Items.IndexOf(entry.TabItem);
            Tabs.Items.Remove(entry.TabItem);
            if (Tabs.Items.Count == 0)
            {
                Tabs.Visibility = System.Windows.Visibility.Collapsed;
                TabContentHost.Content = null;
                TabContentHost.Visibility = System.Windows.Visibility.Collapsed;
                EmptyState.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                var newIndex = Math.Min(index, Tabs.Items.Count - 1);
                ((TabItem)Tabs.Items[newIndex]!).IsSelected = true;
            }
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
