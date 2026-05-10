# Firepit as Platform

**Target:** v0.5.0
**Status:** in progress

## Context

After v0.4.0 closes the last reliability gaps from the first dogfood
day, Firepit takes a substantially bigger step: from *shell host* to
*meta-workspace*.

Three structural pieces work together:

1. **Per-project `.firepit/` folder** (analogous to `.claude/`) becomes
   the source of truth for that project's Firepit config — quick-links,
   MCP activations, agent overrides. Travels with the repo; user
   decides per-file what to commit.
2. **Firepit-as-MCP-server** with a deliberately tiny surface: Claude
   edits `.firepit/config.json` directly via its normal file tools,
   and only calls into the MCP to tell Firepit "reload this project's
   config" or "open/focus a tab". Trust surface stays minimal — Claude
   mutates files, Firepit reacts to file changes.
3. **`.firepit` meta-project** at the projects root (hidden by leading
   dot). Becomes the user's central hub for cross-project knowledge,
   pre-wired with the Firepit MCP, and home for the cross-Claude
   inbox convention.

**Why now.** The native config UI surface is at the limit of what
hand-editing `settings.json` gracefully supports. Pushing config into
per-project files makes it team-shareable; pushing self-config into MCP
eliminates the need to build a settings-editor UI in WPF. Both are
paradigm shifts that get harder the longer we wait.

## Versioning convention (effective with this doc)

The pre-v0.4.0 doc-naming chaos (V1.1 → V1.11 → V1.12 → V1.13 → V1.1.4
→ V1.2, with semver tags lagging behind) ends here. Going forward:

- **No more `V1.x.y` doc names for new work.** Each doc names the
  feature: `PLATFORM.md`, `DETACH.md`, `SEARCH.md`, …
- **Every new doc carries `**Target:** vX.Y.Z` front-matter.**
- **Existing docs stay** as historical artifacts (commit-history
  continuity wins over uniformity).
- **`ROADMAP.md` M0–M8 are macro milestones, all done.** Future macro
  initiatives can get M9+ if they warrant it; small features just use
  feature-doc + semver-tag pair.
- **`SPEC.md`'s V1/V2/V3 vocabulary stays** — strategic tiers, not
  release versioning.

## Decisions adopted

- **Cross-Claude inbox: full Phase 5.** MCP sugar tool + file
  convention + optional tab badge with FileSystemWatcher.
- **Migration: silent auto-migrate** of legacy per-project entries on
  first launch after Phase 1, with `.bak` archive and a toast.
- **MCP transport: stdio bridge** (`firepit-mcp.exe` proxying MCP↔named
  pipe). Auth comes free from per-user pipe ACL.
- **Auto-watch FileSystemWatcher: opt-in, off by default in v0.5.0.**
  Explicit `firepit_reload` MCP calls are the canonical path.
- **Inbox project identity:** environment variable
  `FIREPIT_PROJECT_NAME` injected at PTY spawn. Optional `id` field in
  `.firepit/config.json` overrides if present.

## Phasing — five phases, all ship into v0.5.0

The whole feature is one semver bump. Phases are internal execution
order, not release boundaries.

| # | Phase | Days | Risk |
|---|-------|------|------|
| 1 | Per-project `.firepit/config.json` foundation + migration | 2 | low |
| 2 | Hot-reload pipeline (quick-links only) + "Restart needed" affordance | 2 | low |
| 3 | Firepit MCP server (stdio bridge + minimal tool surface) | 5–7 | medium |
| 4 | `.firepit` meta-project bootstrap (first-launch prompt + templates) | 2 | low |
| 5 | Cross-Claude inbox (MCP sugar tool + tab-badge FileSystemWatcher) | 2–3 | low |

---

## Phase 1 — Per-project `.firepit/config.json`

### Schema

```jsonc
{
  "$schema": "https://firepit-ai.dev/schema/firepit-project.v1.json",
  "version": 1,
  "id": null,                         // optional stable identifier; null = use folder name
  "quickLinks": [
    { "name": "GitHub", "url": "https://github.com/me/{projectName}",
      "target": "external", "icon": "github", "disabled": false }
  ],
  "mcpActivations": [
    { "id": "fishbowl",
      "argOverrides": ["--port", "7180"],
      "envOverrides": { "FOO": "bar" },
      "headerOverrides": { } }
  ],
  "agent": {
    "command": "claude",
    "args": ["--continue"],
    "envOverrides": { "ANTHROPIC_API_KEY": "${cred:firepit/api-key}" }
  },
  "session": { "envOverrides": { "PROJECT_TAG": "v2" } }
}
```

