# Changelog

All notable changes to Firepit. Format roughly follows [Keep a Changelog].
Versioning follows SemVer; pre-1.0 minor bumps may include breaking changes.

## [Unreleased]

## [0.5.8] — 2026-05-13

### Fixed

- **Drag-and-drop files onto the terminal pastes the path** instead of
  opening the file in the embedded Edge layer's preview. Single files,
  multiple files, and folders are all supported; paths with whitespace
  are automatically double-quoted. Matches the Windows Terminal
  convention — useful for sharing images/files with `@<path>` references
  in Claude Code or any agent CLI.

## [0.5.7] — 2026-05-13

### Added

- **Scheduled jobs.** Per-project `.firepit/config.json` gains a
  `scheduledJobs` array — each job pairs a slash-command prompt with a
  cron expression and timezone. A headless runner spawns `claude -p` in the
  project directory, captures stdout/stderr, and writes a JSON record per
  run under `.firepit/runs/<job>/`. Failures, timeouts, and Claude's own
  usage metadata are surfaced in each record. Scheduler honours per-project
  overrides for retention, badge policy, and concurrency, and falls back to
  the platform defaults in `settings.json`.
- **Run-result badges on tabs.** A second amber pill next to the inbox
  badge shows how many run records have arrived since the user last opened
  the runs folder. Policy is `All` or `FailuresOnly` (configurable per
  project). Clicking the badge opens the runs folder in Explorer and marks
  everything as seen. Disabled globally via
  `platform.runBadgesEnabled = false`.
- **Hot-reload for job schedules.** Editing `scheduledJobs` in a project's
  config file invalidates only that project's scheduler state — no full
  restart, no cross-project disruption. Same FileSystemWatcher path that
  already powers quick-link reload.

### Changed

- **Spillover paths for oversized stdout** now default to a project-local
  `.firepit/runs/<job>/stdout-<guid>.log` so the history UI can read the
  full output without extra plumbing. The factory signature gained the full
  `JobRunRequest` so callers can override per project.

## [0.5.6] — 2026-05-13

### Added

- **`Ctrl+PgDn` / `Ctrl+PgUp` cycle tabs** as browser-style alternates to
  `Ctrl+Tab` / `Ctrl+Shift+Tab`. Same handler, just different muscle memory.

### Changed

- **`tabs.autoReloadOnConfigChange` now defaults to `true`.** v0.5.0's
  hot-reload pipeline has been running stable through v0.5.5; the explicit
  "field-test first, flip later" deferral is resolved. Quick-link edits in
  `.firepit/config.json` apply live by default. Existing user settings
  override this default as before — flip back to `false` in `settings.json`
  if you'd rather use the explicit `firepit_reload` MCP tool exclusively.

### Docs

- **README refreshed for v0.5.x** — adds the lazy-tab-restore, window
  placement, keyboard shortcut, and right-click-menu lines to the V1 core
  feature list. Status banner now points at the actual latest release.

## [0.5.5] — 2026-05-12

### Added

- **Window position and size persist across restarts.** Move or resize the
  Firepit window, close it, reopen — it comes back where you left it.
  Maximized state is preserved too (the un-maximized rect is also saved so
  restore-down lands at the right place). State schema gains a nullable
  `window` field; legacy `state.json` files without it fall back to
  CenterScreen + 1180×700 (the previous default). Off-screen rects (e.g.,
  laptop returned from a disconnected dock) are silently ignored.

## [0.5.4] — 2026-05-12

### Added

- **Tab keyboard shortcuts** — `Ctrl+Shift+T` opens the project picker,
  `Ctrl+Shift+W` closes the active tab, `Ctrl+Tab` / `Ctrl+Shift+Tab` cycle
  forward/back, `Ctrl+Alt+1..9` jump to tab N. Shift-prefixed variants
  (instead of plain `Ctrl+W`/`Ctrl+T`) avoid stomping on bash readline's
  delete-word and transpose-chars bindings inside the embedded terminal.
- **Tab right-click menu** — Close, Close others, Close all. Common
  terminal-app UX; each item runs through the same close path with the
  Burning-session confirmation prompt.

### Changed

- **Closing a Burning tab now asks for confirmation** instead of killing the
  agent silently. Mirrors the existing Rekindle-confirm UX. Embers, Dead,
  and Igniting tabs still close without a prompt.

## [0.5.3] — 2026-05-12

### Changed

