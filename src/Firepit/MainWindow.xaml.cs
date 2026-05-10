using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Firepit.Adapters;
using Firepit.Core.Agents;
using Firepit.Core.Mcp;
using Firepit.Core.Projects;
using Firepit.Core.QuickLinks;
using Firepit.Core.Secrets;
using Firepit.Core.Settings;
using Firepit.Core.State;
using Firepit.Native;
using Firepit.Process;
using Firepit.Views;
using Serilog;

namespace Firepit;

public partial class MainWindow : Window
{
    private readonly IReadOnlyDictionary<string, IAgentAdapter> _adapters;
    private readonly ISettingsStore _settingsStore;
    private readonly Dictionary<string, (TabItem TabItem, SessionTab Session)> _openTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ProjectPickerItem> _pickerItems = new();
    private List<Project> _allProjects = new();

    private FirepitSettings _settings;
    private IQuickLinkService _quickLinks;
    private IMcpRegistry _mcpRegistry;
    private readonly IAgentMcpProjector _mcpProjector;
    private readonly ISecretResolver _secretResolver;
    private readonly IStateStore _stateStore;

    public MainWindow()
    {
        InitializeComponent();

        _adapters = new Dictionary<string, IAgentAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            [ClaudeCodeAdapter.AdapterId] = new ClaudeCodeAdapter(),
        };

        _settingsStore = new JsonSettingsStore();
        _settings = _settingsStore.Load();
        ApplyChromeMetricsFromResources();
        _quickLinks = BuildQuickLinkService(_settings);
        _secretResolver = new CompositeSecretResolver(
            new EnvironmentSecretProvider(),
            new CredentialManagerSecretProvider());
        _mcpRegistry = new SettingsBackedMcpRegistry(_settings, _secretResolver);
        _mcpProjector = new ClaudeCodeMcpProjector();
        _stateStore = new JsonStateStore();

        PickerList.ItemsSource = _pickerItems;

