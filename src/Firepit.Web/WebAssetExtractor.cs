using System.IO;
using System.Reflection;

namespace Firepit.Web;

internal static class WebAssetExtractor
{
    private static readonly Dictionary<string, string> ResourceMap = new()
    {
        ["Firepit.Web.Resources.terminal.html"] = "terminal.html",
        ["Firepit.Web.Resources.xterm.js"]      = "xterm.js",
        ["Firepit.Web.Resources.xterm.css"]     = "xterm.css",
        ["Firepit.Web.Resources.addon-fit.js"]  = "addon-fit.js",
    };

    public static string Extract()
    {
        var assembly = typeof(WebAssetExtractor).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(root, "Firepit", "WebAssets", version);
        var marker = Path.Combine(directory, ".extracted");

        if (File.Exists(marker))
        {
            return directory;
        }

        Directory.CreateDirectory(directory);
        foreach (var (resourceName, fileName) in ResourceMap)
        {
            using var source = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' not found in {assembly.GetName().Name}.");
            using var destination = File.Create(Path.Combine(directory, fileName));
            source.CopyTo(destination);
        }

        File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        return directory;
    }
}
