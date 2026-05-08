using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Firepit.Adapters;
using Firepit.Core.Agents;
using Firepit.Core.Projects;
using Firepit.Core.QuickLinks;
using Firepit.Core.Settings;
using Firepit.Views;

namespace Firepit;

public partial class MainWindow : Window
{
    private readonly IReadOnlyDictionary<string, IAgentAdapter> _adapters;
    private readonly ISettingsStore _settingsStore;
    private readonly Dictionary<string, (TabItem TabItem, SessionTab Session)> _openTabs = new(StringComparer.OrdinalIgnoreCase);

    private FirepitSettings _settings;
    private IQuickLinkService _quickLinks;

    public MainWindow()
    {
        InitializeComponent();

        _adapters = new Dictionary<string, IAgentAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            [ClaudeCodeAdapter.AdapterId] = new ClaudeCodeAdapter(),
        };

        _settingsStore = new JsonSettingsStore();
        _settings = _settingsStore.Load();
        _quickLinks = BuildQuickLinkService(_settings);

        ProjectList.ProjectActivated += OnProjectActivated;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReloadProjectList();
    }

    private void ReloadProjectList()
    {
        var manualEntries = (_settings.Projects ?? [])
            .Select(MapManualProject)
            .ToArray();

        var discovery = new ProjectDiscovery(_adapters.Values);
        var projects = discovery.Scan(_settings.ProjectsRoot, manualEntries);

        ProjectList.Projects.Clear();
        foreach (var project in projects)
        {
            ProjectList.Projects.Add(project);
        }
    }

    private Project MapManualProject(ProjectSettings source)
    {
        var adapterId = !string.IsNullOrWhiteSpace(source.AgentCommand)
            ? ClaudeCodeAdapter.AdapterId
            : ClaudeCodeAdapter.AdapterId;

        return new Project(
            Name: source.Name,
            Path: source.Path,
            AdapterId: adapterId,
            AgentCommandOverride: source.AgentCommand,
            AgentArgsOverride: source.AgentArgs);
    }

    private IQuickLinkService BuildQuickLinkService(FirepitSettings settings)
    {
        var globals = (settings.QuickLinks ?? [])
            .Select(MapLinkSetting)
            .ToArray();

        var perProject = (settings.Projects ?? [])
            .Where(p => p.QuickLinks is { Count: > 0 })
            .ToDictionary(p => p.Path, p => p.QuickLinks!, StringComparer.OrdinalIgnoreCase);

        return new QuickLinkService(
            globalDefaults: globals,
            projectOverrides: ctx =>
                perProject.TryGetValue(ctx.Path, out var overrides)
                    ? overrides.Select(MapLinkSetting).ToArray()
                    : []);
    }

    private static QuickLinkEntry MapLinkSetting(QuickLinkSettings source) => new(
        Name: source.Name,
        UrlTemplate: source.Url,
        Target: source.Target == QuickLinkTargetSetting.External ? QuickLinkTarget.External : QuickLinkTarget.SubTab,
        Icon: source.Icon,
        Disabled: source.Disabled ?? false);

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

        var session = new SessionTab(new ProjectContext(project), adapter, _quickLinks);
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

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settingsStore) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } updated)
        {
            _settings = updated;
            _quickLinks = BuildQuickLinkService(_settings);
            ReloadProjectList();
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

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
