using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using Firepit.Core.Settings;
using Firepit.Core.Terminal;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;

namespace Firepit.Web;

[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WebView2TerminalView : ITerminalView
{
    private static readonly Uri TerminalUri = new("https://firepit.local/terminal.html");

    private readonly WebView2 _webView = new();
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TerminalThemeSettings _theme;
    private bool _initialized;
    private bool _disposed;

    public WebView2TerminalView(TerminalThemeSettings? theme = null)
    {
        _theme = (theme ?? TerminalThemeSettings.Defaults).Resolved();
    }

    public event EventHandler<ReadOnlyMemory<byte>>? InputReceived;
    public event EventHandler<TerminalSize>? Resized;

    public WebView2 Element => _webView;

    public TerminalSize? CurrentSize { get; private set; }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        Log.Information("WV2 init: extracting assets");
        var assetsDir = WebAssetExtractor.Extract();
        Log.Information("WV2 init: assets at {Dir}", assetsDir);

        Log.Information("WV2 init: getting environment");
        var environment = await FirepitWebViewEnvironment.GetAsync().ConfigureAwait(true);
        Log.Information("WV2 init: environment ready (browser version {Version})", environment.BrowserVersionString);

        Log.Information("WV2 init: ensuring CoreWebView2");
        await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
        Log.Information("WV2 init: CoreWebView2 ready");

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            hostName: "firepit.local",
            folderPath: assetsDir,
            accessKind: CoreWebView2HostResourceAccessKind.Allow);

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true; // V1 diagnostic — flip back later
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.NavigationCompleted += (_, args) =>
            Log.Information("WV2 NavigationCompleted: success={IsSuccess}, status={Status}", args.IsSuccess, args.WebErrorStatus);
        _webView.CoreWebView2.ProcessFailed += (_, args) =>
            Log.Error("WV2 ProcessFailed: kind={Kind}, reason={Reason}, exitCode={Exit}",
                args.ProcessFailedKind, args.Reason, args.ExitCode);

        Log.Information("WV2 init: navigating to {Uri}", TerminalUri);
        _webView.Source = TerminalUri;

        Log.Information("WV2 init: waiting for ready handshake (15 s timeout)");
        try
        {
            await _readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(true);
            Log.Information("WV2 init: ready received");
        }
        catch (TimeoutException)
        {
            Log.Error("WV2 init: ready handshake timed out after 15 s");
            throw new InvalidOperationException(
                "Terminal renderer never reported ready. The WebView2 page may have failed to load — check the logs for NavigationCompleted status.");
        }
        ApplyTheme();
        _initialized = true;
    }

    private void ApplyTheme()
    {
        // Settings-driven palette. Sent after the ready handshake so xterm has
        // already constructed with the inline defaults — this just overrides.
        var payload = JsonSerializer.Serialize(new
        {
            type  = "theme",
            theme = new
            {
                background                  = _theme.Background,
                foreground                  = _theme.Foreground,
                cursor                      = _theme.Cursor,
                selectionBackground         = _theme.SelectionBackground,
                selectionForeground         = _theme.SelectionForeground,
                selectionInactiveBackground = _theme.SelectionInactiveBackground,
            }
        });
        _webView.CoreWebView2.PostWebMessageAsString(payload);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (data.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        var b64 = Convert.ToBase64String(data.Span);
        var payload = $"{{\"type\":\"data\",\"b64\":\"{b64}\"}}";
        if (_webView.Dispatcher.CheckAccess())
        {
            _webView.CoreWebView2.PostWebMessageAsString(payload);
        }
        else
        {
            _webView.Dispatcher.InvokeAsync(() =>
                _webView.CoreWebView2.PostWebMessageAsString(payload));
        }
        return ValueTask.CompletedTask;
    }

    public void Focus()
    {
        if (_webView.Dispatcher.CheckAccess())
        {
            _webView.Focus();
        }
        else
        {
            _webView.Dispatcher.InvokeAsync(() => _webView.Focus());
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            if (_webView.CoreWebView2 is { } core)
            {
                core.WebMessageReceived -= OnWebMessageReceived;
            }
        }
        catch { /* ignored */ }

        _webView.Dispose();
    }

    private void HandlePasteRequest()
    {
        string text;
        try
        {
            // Clipboard requires STA — WebView2 events fire on the WPF UI
            // thread (STA), so a direct call is fine. Fail closed if anything
            // throws (clipboard is occasionally locked by another process).
            text = System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText()
                : string.Empty;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "WV2 paste-request: clipboard read failed");
            return;
        }
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        var payload = $"{{\"type\":\"paste\",\"text\":{JsonSerializer.Serialize(text)}}}";
        _webView.CoreWebView2.PostWebMessageAsString(payload);
    }

    private static void HandleCopy(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "WV2 copy: clipboard write failed");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch (Exception)
        {
            return;
        }
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return;
            }
            var type = typeProp.GetString();
            switch (type)
            {
                case "ready":
                    Log.Information("WV2 bridge: 'ready' received");
                    _readyTcs.TrySetResult();
                    break;
                case "input":
                    if (doc.RootElement.TryGetProperty("b64", out var b64Prop)
                        && b64Prop.ValueKind == JsonValueKind.String)
                    {
                        var bytes = Convert.FromBase64String(b64Prop.GetString()!);
                        InputReceived?.Invoke(this, bytes);
                    }
                    break;
                case "resize":
                    if (doc.RootElement.TryGetProperty("cols", out var colsProp)
                        && doc.RootElement.TryGetProperty("rows", out var rowsProp)
                        && colsProp.TryGetInt32(out var cols)
                        && rowsProp.TryGetInt32(out var rows))
                    {
                        var size = new TerminalSize(cols, rows);
                        CurrentSize = size;
                        Resized?.Invoke(this, size);
                    }
                    break;
                case "paste-request":
                    HandlePasteRequest();
                    break;
                case "copy":
                    if (doc.RootElement.TryGetProperty("text", out var textProp)
                        && textProp.ValueKind == JsonValueKind.String)
                    {
                        HandleCopy(textProp.GetString());
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            // dropped — handler is robust against malformed messages
        }
        catch (FormatException)
        {
            // base64 decode failed — drop
        }
    }
}