### What moves vs what stays

**Moves to `.firepit/config.json`:** `quickLinks` (per-project),
`mcpActivations`, `agent.{command,args,envOverrides}`,
`session.envOverrides`.

**Stays in `%APPDATA%\Firepit\settings.json`:** `projectsRoot`,
`defaultAgent`, `theme`, `tabs.*`, `shells.*`, `terminal.*`, `ui.*`,
`mcpServers` (the catalog — definitions, not activations),
`quickLinks` (global defaults that merge with per-project),
`projects[]` (manual entries — path/name only after migration).

### Resolution order (session start)

1. `FirepitSettings.Defaults`
2. Global `settings.json`
3. `<project>/.firepit/config.json` (wins on conflict for the four
   sections above)

### Migration (one-shot, silent)

For every entry in `settings.Projects[]` with `QuickLinks`, `McpServers`,
`AgentCommand`, or `AgentArgs`:

1. Create `<projectPath>/.firepit/config.json` if missing
2. Write the merged config
3. Strip those fields from the global entry (keep `Path` and `Name`)
4. Save `settings.json.bak` first, then `settings.json`
5. Toast: "Migrated N projects to per-project `.firepit/config.json`"
6. Idempotent flag in `state.json` — migration runs once

### Files

- **NEW:** `src/Firepit.Core/ProjectConfig/ProjectConfig.cs`
- **NEW:** `src/Firepit.Core/ProjectConfig/IProjectConfigStore.cs`
- **NEW:** `src/Firepit.Core/ProjectConfig/JsonProjectConfigStore.cs`
- **NEW:** `src/Firepit.Core/ProjectConfig/ProjectConfigMigrator.cs`
- **NEW:** `src/Firepit.Core/ProjectConfig/FirepitProjectJsonContext.cs`
- **MODIFY:** `src/Firepit/MainWindow.xaml.cs`, `src/Firepit/Views/SessionTab.cs`,
  `src/Firepit.Core/Mcp/SettingsBackedMcpRegistry.cs`

### Reuse

- `JsonSettingsStore` is the template (atomic write + best-effort
  defaults).
- `FirepitJsonContext` source-gen pattern carries over.
- `${cred:...}` / `${env:...}` `ISecretResolver` chain applies
  unchanged to the new `envOverrides`.

---

## Phase 2 — Hot-reload pipeline

### Scope: quick-links only, honest about limits

`SessionTab.RefreshFromConfigAsync(ProjectConfig newConfig)`:

- **Quick-links:** re-resolve, call `_toolbar.SetQuickLinks(...)` again
  (idempotent — no toolbar code change). Live, no agent restart.
- **MCP activations / agent command/args/env:** show a non-modal banner
  in the toolbar (*"Config changed — restart this session to apply"*)
  reusing the existing `_rekindleAffordance` visual. Click →
  `RekindleAsync(resume:true)`.

`IProjectConfigWatcher` (off by default):

- `FileSystemWatcher` per open project on `<path>/.firepit/config.json`
- 500 ms debounce + size-stable check (handles VS Code `.tmp` → rename)
- On change: parse → if valid, fire event → `SessionTab` re-renders
- Toggle in `FirepitSettings.Tabs`: `autoReloadOnConfigChange: false`
- The Phase 3 `firepit_reload` MCP tool calls the same code path
  *regardless* of the toggle

### Files

- **NEW:** `src/Firepit.Core/ProjectConfig/IProjectConfigWatcher.cs`
- **NEW:** `src/Firepit/ProjectConfig/FileSystemProjectConfigWatcher.cs`
- **MODIFY:** `src/Firepit/Views/SessionTab.cs`,
  `src/Firepit/Views/TabToolbar.xaml.cs` (verify idempotency),
  `src/Firepit.Core/Settings/Settings.cs` (add `AutoReloadOnConfigChange`)

---

## Phase 3 — Firepit MCP server (stdio bridge)

### Architecture

```
Claude Code        firepit-mcp.exe         Firepit GUI process
   │                    │                       │
   │  MCP/stdio         │   Named pipe          │
   ├───────────────────▶│  (frames=JSON,        │
   │                    │   length-prefixed)    │
   │                    ├──────────────────────▶│
   │                    │                       │ ─┐
   │                    │                       │  │ UI-thread hop
   │                    │                       │ ◀┘  for mutating tools
   │                    │◀──────────────────────│
   │◀───────────────────│                       │
```

- `firepit-mcp.exe` is ~150–250 LOC: stdio↔pipe proxy, separate
  csproj `tools/firepit-mcp/`, published next to `Firepit.exe`.
