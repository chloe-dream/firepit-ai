namespace Firepit.Core.Agents;

public sealed record AgentLaunchOptions(
    bool Resume = false,
    string? SessionId = null);
