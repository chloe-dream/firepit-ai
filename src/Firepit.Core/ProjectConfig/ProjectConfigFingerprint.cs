using System.IO;
using System.Text.Json;

namespace Firepit.Core.ProjectConfig;

/// <summary>
/// Stable fingerprints over <see cref="ProjectConfig"/> sub-slices, used to
/// detect "is a session restart needed?" without writing a deep-comparer over
/// the nested records (record value-equality doesn't deep-compare lists/dicts).
/// </summary>
public static class ProjectConfigFingerprint
{
    /// <summary>
    /// Hash of the restart-relevant fields (MCP activations, agent, session).
    /// Hot-reloadable fields (Version, Id, QuickLinks) are normalized to zero
    /// so changes there don't move the fingerprint.
    /// </summary>
    public static string ForRestart(ProjectConfig? config)
    {
        if (config is null) return string.Empty;
        var sliced = new ProjectConfig(
            Version: 0,
            Id: null,
            QuickLinks: null,
            McpActivations: config.McpActivations,
            Agent: config.Agent,
            Session: config.Session);
        using var ms = new MemoryStream();
        JsonSerializer.Serialize(ms, sliced, ProjectConfigJsonContext.Default.ProjectConfig);
        return Convert.ToBase64String(ms.ToArray());
    }
}
