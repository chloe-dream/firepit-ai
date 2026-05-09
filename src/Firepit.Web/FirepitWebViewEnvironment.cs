using System.IO;
using System.Runtime.Versioning;
using Microsoft.Web.WebView2.Core;

namespace Firepit.Web;

[SupportedOSPlatform("windows10.0.17763.0")]
public static class FirepitWebViewEnvironment
{
    private static readonly object Gate = new();
    private static Task<CoreWebView2Environment>? _initialization;

    public static Task<CoreWebView2Environment> GetAsync()
    {
        lock (Gate)
        {
            return _initialization ??= CreateAsync();
        }
    }

    private static async Task<CoreWebView2Environment> CreateAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Firepit",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }
}
