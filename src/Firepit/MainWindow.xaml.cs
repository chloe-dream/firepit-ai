using System;
using System.Threading;
using System.Windows;
using Firepit.Core.Process;
using Firepit.Core.Terminal;
using Firepit.Process;
using Firepit.Web;

namespace Firepit;

public partial class MainWindow : Window
{
    private WebView2TerminalView? _terminalView;
    private IPtyChannel? _ptyChannel;
    private CancellationTokenSource? _sessionCts;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _sessionCts = new CancellationTokenSource();
            var ct = _sessionCts.Token;

            _terminalView = new WebView2TerminalView();
            RootGrid.Children.Clear();
            RootGrid.Children.Add(_terminalView.Element);

            await _terminalView.InitializeAsync(ct);

            _ptyChannel = await ConPtyLauncher.SpawnAsync(
                executable: "powershell.exe",
                arguments: ["-NoLogo"],
                workingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                environmentOverrides: null,
                initialSize: TerminalSize.Default,
                ct: ct);

            _terminalView.InputReceived += OnInputReceived;
            _terminalView.Resized += OnTerminalResized;

            _ = PumpPtyOutputAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // window closed during init
        }
        catch (Exception ex)
        {
            ShowFatal(ex);
        }
    }

    private async System.Threading.Tasks.Task PumpPtyOutputAsync(CancellationToken ct)
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
            // expected on shutdown
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ShowFatal(ex));
        }
    }

    private async void OnInputReceived(object? sender, ReadOnlyMemory<byte> data)
    {
        if (_ptyChannel is null || _sessionCts is null)
        {
            return;
        }
        try
        {
            await _ptyChannel.WriteAsync(data, _sessionCts.Token);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
    }

    private void OnTerminalResized(object? sender, TerminalSize size)
    {
        try
        {
            _ptyChannel?.Resize(size.Cols, size.Rows);
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try { _sessionCts?.Cancel(); } catch { /* ignored */ }

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
    }

    private void ShowFatal(Exception ex)
    {
        StatusText.Text = $"Cannot summon agent: {ex.Message}";
        StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        if (RootGrid.Children.Count > 1)
        {
            // ensure status text is visible above the WebView
            RootGrid.Children.Remove(StatusText);
            RootGrid.Children.Add(StatusText);
        }
    }
}
