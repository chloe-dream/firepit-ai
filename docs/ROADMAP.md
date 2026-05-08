# Roadmap

V1 delivery plan for Firepit. Read after `SPEC.md` (vision) and `ARCHITECTURE.md` (technical contract).

This document is structured for autonomous execution by Claude Code. Each milestone has:

- **Goal** — one sentence
- **Deliverables** — concrete files / behaviors
- **Interfaces** — pseudo-signatures or message shapes that are binding for downstream milestones
- **Acceptance** — testable criteria; if these don't pass, the milestone isn't done
- **Out of scope** — explicit reminders

Milestones are ordered. Skipping is allowed only if a later milestone doesn't depend on an earlier one (most do). When a milestone defers an open decision to a later one, the deferral is named.

---

## V1 Success Definition

> The author replaces three permanently-open PowerShell windows with one Firepit window for daily Claude-Code work and never goes back.

Functional translation: open Firepit, see at least three projects, click each to summon Claude Code, run real coding sessions in parallel, close Firepit at end of day, reopen tomorrow and resume seamlessly. Indicator clearly shows which session needs attention.

If V1 doesn't achieve that, do not start V2.

---

## M0 — Solution Skeleton

**Goal:** A buildable .NET 10 solution with the project structure from ARCHITECTURE §1, CI green on Windows.

**Deliverables**

- `Firepit.sln` referencing the five projects
- `src/Firepit/` — WPF app, blank `MainWindow` showing literal "Firepit" text, exits cleanly
- `src/Firepit.Core/` — class library, no references
- `src/Firepit.Process/` — class library, references Core
- `src/Firepit.Web/` — class library, references Core, includes `Resources/` folder
- `src/Firepit.Adapters/` — class library, references Core
- `tests/` — three xUnit projects: `Firepit.Core.Tests`, `Firepit.Process.Tests`, `Firepit.Adapters.Tests`
- `.github/workflows/ci.yml` — runs `dotnet build` + `dotnet test` on `windows-latest`
- `Directory.Build.props` — sets `LangVersion`, `Nullable=enable`, `TreatWarningsAsErrors=true`, target framework

**Acceptance**

- `dotnet build` succeeds with zero warnings
- `dotnet test` runs (zero tests is fine; framework just needs to be wired)
- `dotnet run --project src/Firepit` opens a window showing "Firepit"
- CI green on a fresh push to `main`

**Out of scope:** any actual functionality. This is plumbing.

---

## M1 — ConPTY + One Terminal Tile

**Goal:** A single hardcoded tab in the WPF window runs a real PowerShell session via ConPTY rendered in xterm.js. The user can type and see output.

**Deliverables**

- `Firepit.Core`:
  - `ITerminalView`, `IPtyChannel`, `IActivityClock` interfaces (per ARCHITECTURE §2)
  - `SystemActivityClock : IActivityClock`
- `Firepit.Process`:
  - `ConPtyChannel : IPtyChannel` using Pty.Net (or direct P/Invoke if needed)
  - `ProcessHost` that spawns the child and owns the channel lifecycle
