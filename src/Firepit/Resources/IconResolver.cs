using System.Windows;
using System.Windows.Media;

namespace Firepit.Resources;

/// <summary>
/// Resolves an icon string to a <see cref="Geometry"/>. Two paths:
/// <list type="bullet">
///   <item>Named icon from <c>Icons.xaml</c> (lookup by capitalised key,
///         e.g. "play" → "IconPlay")</item>
///   <item>Inline SVG path-data, detected by a leading <c>M</c> or <c>m</c> —
///         parsed via <see cref="Geometry.Parse"/>. WPF's mini-language is
///         very close to SVG path data, so most single-path SVG icons paste
///         in directly.</item>
/// </list>
/// Falls back to <c>IconLink</c> on any failure.
/// </summary>
public static class IconResolver
{
    private const string FallbackKey = "IconLink";

    public static (Geometry Geometry, IconMode Mode) Resolve(string? hint, string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(hint))
        {
            // Path-data detection: a string that starts with M/m is almost
            // certainly SVG-ish geometry. Parse it.
            var trimmed = hint.TrimStart();
            if (trimmed.Length > 1 && (trimmed[0] == 'M' || trimmed[0] == 'm'))
            {
                try { return (Geometry.Parse(trimmed), IconMode.Stroke); }
                catch { /* fall through to named lookup */ }
            }
            if (TryLookupNamed(hint, out var named)) return named;
        }
        if (TryLookupNamed(fallbackName, out var fallback)) return fallback;
        return (LookupOrThrow(FallbackKey), IconMode.Stroke);
    }

    private static bool TryLookupNamed(string nameHint, out (Geometry Geometry, IconMode Mode) result)
    {
        var key = "Icon" + Capitalise(nameHint.Trim());
        var geometry = Application.Current?.TryFindResource(key) as Geometry;
        if (geometry is null)
        {
            result = default;
            return false;
        }
        result = (geometry, ModeFor(key));
        return true;
    }

    private static Geometry LookupOrThrow(string key) =>
        (Geometry)Application.Current.FindResource(key);

    private static IconMode ModeFor(string key) => key switch
    {
        "IconGitHub"   => IconMode.Fill,
        "IconFishbowl" => IconMode.Fill,
        _              => IconMode.Stroke,
    };

    private static string Capitalise(string s)
    {
        if (s.Length == 0) return s;
        return char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
    }
}

public enum IconMode { Stroke, Fill }
