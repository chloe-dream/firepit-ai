using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Firepit.Core.Agents;
using Firepit.Core.Process;
using Firepit.Core.Projects;
using Firepit.Core.Sessions;
using Firepit.Core.Terminal;
using Firepit.Core.Time;
using Firepit.Process;
using Firepit.Web;

namespace Firepit.Views;

public sealed class SessionTab : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

    private readonly IAgentAdapter _adapter;
    private readonly ActivityDetector _detector;
    private readonly Grid _content;
    private readonly TextBlock _statusText;
    private readonly TextBlock _headerText;
    private readonly DispatcherTimer _tickTimer;
    private WebView2TerminalView? _terminalView;
    private IPtyChannel? _ptyChannel;
    private CancellationTokenSource? _cts;
    private bool _initialized;
    private bool _disposed;

    public SessionTab(ProjectContext context, IAgentAdapter adapter, IActivityClock? clock = null)
    {
        Context = context;
        _adapter = adapter;
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

        _content = new Grid();
        _content.Children.Add(_statusText);

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

    public async Task EnsureInitializedAsync()
    {
        if (_initialized || _disposed)
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

            _terminalView = new WebView2TerminalView();
            _content.Children.Add(_terminalView.Element);

            await _terminalView.InitializeAsync(ct);
            _content.Children.Remove(_statusText);

            var spec = _adapter.BuildLaunchSpec(Context, new AgentLaunchOptions());
            _ptyChannel = await ConPtyLauncher.SpawnAsync(
                executable: spec.Executable,
                arguments: spec.Arguments,
                workingDirectory: spec.WorkingDirectory,
                environmentOverrides: spec.EnvironmentOverrides,
                initialSize: TerminalSize.Default,
                ct: ct);

            _terminalView.InputReceived += OnInputReceived;
            _terminalView.Resized += OnResized;

            _ = PumpOutputAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // tab closed during init
        }
        catch (Exception ex)
        {
            ShowFatal(ex.Message);
            _detector.NotifyExited();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _tickTimer.Stop();

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
            ApplyHeaderStyle(state);
        }
        else
        {
            _headerText.Dispatcher.InvokeAsync(() => ApplyHeaderStyle(state));
        }
    }

    private void ApplyHeaderStyle(SessionState state)
    {
        _headerText.Foreground = StateColors.Brush(state);
        _headerText.FontWeight = state == SessionState.Burning ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void ShowFatal(string message)
    {
        _statusText.Text = $"Cannot summon agent: {message}";
        _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0x5C, 0x5C));
        if (!_content.Children.Contains(_statusText))
        {
            _content.Children.Add(_statusText);
        }
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