- `Firepit.Web`:
  - `Resources/terminal.html`, `Resources/xterm.bundle.js`, font file — all `EmbeddedResource`
  - `Resources/build.ps1` (or npm scripts in `package.json`) producing `xterm.bundle.js`
  - `WebAssetExtractor` — extracts embedded resources to `%LOCALAPPDATA%\Firepit\WebAssets\<version>\` once per version
  - `WebView2TerminalView : ITerminalView` — owns a `WebView2` control, virtual-host-mapped per ARCHITECTURE §3.1, implements the bridge protocol from §3.3
- `Firepit/MainWindow`:
  - Hosts a single `WebView2TerminalView`
  - On load, spawns `powershell.exe -NoLogo` via `ProcessHost`, wires bytes both ways

**Bridge protocol (binding for all later milestones)**

Host → web messages:

```jsonc
{ "type": "data",   "b64": "<base64-of-bytes>" }
{ "type": "resize", "cols": 120, "rows": 40 }
{ "type": "theme",  "vars": { "--bg": "#1a1612", "--fg": "#e8e2d8", ... } }
```

Web → host messages:

```jsonc
{ "type": "ready" }
{ "type": "input",  "b64": "<base64-of-bytes>" }
{ "type": "resize", "cols": 120, "rows": 40 }
```

Anything else is dropped and logged at Warning.

**Acceptance**

- Launch app → terminal renders, prompt visible
- Type `Get-Date`, press Enter → date prints
- Resize the window → terminal reflows, `ResizePseudoConsole` is called
- Close window → child process exits cleanly (no orphaned `powershell.exe` in Task Manager)
- `Firepit.Process.Tests` has at least one integration test launching `cmd /c echo hi` end-to-end and asserting stdout contains "hi"

**Out of scope:** tabs, project list, activity indicators, configuration, anything visual beyond a single terminal filling the window.

**Open decision resolved here:** Pty.Net vs. direct P/Invoke. Whichever is used, document the choice in `ARCHITECTURE.md §4.1` and remove the alternative.

---

## M2 — Tabs + Project List

**Goal:** A left sidebar lists projects discovered under a hardcoded `projectsRoot`. Clicking a project opens a tab; tab shows the project name. Multiple tabs can exist; only the active tab is mounted.

**Deliverables**

- `Firepit.Core`:
  - `Project`, `Session`, `ProjectContext` records
  - `IAgentAdapter` interface (per ARCHITECTURE §2.3)
  - `IProjectDiscovery` service interface
- `Firepit.Adapters`:
  - `ClaudeCodeAdapter : IAgentAdapter` — markers `["CLAUDE.md", ".claude"]`, builds a launch spec for `claude` (continue flag wired but unused yet)
- `Firepit.Core` (impl):
  - `ProjectDiscovery : IProjectDiscovery` — scans one level deep under `projectsRoot`, asks each registered adapter
- `Firepit/Views`:
  - `ProjectListView` (left sidebar, MVVM via `ObservableCollection<ProjectViewModel>`)
  - `TabHostView` (right area, holds a `TabControl`)
  - Tab content is a `WebView2TerminalView` (lazily mounted when tab first activates)
- `MainWindow`:
  - Hosts `ProjectListView` + `TabHostView` in a two-column `Grid`
  - On project click: open or focus tab; if new, spawn agent via `ProjectContext` + `ClaudeCodeAdapter.BuildLaunchSpec`

**Hardcode for this milestone**

- `projectsRoot = "D:\\Code\\Projects"` (or similar — make it easy to change in code; M5 makes it config)
- `defaultAgent = "claude"` (resolved via PATH)

**Acceptance**

- Run app → see at least the test project on the sidebar (drop a folder with a `CLAUDE.md` under `projectsRoot`)
- Click project → tab opens, terminal mounts, `claude` runs in that folder's working directory
- Click another project → second tab opens, second `claude` process runs in that folder
- Switch between tabs → both processes still alive; UI shows the focused one
- Closing a tab kills only that session's child process

**Out of scope:** activity indicators (M3), toolbar (M4), configuration file (M5), session resume (M4).

---

## M3 — Activity Detection + State Visuals

**Goal:** Each tab shows its session state visually. Burning sessions are bright, embers are dimmed-warm, dead sessions are grey. Indicator updates within ~250 ms of state changes.

**Deliverables**

- `Firepit.Core`:
  - `SessionState` enum
  - `ActivityDetector` — owns the state machine from ARCHITECTURE §6, driven by `IActivityClock` and a `System.Threading.Timer`
  - Subscribes to PTY-read events via the `IPtyChannel`
- `Firepit.Process`:
  - `IPtyChannel` exposes a `read-observed` event/hook (or the channel hands timestamps with bytes)
- `Firepit/Views`:
  - Tab header template binds to `SessionState` and applies brand-styled visuals (bright = burning, dim warm = embers, grey = dead, warming = igniting)
- `Firepit.Core.Tests`:
  - State-machine tests using a `FakeActivityClock` driving deterministic transitions

**Hysteresis defaults**

```
burnWindowMs       = 500
idleThresholdMs    = 1500
ignitingTimeoutMs  = 10000
```

(Configurable in M5; hardcoded here.)

**Acceptance**

- Open a project → tab shows "igniting" briefly, then "burning" once `claude` prints its banner
- Stop typing for 2+ seconds → tab dims to "embers"
- Type something → tab brightens to "burning" within ~250 ms
- Kill `claude` from outside (or `:exit`) → tab transitions to "dead"
- Burning ↔ embers transitions do not flicker on slowly-streaming output (test by piping a slow stream through a fake adapter)

**Out of scope:** persisting state, dead-session "rekindle" UI (M4 covers the toolbar that does this).

---

## M4 — Toolbar Actions + Quick-Links

**Goal:** The four core V1 toolbar actions plus per-project quick-link buttons all work from the active tab. Quick-links are stateless URL openers in M4 (data-driven from M5 onwards).

**Deliverables**

- `Firepit/Views`:
  - `TabToolbar` — four built-in buttons with brand-language tooltips ("Rekindle this session", "Resume last session", "Open in Explorer", "Open external shell")
  - Quick-link region in the toolbar — a horizontal strip of buttons that collapses into a dropdown when more than four entries
- Action handlers:
  - **Rekindle**: tear down the current session (kill child, dispose PTY), start a new one with the same `ProjectContext`. No `--continue`.
  - **Resume Last**: tear down + restart with `AgentLaunchOptions(Resume: true)` → adapter adds `--continue`.
  - **Open in Explorer**: `Process.Start("explorer.exe", projectPath)`.
  - **Open External Shell**: try `Process.Start("wt.exe", $"-d {projectPath}")`; on failure, fall back to `powershell.exe -NoExit -Command "Set-Location '<path>'"`.
- Quick-link service:
  - `Firepit.Core.QuickLinks.IQuickLinkService` per ARCHITECTURE §10
  - Resolution merges global templates with per-project lists; `{projectName}` and `{projectPath}` substitution at click time
  - `Open()` calls `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`
  - **M4 hardcodes** an in-memory default list (`GitHub`, `Fishbowl`) for dogfooding; M5 wires the same shape to settings.json
  - `QuickLinkEntry.Target` enum exists with `External` as the only legal V1 value; any other value renders the button disabled with a "(V2)" tooltip
- Dead-tab UX:
  - Dead tabs show a center-of-tab "rekindle" affordance ("This session went out. Click to rekindle.")
  - Tab toolbar's Rekindle button is enabled in any state (Burning/Embers/Dead); confirmation dialog only when killing a Burning session

**Acceptance**

- Rekindle a Burning session → child PID changes, terminal clears (or restarts), state cycles Igniting → Burning
- Resume Last → child PID changes, command line includes `--continue`
- Open in Explorer → Explorer window opens at the project path
- Open External Shell → on a system with `wt.exe`, opens Windows Terminal in the project folder; on a system without, opens PowerShell with the path set
- Killing a Burning session prompts for confirmation
- Quick-links: clicking the GitHub button opens `https://github.com/<configuredOwner>/<projectName>` in the default browser; clicking Fishbowl opens the templated URL with `{projectName}` substituted
- A quick-link with an unknown placeholder is shown disabled, hover tooltip names the missing variable

