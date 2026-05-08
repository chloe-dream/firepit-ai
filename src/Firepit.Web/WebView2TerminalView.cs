using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using Firepit.Core.Terminal;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Firepit.Web;

[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WebView2TerminalView : ITerminalView
{
    private static readonly Uri TerminalUri = new("https://firepit.local/terminal.html");

    private readonly WebView2 _webView = new();
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _initialized;
    private bool _disposed;

    public event EventHandler<ReadOnlyMemory<byte>>? InputReceived;
    public event EventHandler<TerminalSize>? Resized;

    public WebView2 Element => _webView;

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        var assetsDir = WebAssetExtractor.Extract();
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Firepit",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);

        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder)
            .ConfigureAwait(true);
        await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            hostName: "firepit.local",
            folderPath: assetsDir,
            accessKind: CoreWebView2HostResourceAccessKind.Allow);

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.Source = TerminalUri;

        await _readyTcs.Task.WaitAsync(ct).ConfigureAwait(true);
        _initialized = true;
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
                        Resized?.Invoke(this, new TerminalSize(cols, rows));
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
