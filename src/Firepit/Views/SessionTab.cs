using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Firepit.Core.Agents;
using Firepit.Core.Mcp;
using Firepit.Core.Process;
using Firepit.Core.Projects;
using Firepit.Core.QuickLinks;
using Firepit.Core.Sessions;
using Firepit.Core.Settings;
using Firepit.Core.Terminal;
using Firepit.Core.Time;
using Firepit.Process;
using Firepit.Web;
using Serilog;

namespace Firepit.Views;

public sealed class SessionTab : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

    private readonly IAgentAdapter _adapter;
    private readonly IQuickLinkService _quickLinks;
    private readonly IMcpRegistry? _mcpRegistry;
    private readonly IAgentMcpProjector? _mcpProjector;
    private readonly TerminalThemeSettings? _terminalTheme;
    private readonly ActivityDetector _detector;
    private readonly Grid _content;
    private readonly Grid _terminalArea;
    private readonly TextBlock _statusText;
    private readonly TextBlock _headerText;
    private readonly TabToolbar _toolbar;
    private readonly Border _rekindleAffordance;
    private readonly StackPanel _loadingIndicator;
    private readonly RotateTransform _spinnerRotate;
    private readonly Storyboard _spinnerStoryboard;
    private readonly DispatcherTimer _tickTimer;
    private WebView2TerminalView? _terminalView;
    private IPtyChannel? _ptyChannel;
    private CancellationTokenSource? _cts;
    private bool _initialized;
    private bool _disposed;

    public SessionTab(
        ProjectContext context,
        IAgentAdapter adapter,
        IQuickLinkService quickLinks,
        IMcpRegistry? mcpRegistry = null,
        IAgentMcpProjector? mcpProjector = null,
        IActivityClock? clock = null,
        TerminalThemeSettings? terminalTheme = null)
    {
        Context = context;
        _adapter = adapter;
        _quickLinks = quickLinks;
        _mcpRegistry = mcpRegistry;
        _mcpProjector = mcpProjector;
        _terminalTheme = terminalTheme;

        _detector = new ActivityDetector(clock ?? new SystemActivityClock());
        _detector.StateChanged += OnStateChanged;

        _statusText = new TextBlock
        {
            Text = $"Igniting {context.Name}…",
            Foreground = StateColors.Brush(SessionState.Igniting),
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        (_loadingIndicator, _spinnerRotate, _spinnerStoryboard) = BuildLoadingIndicator(context.Name);

        _terminalArea = new Grid();
        _terminalArea.Children.Add(_loadingIndicator);
        StartSpinner();

        _toolbar = new TabToolbar();
        _toolbar.RekindleRequested += (_, _) => _ = RekindleAsync(resume: false, confirmIfBurning: true);
        _toolbar.ResumeRequested += (_, _) => _ = RekindleAsync(resume: true, confirmIfBurning: true);
        _toolbar.ExplorerRequested += (_, _) => OpenExplorer();
        _toolbar.ShellRequested += (_, _) => OpenExternalShell();
        _toolbar.QuickLinkClicked += (_, link) => OpenQuickLink(link);
        _toolbar.SetQuickLinks(_quickLinks.ResolveForProject(context));

        _rekindleAffordance = BuildRekindleAffordance();

        _terminalArea.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x16, 0x12));

        _content = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x16, 0x12)),
        };
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_toolbar, 0);
        Grid.SetRow(_terminalArea, 1);
        _content.Children.Add(_toolbar);
        _content.Children.Add(_terminalArea);
        _content.Loaded += (_, _) => Log.Information(
            "SessionTab content loaded for {Project}: contentSize={W}x{H}, toolbarVisible={ToolbarVisible}",
            Context.Name, _content.ActualWidth, _content.ActualHeight, _toolbar.IsVisible);

        _headerText = new TextBlock
        {
            Text = context.Name,
            Foreground = StateColors.Brush(SessionState.Cold),
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
        };

        _tickTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TickInterval,
        };
        _tickTimer.Tick += (_, _) => _detector.Tick();
    }

    public ProjectContext Context { get; }

    public UIElement Content => _content;

    public UIElement Header => _headerText;

    public SessionState State => _detector.State;

    public Task EnsureInitializedAsync() => StartSessionAsync(resume: false);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _tickTimer.Stop();
        StopSpinner();

        try { _cts?.Cancel(); } catch { /* ignored */ }

        if (_ptyChannel is not null)
        {
            try { await _ptyChannel.DisposeAsync(); } catch { /* ignored */ }
            _ptyChannel = null;
        }
        if (_terminalView is not null)
        {
            try { _terminalView.Dispose(); } catch { /* ignored */ }
            _terminalView = null;
        }

        _detector.StateChanged -= OnStateChanged;
        _cts?.Dispose();
    }

    public async Task RekindleAsync(bool resume, bool confirmIfBurning)
    {
        if (_disposed)
        {
            return;
        }

        if (confirmIfBurning && _detector.State == SessionState.Burning)
        {
            var owner = Window.GetWindow(_content);
            var confirmed = MessageDialog.Show(
                owner,
                title: "Restart session?",
                message: $"The session in {Context.Name} is still burning. Restarting will kill the running agent and start a fresh one.",
                primaryLabel: "Restart",
                secondaryLabel: "Cancel");
            if (!confirmed)
            {
                return;
            }
        }

        await TeardownSessionAsync();
        await StartSessionAsync(resume);
    }

    private async Task StartSessionAsync(bool resume)
    {
        if (_initialized && _detector.State != SessionState.Dead && _detector.State != SessionState.Cold)
        {
            return;
        }
        _initialized = true;

        try
        {
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            _detector.NotifyIgniting();
            _tickTimer.Start();
            ShowLoadingIndicator();

            if (_terminalView is null)
            {
                Log.Debug("Creating WebView2TerminalView for {Project}", Context.Name);
                _terminalView = new WebView2TerminalView(_terminalTheme);
                _terminalArea.Children.Add(_terminalView.Element);
                await _terminalView.InitializeAsync(ct);
                _terminalView.InputReceived += OnInputReceived;
                _terminalView.Resized += OnResized;
                Log.Debug("WebView2 ready for {Project}", Context.Name);
            }

            if (_terminalArea.Children.Contains(_statusText))
            {
                _terminalArea.Children.Remove(_statusText);
            }
            // The spinner stays visible until the agent's first byte actually paints —
            // WebView2 mounting is not the same as Claude Code being ready.
            HideRekindleAffordance();

            await ApplyMcpAsync(ct);

            var spec = _adapter.BuildLaunchSpec(Context, new AgentLaunchOptions(Resume: resume));
            var initialSize = _terminalView!.CurrentSize ?? TerminalSize.Default;
            Log.Information("Spawning agent for {Project}: {Executable} {Args} in {Cwd} (size {Cols}x{Rows})",
                Context.Name, spec.Executable, string.Join(' ', spec.Arguments), spec.WorkingDirectory,
                initialSize.Cols, initialSize.Rows);
            _ptyChannel = await ConPtyLauncher.SpawnAsync(
                executable: spec.Executable,
                arguments: spec.Arguments,
                workingDirectory: spec.WorkingDirectory,
                environmentOverrides: spec.EnvironmentOverrides,
                initialSize: initialSize,
                ct: ct);
            Log.Information("Agent spawned for {Project}: pid={Pid}", Context.Name, _ptyChannel.Pid);

            // Apply any size update that arrived between init and now (e.g., user resized the
            // window during WebView2 startup).
            if (_terminalView.CurrentSize is { } latest
                && (latest.Cols != initialSize.Cols || latest.Rows != initialSize.Rows))
            {
                _ptyChannel.Resize(latest.Cols, latest.Rows);
            }

            _ = PumpOutputAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Session start cancelled for {Project}", Context.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Session start failed for {Project}", Context.Name);
            ShowFatal(ex.Message);
            _detector.NotifyExited();
        }
    }

    private async Task ApplyMcpAsync(CancellationToken ct)
    {
        if (_mcpRegistry is null || _mcpProjector is null)
        {
            return;
        }
        var active = _mcpRegistry.ResolveForProject(Context);
        Log.Information("Projecting {Count} MCP server(s) for {Project}", active.Count, Context.Name);
        foreach (var server in active)
        {
            foreach (var warning in server.ResolutionWarnings)
            {
                Log.Warning("MCP {Server} resolution: {Warning}", server.Id, warning);
            }
        }
        await _mcpProjector.ApplyAsync(Context, active, ct).ConfigureAwait(true);
    }

    private async Task TeardownSessionAsync()
    {
        try { _cts?.Cancel(); } catch { /* ignored */ }
        if (_ptyChannel is not null)
        {
            try { await _ptyChannel.DisposeAsync(); } catch { /* ignored */ }
            _ptyChannel = null;
        }
        _cts?.Dispose();
        _cts = null;
        _initialized = false;
    }

    private async Task PumpOutputAsync(CancellationToken ct)
    {
        if (_ptyChannel is null || _terminalView is null)
        {
            return;
        }

        try
        {
            await foreach (var chunk in _ptyChannel.ReadAsync(ct))
            {
                _detector.NotifyRead();
                await _terminalView.WriteAsync(chunk, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception ex)
        {
            await _content.Dispatcher.InvokeAsync(() => ShowFatal(ex.Message));
        }
        finally
        {
            if (!_disposed)
            {
                _detector.NotifyExited();
            }
        }
    }

    private async void OnInputReceived(object? sender, ReadOnlyMemory<byte> data)
    {
        if (_ptyChannel is null || _cts is null)
        {
            return;
        }
        try
        {
            await _ptyChannel.WriteAsync(data, _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private void OnResized(object? sender, TerminalSize size)
    {
        try
        {
            _ptyChannel?.Resize(size.Cols, size.Rows);
        }
        catch (ObjectDisposedException) { }
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        if (_headerText.Dispatcher.CheckAccess())
        {
            ApplyStateVisuals(state);
        }
        else
        {
            _headerText.Dispatcher.InvokeAsync(() => ApplyStateVisuals(state));
        }
    }

    private void ApplyStateVisuals(SessionState state)
    {
        _headerText.Foreground = StateColors.Brush(state);
        _headerText.FontWeight = state == SessionState.Burning ? FontWeights.SemiBold : FontWeights.Normal;
        if (state == SessionState.Dead)
        {
            ShowRekindleAffordance();
        }
        else
        {
            HideRekindleAffordance();
        }
        // Hide the load spinner when output settles (Embers) or the process exits (Dead).
        // "Output stopped streaming" is the closest transparent signal we have to
        // "Claude Code finished its boot banner and is waiting for input" without
        // peeking at the PTY content.
        if (state == SessionState.Embers || state == SessionState.Dead)
        {
            HideLoadingIndicator();
        }
    }

    private void OpenExplorer()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Context.Path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowFatal($"Cannot open Explorer: {ex.Message}");
        }
    }

    private void OpenExternalShell()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{Context.Path}\"",
                UseShellExecute = true,
            });
        }
        catch
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -Command \"Set-Location '{Context.Path}'\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                ShowFatal($"Cannot open shell: {ex.Message}");
            }
        }
    }

    private void OpenQuickLink(ResolvedQuickLink link)
    {
        if (!link.Available)
        {
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = link.Url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowFatal($"Cannot open {link.Name}: {ex.Message}");
        }
    }

    private void ShowFatal(string message)
    {
        HideLoadingIndicator();
        _statusText.Text = message;
        _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0x5C, 0x5C));
        if (!_terminalArea.Children.Contains(_statusText))
        {
            _terminalArea.Children.Add(_statusText);
        }
    }

    private static (StackPanel Panel, RotateTransform Rotate, Storyboard Story) BuildLoadingIndicator(string projectName)
    {
        // Single source of truth for rotation center: RotateTransform's explicit
        // CenterX/Y in element-local coords. Do NOT also set RenderTransformOrigin —
        // WPF would compose the two and the spinner would orbit instead of spin.
        var rotate = new RotateTransform(0, 12, 12);
        var spinnerGeometry = (Geometry)Application.Current.FindResource("IconSpinnerArc");
        var spinner = new System.Windows.Shapes.Path
        {
            Data = spinnerGeometry,
            Stroke = StateColors.Brush(SessionState.Igniting),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransform = rotate,
        };

        var label = new TextBlock
        {
            Text = $"Igniting {projectName}…",
            Foreground = StateColors.Brush(SessionState.Igniting),
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(spinner);
        panel.Children.Add(label);
        // WebView2 mounts on top of us; without this z-bump the spinner is
        // hidden behind the WebView's background even before any output paints.
        Panel.SetZIndex(panel, 10);

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = new Duration(TimeSpan.FromMilliseconds(900)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(animation, spinner);
        Storyboard.SetTargetProperty(animation,
            new PropertyPath("RenderTransform.(RotateTransform.Angle)"));
        var story = new Storyboard();
        story.Children.Add(animation);

        return (panel, rotate, story);
    }

    private void StartSpinner()
    {
        try { _spinnerStoryboard.Begin(_loadingIndicator, isControllable: true); }
        catch { /* animation start is best-effort */ }
    }

    private void StopSpinner()
    {
        try { _spinnerStoryboard.Stop(_loadingIndicator); } catch { /* ignored */ }
    }

    private void ShowLoadingIndicator()
    {
        if (!_terminalArea.Children.Contains(_loadingIndicator))
        {
            _terminalArea.Children.Add(_loadingIndicator);
        }
        _loadingIndicator.Visibility = Visibility.Visible;
        StartSpinner();
    }

    private void HideLoadingIndicator()
    {
        StopSpinner();
        _loadingIndicator.Visibility = Visibility.Collapsed;
        if (_terminalArea.Children.Contains(_loadingIndicator))
        {
            _terminalArea.Children.Remove(_loadingIndicator);
        }
    }

    private Border BuildRekindleAffordance()
    {
        var label = new TextBlock
        {
            Text = "This session went out. Click to restart.",
            Foreground = StateColors.Brush(SessionState.Igniting),
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(20, 12, 20, 12),
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x1A, 0x16, 0x12)),
            BorderBrush = StateColors.Brush(SessionState.Igniting),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            Child = label,
        };
        border.MouseLeftButtonUp += (_, _) => _ = RekindleAsync(resume: false, confirmIfBurning: false);
        return border;
    }

    private void ShowRekindleAffordance()
    {
        if (!_terminalArea.Children.Contains(_rekindleAffordance))
        {
            _terminalArea.Children.Add(_rekindleAffordance);
        }
        _rekindleAffordance.Visibility = Visibility.Visible;
    }

    private void HideRekindleAffordance()
    {
        _rekindleAffordance.Visibility = Visibility.Collapsed;
    }
}

internal static class StateColors
{
    public static Brush Brush(SessionState state) => state switch
    {
        SessionState.Cold     => Frozen(0x6B, 0x62, 0x58),
        SessionState.Igniting => Frozen(0xC9, 0x9A, 0x5C),
        SessionState.Burning  => Frozen(0xF5, 0xC9, 0x7B),
        SessionState.Embers   => Frozen(0x8C, 0x7A, 0x5C),
        SessionState.Dead     => Frozen(0x6B, 0x62, 0x58),
        _                     => Frozen(0xA8, 0x9F, 0x92),
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