**Out of scope:** session history dropdown (V2), session resume by ID (V2), quick-link sub-tab hosting (V2 §Project Sub-Tabs).

---

## M5 — Configuration Foundation

**Goal:** Settings persist in `%APPDATA%\Firepit\settings.json` per ARCHITECTURE §7 and SPEC §Configuration. The hardcoded `projectsRoot` from M2 and the in-memory quick-link list from M4 are now read from config.

**Deliverables**

- `Firepit.Core`:
  - `Settings`, `TabSettings`, `ShellsSettings`, `ProjectEntry` immutable records (System.Text.Json source-gen attributes for AOT-friendliness)
  - `QuickLinkEntry` record (the schema defined in ARCHITECTURE §10.1) — read from config now, validated against the `Target` enum at load time
  - `Settings.Defaults` static
  - `ISettingsStore` interface; `JsonSettingsStore` implementation
- First-launch behavior:
  - File does **not** exist by default. Defaults are in-memory.
  - First time the user changes anything (M5: only `projectsRoot` setting matters for V1), the file is written.
- Minimal Settings UI (V1):
  - A single "Settings" menu item opening a small window with one input: projects root path, with a folder picker. Save and reload discovery.
  - Other settings remain editable by hand-editing the JSON file. Document this in the README.
- Adapter resolution:
  - Per-project `agentCommand` / `agentArgs` overrides honored if present
  - Otherwise `defaultAgent` from settings