        Loaded += OnLoaded;
        Closing += OnClosing;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnWindowStateChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowDarkMode.EnableForWindow(this);
    }

    private void ApplyChromeMetricsFromResources()
    {
        // App.xaml.cs writes CaptionPixelHeight before any Window loads. Mirror that
        // value into the title-bar row and WindowChrome so caption, tabs, and the
        // drag region all line up at any font size.
        if (TryGetDouble("CaptionPixelHeight", out var capH))
        {
            CaptionRow.Height = new System.Windows.GridLength(capH);
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            if (chrome is not null)
            {
                chrome.CaptionHeight = capH;
            }
        }
    }

    private bool TryGetDouble(string key, out double value)
    {
        if (TryFindResource(key) is double d) { value = d; return true; }
        value = 0; return false;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        MaxRestoreButton.Style = (Style)FindResource(
            WindowState == WindowState.Maximized
                ? "CaptionRestoreButton"
                : "CaptionMaximizeButton");
        MaxRestoreButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";

        // WPF custom-chrome windows extend their client rect past the work
        // area by ResizeBorderThickness on every edge when maximized — a known
        // platform quirk. Without compensation, the 2 px yellow accent at the
        // top of the selected tab is the first thing to fall off-screen.
        // Add a matching margin on the root grid only while maximized.
        if (Content is FrameworkElement root)
        {
            if (WindowState == WindowState.Maximized)
            {
                var border = System.Windows.Shell.WindowChrome.GetWindowChrome(this)?.ResizeBorderThickness
                             ?? new Thickness(6);
                root.Margin = border;
            }
            else
            {
                root.Margin = default;
            }
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaxRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReloadProjectList();
        if (_settings.Tabs.PersistAcrossRestarts)
        {
            RestoreTabsFromState();
        }
    }

    private void ReloadProjectList()
    {
        var manualEntries = (_settings.Projects ?? [])
            .Select(MapManualProject)
            .ToArray();

        var discovery = new ProjectDiscovery(_adapters.Values);
        _allProjects = discovery.Scan(_settings.ProjectsRoot, manualEntries).ToList();

        Log.Information("Project list loaded: root={Root}, found={Count}",
            _settings.ProjectsRoot, _allProjects.Count);
        if (_allProjects.Count == 0 && !Directory.Exists(_settings.ProjectsRoot))
        {
            ShowToast($"Projects root not found: {_settings.ProjectsRoot}", isError: true);
        }
    }

    private void RestoreTabsFromState()
    {
        var state = _stateStore.Load();
        if (state.Tabs.Count == 0)
        {
            return;
        }

        var byName = _allProjects.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var tab in state.Tabs)
        {
            if (byName.TryGetValue(tab.ProjectName, out var project))
            {
                OpenSessionTab(project, resume: tab.LastSessionResumable);
            }
        }
    }

    public void SummonByName(string projectName)
    {
        if (_allProjects.FirstOrDefault(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase)) is { } project)
        {
            OpenSessionTab(project, resume: false);
        }
    }

    private Project MapManualProject(ProjectSettings source) => new(
        Name: source.Name,
        Path: source.Path,
        AdapterId: ClaudeCodeAdapter.AdapterId,
        AgentCommandOverride: source.AgentCommand,
        AgentArgsOverride: source.AgentArgs);

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

    private void OpenSessionTab(Project project, bool resume)
    {
        if (_openTabs.TryGetValue(project.Path, out var existing))
        {
            existing.TabItem.IsSelected = true;
            return;
        }

        if (!_adapters.TryGetValue(project.AdapterId, out var adapter))
        {
            Log.Warning("No adapter registered for project {Project} (adapterId={AdapterId})",
                project.Name, project.AdapterId);
            ShowToast($"No adapter for {project.Name}", isError: true);
            return;
        }
        Log.Information("Opening tab for project {Project} (resume={Resume}) via adapter {Adapter}",
            project.Name, resume, adapter.Id);

        var session = new SessionTab(
            new ProjectContext(project),
            adapter,
            _quickLinks,
            _mcpRegistry,
            _mcpProjector,
            terminalTheme: _settings.Terminal,
            terminalFontSize: (_settings.Ui ?? UiSettings.Defaults).ResolvedFontSize);
        var tabItem = new TabItem
        {
            Header = session.Header,
            Tag = session,
            ToolTip = project.Path,
        };

        Tabs.Items.Add(tabItem);
        Tabs.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        tabItem.IsSelected = true;

        _openTabs[project.Path] = (tabItem, session);
        if (resume)
        {
            _ = session.RekindleAsync(resume: true, confirmIfBurning: false);
        }
        else
        {
            _ = session.EnsureInitializedAsync();
        }
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is TabItem { Tag: SessionTab session })
        {
            if (TabContentHost.Content is UIElement existing && !ReferenceEquals(existing, session.Content))
            {
                TabContentHost.Content = null;
            }
            if (!ReferenceEquals(TabContentHost.Content, session.Content))
            {
                TabContentHost.Content = session.Content;
            }
            TabContentHost.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
        }
        else
        {
            TabContentHost.Content = null;
            TabContentHost.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
    }

    private async void OnTabCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TabItem tabItem } || tabItem.Tag is not SessionTab session)
        {
            return;
        }

        var key = _openTabs.FirstOrDefault(kvp => ReferenceEquals(kvp.Value.TabItem, tabItem)).Key;
        if (key is not null)
        {
            _openTabs.Remove(key);
        }

        var index = Tabs.Items.IndexOf(tabItem);
        Tabs.Items.Remove(tabItem);
        if (Tabs.Items.Count == 0)
        {
            Tabs.Visibility = Visibility.Collapsed;
            TabContentHost.Content = null;
            TabContentHost.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
        else
        {
            var newIndex = Math.Min(index, Tabs.Items.Count - 1);
            ((TabItem)Tabs.Items[newIndex]!).IsSelected = true;
        }

        try { await session.DisposeAsync(); } catch { /* ignored */ }
    }

    private void OnNewTabClick(object sender, RoutedEventArgs e)
    {
        ProjectPicker.IsOpen = !ProjectPicker.IsOpen;
    }

    private void OnProjectPickerOpened(object? sender, EventArgs e)
    {
        ReloadProjectList();
        PickerSearch.Text = string.Empty;
        RefreshPickerItems(string.Empty);
        Dispatcher.InvokeAsync(() => PickerSearch.Focus(), DispatcherPriority.Input);
    }

    private void OnPickerSearchChanged(object sender, TextChangedEventArgs e)
    {
        RefreshPickerItems(PickerSearch.Text);
    }

    private void OnPickerSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (_pickerItems.Count > 0)
                {
                    PickerList.SelectedIndex = 0;
                    var container = (ListBoxItem?)PickerList.ItemContainerGenerator.ContainerFromIndex(0);
                    container?.Focus();
                    e.Handled = true;
                }
                break;
            case Key.Enter:
                ActivatePickerSelection(_pickerItems.FirstOrDefault());
                e.Handled = true;
                break;
            case Key.Escape:
                ProjectPicker.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnPickerListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ActivatePickerSelection(PickerList.SelectedItem as ProjectPickerItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ProjectPicker.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnPickerItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { Content: ProjectPickerItem item })
        {
            ActivatePickerSelection(item);
            e.Handled = true;
        }
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = _settings.ProjectsRoot,
            Title = "Pick a folder to open as a project",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var folder = dialog.FolderName;
        var name = System.IO.Path.GetFileName(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

        // Persist as a manual project entry so it survives restart.
        var existing = (_settings.Projects ?? []).ToList();
        if (!existing.Any(p => string.Equals(p.Path, folder, StringComparison.OrdinalIgnoreCase)))
        {
            existing.Add(new ProjectSettings(name, folder));
            _settings = _settings with { Projects = existing };
            try
            {
                _settingsStore.Save(_settings);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist manual project {Folder}", folder);
                ShowToast("Could not save project to settings.json — opening anyway", isError: true);
            }
        }

        ProjectPicker.IsOpen = false;
        var project = new Project(name, folder, ClaudeCodeAdapter.AdapterId);
        OpenSessionTab(project, resume: false);

        // Refresh the in-memory list so the picker shows the new entry next time.
        ReloadProjectList();
    }

    private void ActivatePickerSelection(ProjectPickerItem? item)
    {
        if (item?.Project is null)
        {
            return;
        }
        ProjectPicker.IsOpen = false;
        OpenSessionTab(item.Project, resume: false);
    }

    private void RefreshPickerItems(string filter)
    {
        var goldBrush = (Brush)new SolidColorBrush(Color.FromRgb(0xF5, 0xC9, 0x7B));
        var defaultBrush = (Brush)new SolidColorBrush(Color.FromRgb(0xE8, 0xE2, 0xD8));
        goldBrush.Freeze();
        defaultBrush.Freeze();

        _pickerItems.Clear();
        var query = string.IsNullOrWhiteSpace(filter)
            ? _allProjects
            : _allProjects.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var project in query)
        {
            var open = _openTabs.ContainsKey(project.Path);
            _pickerItems.Add(new ProjectPickerItem(
                Project: project,
                Name: project.Name,
                Path: project.Path,
                Status: open ? "open" : string.Empty,
                NameBrush: open ? goldBrush : defaultBrush));
        }
        // Don't auto-select the first row — it reads as "this is the default"
        // and visually clutters the popup. Enter on the search box still picks
        // _pickerItems.FirstOrDefault(); Down-arrow path explicitly sets index.
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var about = new AboutDialog { Owner = this };
        about.ShowDialog();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var previousFontSize = (_settings.Ui ?? UiSettings.Defaults).ResolvedFontSize;
        var dialog = new SettingsDialog(_settingsStore) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } updated)
        {
            _settings = updated;
            _quickLinks = BuildQuickLinkService(_settings);
            _mcpRegistry = new SettingsBackedMcpRegistry(_settings, _secretResolver);
            ReloadProjectList();

            var newFontSize = (_settings.Ui ?? UiSettings.Defaults).ResolvedFontSize;
            if (newFontSize != previousFontSize)
            {
                // StaticResource lookups in XAML resolve once — the window chrome
                // stays at the old size until the process restarts. Offer a
                // self-restart so the user doesn't have to remember.
                var restartNow = Views.MessageDialog.Show(
                    this,
                    title: "Restart to apply font size?",
                    message: $"Font size set to {newFontSize}pt. Firepit needs to restart for the window chrome (tabs, captions, dialogs) to pick up the new size.",
                    primaryLabel: "Restart now",
                    secondaryLabel: "Later");
                if (restartNow)
                {
                    SelfRestart();
                }
            }
        }
    }

    private void SelfRestart()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Log.Warning("Self-restart requested but ProcessPath is empty");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
            });
            // Shutdown triggers OnClosing → tabs persisted, sessions disposed.
            // The new instance picks up immediately because the singleton guard
            // releases its mutex on dispose (see OnExit).
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Self-restart failed");
            ShowToast($"Restart failed: {ex.Message}", isError: true);
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_settings.Tabs.PersistAcrossRestarts)
        {
            try
            {
                var snapshot = new AppState(
                    Version: AppState.CurrentVersion,
                    Tabs: _openTabs.Values
                        .Select(t => new TabState(
                            ProjectName: t.Session.Context.Name,
                            LastSessionResumable: t.Session.State != Core.Sessions.SessionState.Dead))
                        .ToArray());
                _stateStore.Save(snapshot);
            }
            catch { /* persistence is best-effort */ }
        }

        var sessions = _openTabs.Values.Select(t => t.Session).ToArray();
        _openTabs.Clear();
        foreach (var session in sessions)
        {
            try { await session.DisposeAsync(); } catch { /* ignored */ }
        }
    }

    public void ShowToast(string message, bool isError = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => ShowToast(message, isError));
            return;
        }

        ToastText.Text = message;
        Toast.BorderBrush = new SolidColorBrush(isError
            ? Color.FromRgb(0xCD, 0x5C, 0x5C)
            : Color.FromRgb(0xF5, 0xC9, 0x7B));
        Toast.Visibility = Visibility.Visible;

        var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        hideTimer.Tick += (s, _) =>
        {
            Toast.Visibility = Visibility.Collapsed;
            ((DispatcherTimer)s!).Stop();
        };
        hideTimer.Start();
    }

    public sealed record ProjectPickerItem(
        Project Project,
        string Name,
        string Path,
        string Status,
        Brush NameBrush);
}
