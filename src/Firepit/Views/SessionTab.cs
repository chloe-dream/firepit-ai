using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Firepit.Core.Agents;
using Firepit.Core.Process;
using Firepit.Core.Projects;
using Firepit.Core.Terminal;
using Firepit.Process;
using Firepit.Web;

namespace Firepit.Views;

public sealed class SessionTab : IAsyncDisposable
{
    private readonly IAgentAdapter _adapter;
    private readonly Grid _content;
    private readonly TextBlock _statusText;
    private WebView2TerminalView? _terminalView;
    private IPtyChannel? _ptyChannel;
    private CancellationTokenSource? _cts;
    private bool _initialized;
    private bool _disposed;

    public SessionTab(ProjectContext context, IAgentAdapter adapter)
    {
        Context = context;
        _adapter = adapter;

        _statusText = new TextBlock
        {
            Text = $"Igniting {context.Name}…",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0x9F, 0x92)),
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _content = new Grid();
        _content.Children.Add(_statusText);
    }

    public ProjectContext Context { get; }

    public UIElement Content => _content;

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
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

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
                await _terminalView.WriteAsync(chunk, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch (Exception ex)
        {
            await _content.Dispatcher.InvokeAsync(() => ShowFatal(ex.Message));
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
