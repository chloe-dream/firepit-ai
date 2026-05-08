using Firepit.Core.Projects;
using Firepit.Core.Secrets;
using Firepit.Core.Settings;

namespace Firepit.Core.Mcp;

public sealed class SettingsBackedMcpRegistry : IMcpRegistry
{
    private readonly IReadOnlyList<McpRegistryEntry> _entries;
    private readonly IReadOnlyDictionary<string, McpRegistryEntry> _byId;
    private readonly IReadOnlyDictionary<string, (IReadOnlyList<string> Active, IReadOnlyDictionary<string, McpOverride> Overrides)> _projectActivations;
    private readonly ISecretResolver _resolver;

    public SettingsBackedMcpRegistry(FirepitSettings settings, ISecretResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        _entries = (settings.McpServers ?? new Dictionary<string, McpServerSettings>())
            .Select(kvp => MapEntry(kvp.Key, kvp.Value))
            .ToArray();
        _byId = _entries.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

        var projectMap = new Dictionary<string, (IReadOnlyList<string>, IReadOnlyDictionary<string, McpOverride>)>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in settings.Projects ?? [])
        {
            if (project.McpServers is null || project.McpServers.Count == 0)
            {
                continue;
            }
            var overrides = (project.McpOverrides ?? new Dictionary<string, McpOverrideSettings>())
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => MapOverride(kvp.Value),
                    StringComparer.OrdinalIgnoreCase);
            projectMap[project.Path] = (project.McpServers, overrides);
        }
        _projectActivations = projectMap;
    }

    public IReadOnlyList<McpRegistryEntry> All => _entries;

    public McpRegistryEntry? Find(string id) => _byId.TryGetValue(id, out var entry) ? entry : null;

    public IReadOnlyList<ResolvedMcpServer> ResolveForProject(ProjectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_projectActivations.TryGetValue(context.Path, out var activation))
        {
            return [];
        }

        var resolved = new List<ResolvedMcpServer>(activation.Active.Count);
        foreach (var id in activation.Active)
        {
            if (!_byId.TryGetValue(id, out var entry))
            {
                continue;
            }
            activation.Overrides.TryGetValue(id, out var overrideEntry);
            resolved.Add(Resolve(entry, overrideEntry));
        }
        return resolved;
    }

    private ResolvedMcpServer Resolve(McpRegistryEntry entry, McpOverride? overrideEntry)
    {
        var warnings = new List<string>();
        var args = (overrideEntry?.Args ?? entry.Args ?? [])
            .Select(arg => InterpolateInto(arg, warnings, $"args of {entry.Id}"))
            .ToArray();
        var env = MergeMaps(entry.Environment, overrideEntry?.Environment, warnings, $"env of {entry.Id}");
        var headers = MergeMaps(entry.Headers, overrideEntry?.Headers, warnings, $"headers of {entry.Id}");
        var url = entry.Url is null ? null : InterpolateInto(entry.Url, warnings, $"url of {entry.Id}");

        return new ResolvedMcpServer(
            Id: entry.Id,
            DisplayName: entry.DisplayName,
            Transport: entry.Transport,
            Command: entry.Command,
            Args: args,
            Environment: env,
            Url: url,
            Headers: headers,
            ResolutionWarnings: warnings);
    }

    private string InterpolateInto(string value, List<string> warnings, string context)
    {
        var result = SecretInterpolation.Interpolate(value, _resolver);
        foreach (var missing in result.MissingTokens)
        {
            warnings.Add($"unresolved {missing} in {context}");
        }
        return result.ResolvedValue;
    }

    private IReadOnlyDictionary<string, string> MergeMaps(
        IReadOnlyDictionary<string, string?>? baseline,
        IReadOnlyDictionary<string, string?>? overrides,
        List<string> warnings,
        string context)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (baseline is not null)
        {
            foreach (var (key, value) in baseline)
            {
                if (value is null) continue;
                merged[key] = InterpolateInto(value, warnings, context);
            }
        }
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                if (value is null)
                {
                    merged.Remove(key);
                }
                else
                {
                    merged[key] = InterpolateInto(value, warnings, context);
                }
            }
        }
        return merged;
    }

    private static McpRegistryEntry MapEntry(string id, McpServerSettings source) => new(
        Id: id,
        DisplayName: source.DisplayName,
        Transport: ParseTransport(source.Transport),
        Description: source.Description,
        Command: source.Command,
        Args: source.Args,
        Environment: source.Environment,
        Url: source.Url,
        Headers: source.Headers);

    private static McpOverride MapOverride(McpOverrideSettings source) => new(
        Args: source.Args,
        Environment: source.Environment,
        Headers: source.Headers);

    private static McpTransport ParseTransport(string value) => value?.ToLowerInvariant() switch
    {
        "stdio" => McpTransport.Stdio,
        "http"  => McpTransport.Http,
        "sse"   => McpTransport.Sse,
        _       => McpTransport.Stdio,
    };
}