- **Restored tabs load lazily — only the previously-active tab starts at
  launch.** Other restored tabs sit cold (no spinner, no PTY) until the user
  clicks them. The first click triggers init and shows the spinner. Cuts
  startup CPU + memory roughly in proportion to tab count, and the tab the
  user last had focused is the one Firepit prioritises. State schema gains a
  new `activeTabProjectName` field (nullable, backwards-compatible).

## [0.5.2] — 2026-05-12

### Changed

- **`.firepit` meta-project always pins to top of the project picker** so the
  cross-project hub is one click away regardless of manual entries or alpha
  order. Other discovered projects stay alphabetical; manual entries keep
  their relative ordering after the pin.

### Fixed

- **GitHub quick-link icon now resolves to the Octocat** instead of the
  generic chain-link fallback. Root cause: resource-key case mismatch
  (`IconGitHub` vs the `Capitalise()`-normalised lookup `IconGithub`).
- **Personal GitHub/Fishbowl URLs removed from `FirepitSettings.Defaults`.**
  Previously the defaults shipped with `github.com/chloe-dream/{projectName}`
  and `localhost:7180/p/{projectName}` — author-specific config that
  shouldn't have leaked into the OSS defaults. QuickLinks now start empty;
  configure via `settings.json` globals or per-project `.firepit/config.json`.
  Existing user settings are untouched.

## [0.5.1] — 2026-05-12

### Fixed

- **Installer adds Firepit to user PATH** so `firepit-mcp` is resolvable from any
  shell or ConPTY child without manually editing `settings.json`. Opt-out
  available as a wizard task. Removed cleanly on uninstall. Closes #3, #6.
- **MCP spawn failures now surface in the workspace tab.** Pre-flight PATH
  resolution runs at session start; missing-binary failures show a non-modal
  banner ("⚠ MCP server failed: `<id>` — `<command>` not found on PATH"),
  click to dismiss. Closes #4.
- **Meta-project no longer creates a dead root-level `inbox/`** — actual inbox
  traffic has always gone to `.firepit/inbox/`. Templates (CLAUDE.md, README.md,
  .gitignore) updated to match. Existing meta-projects auto-clean an empty
  legacy `inbox/` on next bootstrap; non-empty ones are left alone. Closes #5.

### Added

- **"Configure" toolbar button** opens the project's `.firepit/config.json`,
  scaffolding a commented JSONC template if missing. Lowest-friction entrypoint
  to the per-project config surface. Closes #9.

### Docs

- **`docs/V1.12-INSTALLER.md` v0.2** — adds the MCP-bridge resolution decision
  and a "first-run end-to-end check" template for future planning docs.
- **`docs/ARCHITECTURE.md` v0.3** — fixes §13/§14 subsection numbering, adds
  the V1.1.4 `progress` bridge message, corrects the `ProjectMcpActivation`
  schema, adds §9.7 (MCP lifecycle errors, symmetric to §4.4), and documents
  `firepit-mcp.exe` in §13 Distribution. Closes #7.
- **`SPEC.md` v0.3** — original V1 vision preserved; tech-stack (`Pty.Net` →
  `Porta.Pty`), architecture diagram, and configuration sections updated in
  place. New "Shipped — what v0.5.0 added beyond this spec" section enumerates
  meta-project, MCP bridge, inbox, commands, hot-reload, OSC 9;4, V1.2 tab
  interactions, and the V1.12 installer. Closes #8.

## [0.5.0] — Firepit as Platform — 2026-05-11

The biggest shift since V1: Firepit becomes a meta-workspace. Per-project
config lives next to your code, Claude can talk to Firepit through MCP, and
a hidden `.firepit` central project becomes your hub for cross-project
work.

### Added

- **Per-project `.firepit/config.json`** — quick-links, MCP activations,
  agent overrides, and session env now live alongside the project. The
  file travels with your repo; gitignore at your discretion. Resolution
  order: defaults → global `settings.json` → per-project file (per-project
  wins).
- **Silent migration** — first launch after upgrade walks
  `settings.Projects[]` and splits behavioural fields out into per-project
  files. Toast confirms; `settings.json.bak` archived.
- **Hot-reload pipeline** — quick-link edits in `.firepit/config.json`
  apply live; MCP / agent / env changes surface a "restart needed" banner
  in the tab toolbar. Optional `tabs.autoReloadOnConfigChange` flag enables
  a debounced `FileSystemWatcher` (off by default).
- **Firepit MCP server** — Claude Code can call `firepit_*` tools to list
  / open / focus / close / reload tabs, and read `firepit://projects`,
  `firepit://sessions`, `firepit://settings` (secrets redacted).
  Architecture: stdio bridge `firepit-mcp.exe` ↔ named pipe ↔ in-process
  GUI host. Up to 8 concurrent client connections.
