namespace Firepit.Core.Agents;

public sealed record AgentLaunchSpec(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? EnvironmentOverrides = null);