- Firepit GUI hosts `Firepit.Mcp.NamedPipeMcpHost` — accepts pipe
  connections, dispatches by frame envelope. Mutating handlers hop to
  `Application.Current.Dispatcher`.
- New named pipe `firepit-mcp` (separate from `firepit-singleton` to
  avoid protocol fork).

### Tool surface

**Mutations (Claude → Firepit):**
- `firepit_reload(projectName, restart?: bool)` — re-reads project
  config, applies hot-reloadable, calls `RekindleAsync` if needed
- `firepit_open_tab(projectName, resume?: bool)`
- `firepit_focus_tab(projectName)`
- `firepit_close_tab(projectName)`
- `firepit_send_to(toProject, subject, body, priority?)` — Phase 5

**Resources (Claude reads):**
- `firepit://projects` — discovery + status
- `firepit://sessions` — open tabs + state
- `firepit://settings` — secrets redacted (`${cred:...}` opaque,
  literal `(?i)(key|token|secret|password)`-keyed values masked `***`)

### Approval UX

Each mutating tool prompts on first call per session per project:
non-modal toast — *Allow once* / *Allow for this project* / *Deny*.
Decisions persist in `state.json` `ToolApprovals`. Settings dialog
gets "Revoke approvals" button.

Read-only resource access is allowed without prompt.

### Files

- **NEW project:** `tools/firepit-mcp/Firepit.Mcp.Bridge.csproj`
- **NEW:** `src/Firepit.Mcp/` (csproj + NamedPipeMcpHost +
  `Handlers/*.cs`)
- **NEW:** `src/Firepit.Core/State/ToolApproval.cs` + extend `AppState`
- **MODIFY:** `installer/firepit.iss`, `src/Firepit/App.xaml.cs`,
  `src/Firepit/MainWindow.xaml.cs`

### Reuse

- `SingletonGuard.cs` is the proven pipe template (NamedPipeServerStream
  + length-prefixed JSON).
- `SingletonCommand` record's tagged-string discriminator generalizes
  cleanly into MCP request envelopes.

---

## Phase 4 — `.firepit` meta-project bootstrap

### First-launch prompt

If `<projectsRoot>/.firepit/` doesn't exist on first launch after
Phase 4 ships, show a non-blocking dialog:

> *Firepit can create a central project at `.firepit/` to help you
> manage settings across all your work via Claude. You can also do
> this later from Settings. Create it now?*

Buttons: **Create**, **Not now**, **Don't ask again**. Idempotent flag
in `state.json`.

### Templates seeded on accept

```
<projectsRoot>/.firepit/
├── CLAUDE.md                          # meta-project brief, MCP tool docs, Fishbowl recommendation
├── README.md
├── .claude/
│   └── settings.json                  # pre-registers firepit MCP
├── .firepit/
│   └── config.json                    # mcpActivations: ["firepit"]
├── notes/
│   └── README.md
└── inbox/
    └── .gitkeep
```

`CLAUDE.md` documents the MCP surface, the inbox convention, and
example workflows. Calls out Fishbowl as recommended per-project
memory store with one-line `init-project` example.

### Files

- **NEW:** `src/Firepit/Views/MetaProjectDialog.xaml(.cs)`
- **NEW:** `src/Firepit/Resources/Templates/MetaProject/` (embedded
  resources per template file)
- **NEW:** `src/Firepit/Services/MetaProjectBootstrapper.cs`
- **MODIFY:** `src/Firepit/MainWindow.xaml.cs` (OnLoaded triggers
  prompt after migration), `src/Firepit.Core/Settings/Settings.cs`
  (add `PlatformSettings`)

### Reuse

- `WebAssetExtractor.cs`'s embedded-resource extraction pattern with
  content-hash cache key applies directly.

---

## Phase 5 — Cross-Claude inbox

### Convention

```
<projectB>/.firepit/inbox/
├── 2026-05-12T09-15-00Z-from-projectA-feature-request.md
└── 2026-05-12T14-30-22Z-from-projectA-bug.md

<projectB>/.firepit/inbox/processed/
└── ...moved by project B's Claude after handling
```

### File format

```markdown
---
from: projectA
to: projectB
subject: Feature request — expose /healthz endpoint
sentAt: 2026-05-12T14:30:22Z
priority: normal              # low | normal | high
threadId: optional-correlation
---

# Body

Free-form markdown.
```

### Project identity

`FIREPIT_PROJECT_NAME` env var injected at PTY spawn (`ConPtyChannel`
already merges adapter-provided env). Value: `Project.Name`, or `id`
from `.firepit/config.json` if set. The `firepit_send_to` MCP handler
reads `from` from this env var server-side — Claude doesn't need to
know its own project name.

