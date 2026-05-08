using System.Text.RegularExpressions;
using Firepit.Core.Projects;

namespace Firepit.Core.QuickLinks;

public sealed class QuickLinkService : IQuickLinkService
{
    private static readonly Regex PlaceholderRegex = new(@"\{(\w+)\}", RegexOptions.Compiled);

    private readonly IReadOnlyList<QuickLinkEntry> _globalDefaults;
    private readonly Func<ProjectContext, IReadOnlyList<QuickLinkEntry>> _projectOverrides;

    public QuickLinkService(
        IReadOnlyList<QuickLinkEntry> globalDefaults,
        Func<ProjectContext, IReadOnlyList<QuickLinkEntry>>? projectOverrides = null)
    {
        _globalDefaults = globalDefaults ?? [];
        _projectOverrides = projectOverrides ?? (_ => []);
    }

    public IReadOnlyList<ResolvedQuickLink> ResolveForProject(ProjectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var overrides = _projectOverrides(context);
        var byName = new Dictionary<string, QuickLinkEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _globalDefaults)
        {
            byName[entry.Name] = entry;
        }
        foreach (var entry in overrides)
        {
            byName[entry.Name] = entry;
        }

        var result = new List<ResolvedQuickLink>(byName.Count);
        foreach (var entry in byName.Values)
        {
            if (entry.Disabled)
            {
                continue;
            }
            result.Add(Resolve(entry, context));
        }
        return result;
    }

    private static ResolvedQuickLink Resolve(QuickLinkEntry entry, ProjectContext context)
    {
        string? missing = null;
        var url = PlaceholderRegex.Replace(entry.UrlTemplate, match =>
        {
            var key = match.Groups[1].Value;
            var value = LookupPlaceholder(key, context);
            if (value is null)
            {
                missing ??= key;
                return match.Value;
            }
            return value;
        });

        return new ResolvedQuickLink(
            Name: entry.Name,
            Url: url,
            Target: entry.Target,
            Icon: entry.Icon,
            Available: missing is null && entry.Target == QuickLinkTarget.External,
            UnavailableReason: missing is not null
                ? $"missing variable: {{{missing}}}"
                : entry.Target != QuickLinkTarget.External
                    ? "(V2)"
                    : null);
    }

    private static string? LookupPlaceholder(string key, ProjectContext context) => key switch
    {
        "projectName" => Uri.EscapeDataString(context.Name),
        "projectPath" => Uri.EscapeDataString(context.Path),
        _ => null,
    };
}