- **`.firepit` meta-project** — first-launch prompt seeds a hidden
  central project at the projects root (`CLAUDE.md`, `README.md`,
  `.claude/settings.json` preregistering the firepit MCP, `notes/`,
  `inbox/`). Your hub for cross-project knowledge and orchestration.
- **Cross-Claude inbox** — `firepit_send_to(toProject, subject, body)`
  drops a markdown message into `<toProject>/.firepit/inbox/`. Receiving
  project's tab shows an unread-count badge; click opens the inbox folder.
  `FIREPIT_PROJECT_NAME` env var injected at PTY spawn so the bridge can
  populate the `from` field automatically.
- **Custom commands** — `commands[]` in `.firepit/config.json` adds
  toolbar buttons. Three types: `shell` (spawns a process in the project
  dir), `claude-prompt` (injects text into the active session as if you
  typed it), `url` (opens in browser).
- **Icon flexibility** — bundled curated set extended from 8 → 28 named
  icons (Lucide-style minimalist). Inline SVG path-data is also accepted
  (`"icon": "M2 2L10 10z"`) — WPF's mini-language is ~99% SVG-path
  compatible, so most single-path SVGs paste in directly.
- **Tab-switch focus** — switching tabs hands focus directly to the
  embedded terminal. Type immediately, no click.
- **`docs/PLATFORM.md`** — implementation reference for v0.5.0+.
- **`docs/FISHBOWL.md`** — Fishbowl-as-canonical-MCP-integration writeup
  (one team per Firepit project, bearer-per-project pattern).

### Changed

- Versioning convention: doc names drop the `V1.x.y` pattern. New docs
  are named by feature (`PLATFORM.md`, `FISHBOWL.md`) and carry a
  `**Target:** vX.Y.Z` front-matter. Existing `V1.11/12/13`-style docs
  stay as historical artifacts.
- `SPEC.md` MCP examples updated from the older `X-Project` header
  pattern to the bearer-per-project pattern that real adapters use.

### Deferred

- Interactive approval UX for MCP mutations — every tool call is
  currently auto-allowed and logged. `state.json` already carries a
  `ToolApprovals` slot for the prompt-with-memory flow that lands later.
- Settings dialog UI for the new `Platform.*` knobs (hand-edit
  `settings.json` for now).
- Per-message threading / reply UX in the inbox.
- Auto-launch of Firepit GUI when the bridge is invoked but the GUI
  isn't running (today: clear error to Claude).

## [0.4.0] — Reliability + Tab Interactions — 2026-05-10

Two minor doc-tracks (V1.1.4 reliability fixes + V1.2 tab interactions)
shipped together.

### Added

- **Drag-to-reorder tabs** — 8 px hysteresis triggers a drag adorner;
  drop indicator (2 px brand-warm bar) snaps to the nearest gap. Order
  persists across restarts.
- **Terminal search** — `Ctrl+Shift+F` opens an in-tab overlay backed by
  `xterm-addon-search`. Live-search on input, `Enter` / `Shift+Enter`
  navigate, `Esc` closes. Each tab has its own search state.

### Fixed

- **Multi-tab cold-start race** — parallel WebView2 inits under load
  exceeded the 15s ready-handshake timeout for the 4th+ tab, leaving
  zombies that silently dropped keystrokes. WebView2 boot serialised
  through a static `SemaphoreSlim`.
- **Agent activity during thinking** — `ActivityDetector` now hooks
  Claude Code's `OSC 9;4` (ConEmu / Windows Terminal tab-progress)
  emissions via xterm.js's `parser.registerOscHandler`. Tab stays gold
  while the agent thinks even though no bytes are flowing.
- **Maximize chrome clipping** — WPF custom-chrome windows extend their
  client rect past the work area by `ResizeBorderThickness` when
  maximized. Compensated with a matching margin on the root grid; the
  yellow accent on the selected tab is fully visible again.

## [0.3.0] — V1.13 Font Scaling — earlier

- Single `ui.fontSize` setting cascades through caption, tabs, toolbar,
  dialog rows, and the embedded terminal via the bridge.

## [0.2.0] — V1.12 Installer — earlier

- Inno Setup installer with projects-root pre-seed marker.
- `/release` slash command + Version field as source of truth.

## [0.1.0] — V1 GA — earlier

M0–M8 milestones. See `docs/ROADMAP.md`. Functional translation: open
Firepit, see your projects, click each to summon Claude, run real
sessions in parallel, close + reopen and resume seamlessly.

[Keep a Changelog]: https://keepachangelog.com/en/1.1.0/
