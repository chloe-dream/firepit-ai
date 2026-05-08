namespace Firepit.Core.QuickLinks;

public sealed record QuickLinkEntry(
    string Name,
    string UrlTemplate,
    QuickLinkTarget Target = QuickLinkTarget.External,
    string? Icon = null,
    bool Disabled = false);
