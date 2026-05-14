using System.IO;
using System.Runtime.Versioning;
using System.Text;
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

    // Serialize WebView2 + xterm.js boot across all tabs. Parallel inits during
    // cold-start with 4 tabs blew past the 15s ready-handshake timeout under
    // load: the tab survived as a zombie — ready arrived 30s+ late, session
    // was already Dead, no PTY channel, every keypress silently dropped.
    private static readonly SemaphoreSlim InitGate = new(1, 1);

    private readonly WebView2 _webView = new();
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TerminalThemeSettings _theme;
    private readonly int _fontSize;
    private bool _initialized;
    private bool _disposed;

    // Native OLE drop target registered on the WebView2 host HWND. Kept alive
    // by this field so the CCW isn't collected while OLE holds it; the HWND is
    // remembered separately so Dispose can RevokeDragDrop even after teardown.
    private FileDropTarget? _fileDropTarget;
    private IntPtr _dropTargetHwnd;

    public WebView2TerminalView(TerminalThemeSettings? theme = null, int fontSize = 14)
    {
        _theme = (theme ?? TerminalThemeSettings.Defaults).Resolved();
        _fontSize = fontSize;
    }

    public event EventHandler<ReadOnlyMemory<byte>>? InputReceived;
    public event EventHandler<TerminalSize>? Resized;
    public event EventHandler<bool>? ProgressChanged;

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

        Log.Information("WV2 init: waiting for init gate (queued={Queued})", InitGate.CurrentCount == 0);
        await InitGate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
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

            // Turn off the browser layer's own drag-and-drop: it would
            // navigate to dropped files (Edge opens images in a preview) and
            // it registers a Chromium drop target on the inner HWNDs. With it
            // off, Firepit registers its own native IDropTarget on the host
            // HWND once init finishes — see RegisterFileDropTarget(). WPF's
            // managed drag-drop events can't be used: the WebView2 is an
            // HwndHost and its airspace is invisible to WPF hit-testing.
            _webView.AllowExternalDrop = false;

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
            ApplyFontSize();
            RegisterFileDropTarget();
            _initialized = true;
        }
        finally
        {
            InitGate.Release();
        }
    }

    private void ApplyFontSize()
    {
        var payload = $"{{\"type\":\"font\",\"size\":{_fontSize}}}";
        _webView.CoreWebView2.PostWebMessageAsString(payload);
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
        // Two-stage focus: WPF focuses the WebView2 host, then a bridge
        // message tells xterm.js to focus its hidden textarea. Without the
        // second stage, the WebView2 has focus but keystrokes go to the
        // browser layer rather than the terminal.
        if (_webView.Dispatcher.CheckAccess())
        {
            FocusCore();
        }
        else
        {
            _webView.Dispatcher.InvokeAsync(FocusCore);
        }
    }

    private void FocusCore()
    {
        try { _webView.Focus(); } catch { /* ignored */ }
        try { _webView.CoreWebView2?.PostWebMessageAsString("{\"type\":\"focus\"}"); }
        catch { /* CoreWebView2 not ready yet — first user click will refocus */ }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_dropTargetHwnd != IntPtr.Zero)
        {
            try { NativeDragDrop.RevokeDragDrop(_dropTargetHwnd); }
            catch (Exception ex) { Log.Debug(ex, "WV2 drop: RevokeDragDrop failed"); }
            _dropTargetHwnd = IntPtr.Zero;
            _fileDropTarget = null;
        }

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
        PasteText(text);
    }

    private void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text) || _disposed) return;
        var payload = $"{{\"type\":\"paste\",\"text\":{JsonSerializer.Serialize(text)}}}";
        try { _webView.CoreWebView2.PostWebMessageAsString(payload); }
        catch (Exception ex) { Log.Debug(ex, "WV2 paste-text: post failed"); }
    }

    /// <summary>
    /// Register a native OLE drop target on the WebView2 host HWND. WPF's
    /// managed drag-drop never fires over the HwndHost airspace, and with
    /// <c>AllowExternalDrop = false</c> the browser registers no target of its
    /// own — so without this, dropping a file just shows the "no-drop" cursor.
    /// </summary>
    private void RegisterFileDropTarget()
    {
        var hwnd = _webView.Handle;
        if (hwnd == IntPtr.Zero)
        {
            Log.Warning("WV2 drop: host HWND unavailable — file drop disabled");
            return;
        }

        _fileDropTarget = new FileDropTarget(OnFilesDropped);
        var hr = NativeDragDrop.RegisterDragDrop(hwnd, _fileDropTarget);
        if (hr != 0)
        {
            Log.Warning("WV2 drop: RegisterDragDrop failed (0x{Hr:X8})", hr);
            _fileDropTarget = null;
            return;
        }

        _dropTargetHwnd = hwnd;
        Log.Information("WV2 drop: file drop target registered on hwnd 0x{Hwnd:X}", hwnd.ToInt64());
    }

    private void OnFilesDropped(IReadOnlyList<string> paths)
    {
        // OLE invokes the drop target on the thread that registered it — the
        // WPF UI thread — so posting to the WebView2 directly is safe.
        if (paths.Count == 0) return;
        var formatted = FormatDroppedPaths(paths);
        Log.Information("WV2 drop: {Count} path(s) pasted", paths.Count);
        PasteText(formatted);
    }

    /// <summary>
    /// Format file paths for paste into the terminal. One path → bare or
    /// double-quoted if it contains whitespace. Multiple paths → space-
    /// separated, each quoted individually if needed. Matches Windows
    /// Terminal's drag-drop convention.
    /// </summary>
    internal static string FormatDroppedPaths(IReadOnlyList<string> paths)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < paths.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            var p = paths[i];
            if (NeedsQuoting(p))
            {
                sb.Append('"').Append(p).Append('"');
            }
            else
            {
                sb.Append(p);
            }
        }
        return sb.ToString();
    }

    private static bool NeedsQuoting(string path)
    {
        foreach (var c in path)
        {
            if (char.IsWhiteSpace(c)) return true;
        }
        return false;
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
                case "progress":
                    // OSC 9;4 from the agent. State 0 = cleared (idle), any
                    // other value = active. We don't differentiate normal vs
                    // error vs indeterminate at this layer — the activity
                    // detector only needs the boolean.
                    if (doc.RootElement.TryGetProperty("state", out var stateProp)
                        && stateProp.TryGetInt32(out var progressState))
                    {
                        ProgressChanged?.Invoke(this, progressState != 0);
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