### Lifecycle

- **Write:** `firepit_send_to` MCP tool generates filename, writes
  file, returns path so Claude can confirm.
- **Read:** project B's Claude prompted via seed `CLAUDE.md` to check
  `.firepit/inbox/*.md` at session start.
- **Processed:** Claude moves files to `inbox/processed/` itself.
  Firepit doesn't GC.

### Tab badge

`FileSystemWatcher` per open tab on `<project>/.firepit/inbox/`
(depth 1, excludes `processed/`). Unread count rendered as a small
pill adjacent to tab title. Click → `explorer.exe <inboxPath>`.

Toggle: `Settings.Platform.InboxBadgesEnabled: bool`.

### Files

- **NEW:** `src/Firepit.Mcp/Handlers/SendToHandler.cs`
- **NEW:** `src/Firepit.Core/Inbox/InboxWatcher.cs`
- **MODIFY:** `src/Firepit/Views/SessionTab.cs` (badge in header),
  `src/Firepit.Process/ConPtyChannel.cs` (env injection), CLAUDE.md
  template (document the inbox habit)

---

## Fishbowl integration (canonical proof of concept)

Fishbowl is the user's self-hosted personal-memory store with an MCP
surface — see [`docs/FISHBOWL.md`](FISHBOWL.md) for the integration
writeup. It is the *archetypal* per-project Firepit MCP activation:
one Fishbowl team per Firepit project, each with its own bearer token
bound server-side to that team. Switching tabs = switching bearer =
switching memory scope. No per-call `project_id` argument needed —
the token *is* the scope.

**Schema fit (no work in Phase 1).** Existing `settings.Projects[].mcpServers`
+ `mcpOverrides` migrate cleanly into `mcpActivations[].headerOverrides`.

**Bootstrap stays external.** Fishbowl ships `tools/init-project`;
Firepit consumes its output through normal config.

**Meta-project template (Phase 4) recommends Fishbowl** in seed
`CLAUDE.md` with one-line `init-project` example.

---

## Critical files (reference)

- `src/Firepit.Core/Settings/Settings.cs`
- `src/Firepit.Core/Settings/JsonSettingsStore.cs` (store template)
- `src/Firepit.Core/Settings/FirepitJsonContext.cs` (source-gen pattern)
- `src/Firepit.Core/Projects/ProjectDiscovery.cs` — discovery matches
  `.firepit/CLAUDE.md` automatically via existing `["CLAUDE.md", ".claude"]`
  markers; no adapter change needed
- `src/Firepit.Core/Mcp/SettingsBackedMcpRegistry.cs`
- `src/Firepit/Singleton/SingletonGuard.cs` (pipe template)
- `src/Firepit/MainWindow.xaml.cs`
- `src/Firepit/Views/SessionTab.cs`
- `src/Firepit/Views/TabToolbar.xaml.cs`
- `src/Firepit.Process/ConPtyChannel.cs` (env injection point)
- `src/Firepit.Web/WebAssetExtractor.cs` (template extraction pattern)
- `installer/firepit.iss`

## Verification

### End-to-end happy path (manual, after Phase 5)

1. Install v0.5.0. First launch:
   - Toast shows N projects migrated from `settings.json`
   - Meta-project prompt appears; accept
   - `<projectsRoot>/.firepit/` exists with all templates
2. Open `.firepit` tab, ask Claude:
   - "list my projects" → calls `firepit://projects`
   - "add a Linear quick-link to all my projects" → edits each
     `<project>/.firepit/config.json`, calls `firepit_reload`,
     toolbar updates without rekindle
3. From `dream-tools` Claude: "send a feature request to firepit-ai
   asking for dark-mode toggle support" → file lands in `firepit-ai`'s
   inbox, tab gets badge "1"
4. Click badge → Explorer opens at inbox

### Unit / integration tests

- `Firepit.Core.Tests/ProjectConfig/JsonProjectConfigStoreTests.cs`
- `Firepit.Core.Tests/ProjectConfig/MigrationTests.cs`
- `Firepit.Mcp.Tests/HandlerTests.cs`
- `Firepit.Process.Tests` — `FIREPIT_PROJECT_NAME` propagation

### Regression smoke

- 14 `ActivityDetector` tests
- 4-tab cold-start race (V1.1.4)
- Drag-reorder + search (V1.2)
- Maximize accent visible (V1.1.4)

### Release flow

After Phase 5 green: `/release` → tag `v0.5.0` → CI publishes installer
+ single-file Firepit.exe + firepit-mcp.exe to GitHub Release.
