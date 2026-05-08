namespace Firepit.Core.QuickLinks;

public sealed record ResolvedQuickLink(
    string Name,
    string Url,
    QuickLinkTarget Target,
    string? Icon,
    bool Available,
    string? UnavailableReason);
