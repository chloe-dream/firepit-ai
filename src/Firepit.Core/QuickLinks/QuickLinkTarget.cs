namespace Firepit.Core.QuickLinks;

public enum QuickLinkTarget
{
    External,
    SubTab, // V2+ — branching on this in V1 logic is forbidden, see ARCHITECTURE §17.2
}