**Acceptance**

- First launch on a clean machine → no settings file exists; defaults apply
- Change projects root via the Settings dialog → file is created, change persists across restarts
- Edit `tabs.activityIdleThresholdMs` by hand-editing the JSON → restart → new threshold takes effect (verified with the M3 hysteresis test)
- Add a manual `projects[]` entry pointing to a folder outside `projectsRoot` → it appears in the sidebar
- Add `quickLinks` entries to the JSON file → they appear in the toolbar after restart; per-project entries override global by name

**Out of scope:** hot-reload on file change, settings migration, validation UI. Bad JSON: log warning, fall back to defaults, surface a non-blocking banner. MCP registry data lives in this same file but is consumed in M6.

---

## M6 — MCP Server Registry & Per-Project Activation

**Goal:** Projects can declare an active set of MCP servers from a global registry, and the Claude Code adapter projects that set into Claude's expected configuration at session start. Sessions launched in M6 actually have the activated MCP servers available to the agent.

**Deliverables**

- `Firepit.Core.Mcp`:
  - `McpRegistryEntry`, `McpProjectActivation`, `McpOverride`, `ResolvedMcpServer` records per ARCHITECTURE §9.1
  - `IMcpRegistry` service per §9.2 with `JsonBackedMcpRegistry` implementation reading from the same `settings.json` foundation introduced in M5
- `Firepit.Core.Secrets`:
  - `ISecretResolver` with two providers: `EnvironmentSecretProvider` (`${env:NAME}`) and `CredentialManagerSecretProvider` (`${cred:firepit/<key>}`)
  - The Credential Manager P/Invoke (`CredRead`) lives in `Firepit.Process` and is wired in via DI
- `Firepit.Adapters.ClaudeCode`:
  - `ClaudeCodeMcpProjector : IAgentMcpProjector` — translates a `ResolvedMcpServer` list into Claude's expected format. Implementation choice (write `.claude/mcp.json` vs. run `claude mcp add` invocations) decided during M6 based on what Claude Code's CLI supports cleanly at the time. Document the choice in ARCHITECTURE §9.3.
- Session lifecycle integration:
  - On session start, before the agent is spawned, the projector applies the active MCP set for the project
  - Resolution warnings (missing tokens, unrecognized ids) surface as a non-blocking banner in the tab; the session still launches
  - Rekindle re-resolves (so secret rotation in Credential Manager picks up without a Firepit restart)
- Settings UI (minimal):
  - The settings JSON file is still the canonical edit surface for the registry. M6 ships a "View MCP Servers" panel that lists registry entries and shows which are active for the currently focused project — read-only. Editing happens by hand for V1; the panel proves the registry is loaded correctly.

**Acceptance**

- Add an MCP entry to `settings.json` (e.g. a stdio server that just echoes a tool list) → restart Firepit → the panel lists it
- Activate the entry on a project → start a session → the agent sees the MCP server's tools (verified by the agent's own `tools` listing or equivalent)
- Override the entry's `args` per project → restart that project's session → the override takes effect (verified by inspecting what Claude Code received)
- Reference a `${cred:firepit/test-token}` token; provision the credential via `cmdkey /add:firepit/test-token /user:firepit /pass:<value>`; restart session → token resolved into the request
- Provision NO credential → restart session → entry skipped, banner shows the resolution warning, session still starts

**Out of scope:** registry editing UI (V1.1), public MCP marketplace discovery (V1.1+), DPAPI in-file secrets (V1.1), tool-list pre-validation (V1.1).

**Brand language:** the panel is labeled *"Kindling"* in user-facing copy; tooltips read "the kindling you stack to start each fire". Code identifiers stay neutral (`McpRegistryPanel`, `McpProjectActivation`).

---

## M7 — Single-Instance + Tab Restoration

**Goal:** Launching `firepit.exe` while it's already running brings the existing window forward. Tabs that were open at last close are restored on next launch (per opt-in setting).

