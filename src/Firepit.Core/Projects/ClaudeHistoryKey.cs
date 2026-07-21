using System.IO;
using System.Text;

namespace Firepit.Core.Projects;

/// <summary>
/// Claude Code keys per-project chat history and auto-memory by an encoded
/// form of the project's absolute path — every character outside [A-Za-z0-9]
/// becomes '-' — stored under <c>~/.claude/projects/</c>, e.g.
/// <c>D:\repos\firepit-ai</c> → <c>D--repos-firepit-ai</c>. Renaming a
/// project folder without moving that directory silently orphans history AND
/// auto-memory, which is why firepit_rename_project migrates it as part of
/// the rename cascade.
/// </summary>
public static class ClaudeHistoryKey
{
    public static string Encode(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(absolutePath);
        var sb = new StringBuilder(absolutePath.Length);
        foreach (var ch in absolutePath)
        {
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        }

        return sb.ToString();
    }

    /// <summary><c>{userProfileDir}/.claude/projects/{encoded}</c> for the
    /// given project path.</summary>
    public static string GetHistoryDir(string userProfileDir, string projectPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(userProfileDir);
        return Path.Combine(
            userProfileDir, ".claude", "projects", Encode(Path.GetFullPath(projectPath)));
    }
}
