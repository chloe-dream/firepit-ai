using System.IO;

namespace Firepit.Core.Projects;

/// <summary>
/// Derives a project's display name from its folder path.
/// </summary>
public static class ProjectNaming
{
    /// <summary>
    /// The last path segment, e.g. <c>C:\repos\foo</c> → <c>foo</c>.
    /// <see cref="Path.GetFileName(string)"/> returns EMPTY for a UNC share
    /// root (<c>\\server\share</c>) and drive roots — .NET treats them as roots
    /// — which left network-directory projects with a blank tab. Fall back to
    /// the last non-empty segment so <c>\\nas\music</c> → <c>music</c>.
    /// </summary>
    public static string DeriveName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        // UNC share root / drive root — GetFileName is empty. Take the last
        // segment: \\nas\music → "music", \\nas → "nas".
        var segments = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[^1] : trimmed;
    }
}