**Deliverables**

- `Firepit/Bootstrap`:
  - Named-pipe singleton per ARCHITECTURE §11
  - On second launch: connect to pipe, send `{ "command": "focus" }`, exit with success
  - On first launch: become the pipe server; main window subscribes to pipe messages
  - Pipe protocol supports `{ "command": "summon", "project": "<name>" }` even if no CLI yet uses it (V2)
- Tab restoration:
  - `state.json` in `%LOCALAPPDATA%\Firepit\` is a versioned schema (`{ "version": 1, "tabs": [{ "projectName": "...", "lastSessionResumable": true }] }`) — see ARCHITECTURE §17.3
  - Saved on graceful shutdown; loaded on startup if `tabs.persistAcrossRestarts` is true
  - Restored tabs auto-summon their agent (with `--continue` if `lastSessionResumable`)

**Acceptance**

- Launch Firepit → open three tabs → close → relaunch → same three tabs reappear and resume their sessions
- Launch Firepit → with the window already running, double-click the launcher again → existing window comes to front, no second process
- Force-killing the running instance leaves a stale pipe? On next launch, detect orphan and recover (try-connect with timeout, fall through to server mode)

**Out of scope:** scrollback restoration (V2 via `xterm-addon-serialize`).

---

## M8 — Polish + Dogfood Hardening

**Goal:** Three full days of the author using Firepit as the only entrypoint to Claude Code. Bugs found during dogfooding either fixed in this milestone or moved to a V1.1 backlog.

**Deliverables**

- README with installation, configuration, screenshots, and the brand-voice tone
- LICENSE file (MIT, per SPEC §License Direction unless changed)
- A first GitHub Release (`v0.1.0`) with the published archive attached
- Logging fully wired per ARCHITECTURE §12
- Crash safety pass: any unhandled exception → log + non-blocking error toast in the UI, never a silent crash
- Performance pass: confirm the bridge keeps up under heavy output (large `git log`, big diff). If not, switch to the host-object path per ARCHITECTURE §3.3 — this is the only milestone allowed to make that switch.
- Visual pass: dark theme tokens applied consistently, monospace font verified, indicator colors cohesive with brand

**Acceptance (the only one that matters)**

- The author works exclusively in Firepit for three full days running real Claude Code sessions across at least three projects, doesn't open a separate PowerShell for Claude Code use, and doesn't lose work or get stuck.
- A V1.1 backlog file lists every paper-cut found, ranked.

**Out of scope:** anything in V2/V3.

---

## Cross-Milestone Conventions

These apply throughout — call them out in PRs/commits:

- **Brand-vocab discipline.** User-facing strings use SPEC §Brand Language. Code identifiers stay neutral.
- **Test seams over mocks.** Use `IPtyChannel`, `IActivityClock`, `IAgentAdapter` to avoid mocking ConPTY/WebView2/`Process`.
- **No PTY content logging.** Sizes and timestamps only.
- **Each milestone ends with a tag.** `m0-skeleton`, `m1-conpty`, ... — easy bisect when V1.1 finds regressions.

---

## V2 (Reference, Not Committed)

Listed in dependency order so M2-onwards can leave hooks where helpful, not so V2 starts before V1 is done:

1. **Project sub-tabs (the cockpit)** — the layout shift that turns each project tab into a multi-pane workspace; the items below are panes inside it. Quick-link entries with `target = "subTab"` become hostable here. See SPEC §V2 *Project Sub-Tabs* and ARCHITECTURE §17.1.
2. File browser pane (tree + grid toggle, FileSystemWatcher refresh)
3. Markdown viewer (in-pane, mermaid, syntax-highlighted code, no editing)
4. Image viewer (with EXIF, zoom/pan)
5. Embedded PowerShell (second pane, reuses ConPTY infrastructure)
6. Session history dropdown (`--resume <id>` per adapter)
7. Scrollback restoration (`xterm-addon-serialize`)

V3 (image AI) is deliberately not detailed here. It will get its own roadmap when V2 is in active personal use.

---

*Document version: 0.2 — adds quick-links to M4, splits configuration foundation (M5) from MCP registry (M6), promotes project sub-tabs to top of V2*
