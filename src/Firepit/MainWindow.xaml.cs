using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Firepit.Adapters;
using Firepit.Core.Agents;
using Firepit.Core.Projects;
using Firepit.Views;

namespace Firepit;

public partial class MainWindow : Window
{
    // M5 will move this to settings.json. For now, hardcode to where projects live.
    private static readonly string ProjectsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "SynologyDrive", "PROJECTS");

    private readonly IReadOnlyDictionary<string, IAgentAdapter> _adapters;
    private readonly Dictionary<string, (TabItem TabItem, SessionTab Session)> _openTabs = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();

        _adapters = new Dictionary<string, IAgentAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            [ClaudeCodeAdapter.AdapterId] = new ClaudeCodeAdapter(),
        };

        ProjectList.ProjectActivated += OnProjectActivated;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var discovery = new ProjectDiscovery(_adapters.Values);
        var projects = discovery.Scan(ProjectsRoot);

        ProjectList.Projects.Clear();
        foreach (var project in projects)
        {
            ProjectList.Projects.Add(project);
        }
    }

    private void OnProjectActivated(object? sender, Project project)
    {
        if (_openTabs.TryGetValue(project.Path, out var existing))
        {
            existing.TabItem.IsSelected = true;
            return;
        }

        if (!_adapters.TryGetValue(project.AdapterId, out var adapter))
        {
            return;
        }

        var session = new SessionTab(new ProjectContext(project), adapter);
        var tabItem = new TabItem
        {
            Header = session.Header,
            Content = session.Content,
            Tag = session,
        };

        Tabs.Items.Add(tabItem);
        Tabs.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        tabItem.IsSelected = true;

        _openTabs[project.Path] = (tabItem, session);
        _ = session.EnsureInitializedAsync();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var sessions = _openTabs.Values.Select(t => t.Session).ToArray();
        _openTabs.Clear();
        foreach (var session in sessions)
        {
            try { await session.DisposeAsync(); } catch { /* ignored */ }
        }
    }
}
