namespace Firepit.Core.Blueprints;

/// <summary>
/// Single source for the content of the built-in "firepit" blueprint. Used
/// twice: <see cref="BlueprintStore.EnsureDefaults"/> seeds it to
/// <c>{metaProject}/blueprints/firepit/</c> as editable data, and the
/// fresh-project scaffold (ProjectScaffolding) applies the same conventions
/// directly so brand-new projects are blueprint-conformant from birth.
/// </summary>
public static class FirepitBlueprintDefaults
{
    public const string DefaultBlueprintName = "firepit";

    public const string Description =
        "Standard Firepit project layout: shared-config git hygiene, " +
        "inbox convention, knowledge base.";

    /// <summary>Idempotency marker for the CLAUDE.md inbox section.</summary>
    public const string InboxSectionMarker = "firepit_inbox_complete";

    public const string InboxSection =
        "## Firepit inbox\n\n" +
        "At the start of a session, read any pending messages in " +
        "`.firepit/inbox/*.md` — cross-project notes Firepit routes here. " +
        "Act on each, then mark it done with the `firepit_inbox_complete` " +
        "MCP tool, passing the message's filename as the `id`.\n";

    /// <summary>Idempotency marker for the CLAUDE.md knowledge section.</summary>
    public const string KnowledgeSectionMarker = "firepit_knowledge_search";

    public const string KnowledgeSection =
        "## Firepit knowledge\n\n" +
        "Before researching something that may already be known, query the " +
        "knowledge base with the `firepit_knowledge_search` MCP tool (scope " +
        "`both` covers this project plus the global base). Save durable " +
        "findings with `firepit_knowledge_add` — written in English, per the " +
        "indexing convention. The created markdown files live under " +
        "`.firepit/knowledge/` and are committed like any other file.\n";

    public const string KnowledgeReadmePath = ".firepit/knowledge/README.md";

    public const string KnowledgeReadme =
        "# Knowledge\n\n" +
        "Project knowledge base — research notes, background docs, decisions.\n\n" +
        "Conventions:\n\n" +
        "- One markdown file per topic, written in English (the search index\n" +
        "  embeds English best).\n" +
        "- These files are committed — they are part of the project.\n" +
        "- `knowledge.db` next to this folder is the derived search index:\n" +
        "  gitignored, rebuilt automatically from the markdown at any time.\n" +
        "- Search and add via the Firepit MCP tools `firepit_knowledge_search`\n" +
        "  and `firepit_knowledge_add`.\n";
}
