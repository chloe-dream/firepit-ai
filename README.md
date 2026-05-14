# Firepit

> *Summon your agents.*

A local, personal workspace for AI coding agents. Tabs, status indicators, and a project switcher around the CLI tools you already use — without dragging you into a cloud or an editor.

> **Status: V1 feature-complete + V0.5.x platform layer in daily-driver use.** Latest release: `v0.5.11` — see [`CHANGELOG.md`](./CHANGELOG.md). The v0.5 line added per-project `.firepit/` config, the Firepit MCP server, a hidden meta-project, cross-Claude inbox, lazy tab restore, keyboard shortcuts, window placement persistence, and scheduled jobs.

---

## What it is

You're running Claude Code (or Aider, or another agent CLI) across three PowerShell windows on two monitors. You can't remember which one is doing what. Firepit fixes that with a native Windows shell that hosts your agent sessions in tabs, shows you which one is *burning* vs *embers* vs *out*, and gives you a one-click toolbar for the things you constantly need.

It is **not** a cloud orchestrator. It is **not** an IDE. It is a console workspace with manners.

## What it isn't

- A VS Code extension
- An Electron app
- A team / enterprise governance tool (see GitHub's Agent HQ for that layer — Firepit is the local, personal tier)
- macOS or Linux (V1 is Windows-only)

## Features

### V1 core
- **Project list** — auto-discovered from a configurable root directory; folders qualify by containing `CLAUDE.md` or `.claude/`, plus manual entries. The `.firepit` meta-project always pins to top
- **Tabs** — one per active session, drag-to-reorder, terminal search (`Ctrl+Shift+F`), right-click context menu (Close / Close others / Close all)
- **Activity indicator** — *cold* / *igniting* / *burning* / *embers* / *out* per tab, driven by PTY traffic plus Claude Code's `OSC 9;4` thinking signal
- **Toolbar** — Rekindle, Resume Last, Open in Explorer, Open external shell, Configure, plus per-project quick-link buttons and custom commands
- **MCP server registry ("kindling")** — global catalog of MCP servers, activated per project with optional argument / env / header overrides
- **Secret references** — `${env:NAME}` and `${cred:firepit/<key>}` (Windows Credential Manager) tokens resolved at session-start time; raw secrets never sit in `settings.json`
- **Single-instance** — second launch focuses the existing window via a named pipe
- **Lazy tab restore** — on relaunch, only the tab you last had focused starts its session. Click any other tab to wake it up. Cuts cold-start cost roughly in proportion to tab count
- **Window placement** — position, size, and maximized state persist across restarts (off-screen rects ignored)
- **Keyboard shortcuts** — `Ctrl+Shift+T` open project picker, `Ctrl+Shift+W` close tab, `Ctrl+Tab`/`Ctrl+Shift+Tab` and `Ctrl+PgDn`/`Ctrl+PgUp` cycle, `Ctrl+Alt+1..9` jump. Shift-prefixed variants intentionally avoid stomping on bash readline inside the terminal

### V0.5.0 platform layer
- **`.firepit/config.json` per project** — quick-links, MCP activations, agent overrides travel with your repo; gitignore at your discretion
- **Firepit MCP server** — Claude Code can talk to Firepit via `firepit_*` tools and `firepit://*` resources (settings auto-redacted). Bridge ships as `firepit-mcp.exe` next to `Firepit.exe`
- **`.firepit` central project** — first-launch prompt seeds a hidden meta-project where Claude has the firepit MCP wired up out of the box; cross-project notes + inbox live here
- **Cross-Claude inbox** — `firepit_send_to(toProject, …)` drops a message into another project's inbox; the receiving tab shows an unread badge
- **Custom commands** — toolbar buttons defined in `.firepit/config.json`, three types: shell-exec, claude-prompt-injection, url-open
- **Icon flexibility** — 28 bundled named icons + inline SVG path-data fallback (`"icon": "M2 2L10 10z"`)

## Stack

.NET 10 · WPF · WebView2 + xterm.js · ConPTY (via Porta.Pty). Native Windows app, no Electron, no Node runtime at runtime (npm only at build time for the xterm.js bundle).

## Building

Requires the .NET 10 SDK and Node.js (for the xterm.js bundle).

```powershell
dotnet build Firepit.slnx
dotnet test  Firepit.slnx
dotnet run   --project src/Firepit
```

## Configuration

Settings live at `%APPDATA%\Firepit\settings.json`. The file is created the first time you change something via the in-app settings dialog (Firepit → Settings…); other knobs are hand-editable. The shape is described in `SPEC.md §Configuration`. Secrets in MCP entries are referenced via `${env:…}` or `${cred:…}` tokens — never put raw secrets in this file.

State (open tabs) lives at `%LOCALAPPDATA%\Firepit\state.json` (versioned schema). Logs go to `%LOCALAPPDATA%\Firepit\logs\firepit-YYYY-MM-DD.log` (rotating, 14-day retention, 10 MB cap). Logs never include PTY contents — sizes and timestamps only.

## Distribution

```powershell
# Main app
dotnet publish src/Firepit -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true

# MCP stdio bridge — ships next to Firepit.exe
dotnet publish tools/firepit-mcp -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Both publish to `bin/win-x64/` at the repo root (`Firepit.exe` ~160 MB, `firepit-mcp.exe` ~70 MB, both single-file self-contained .NET 10). The Inno Setup installer (`installer/firepit.iss`) packages both. Drop the directory anywhere and run `Firepit.exe`.

The Evergreen WebView2 runtime is required and ships with Windows 10 21H2+ and all Win11. On older systems Firepit will display a download link on first launch.

## Where things live

- [`SPEC.md`](./SPEC.md) — vision, scope, brand language
- [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) — technical contract
- [`docs/ROADMAP.md`](./docs/ROADMAP.md) — V1 delivery plan, milestone-by-milestone
- [`docs/PLATFORM.md`](./docs/PLATFORM.md) — V0.5.0 platform layer reference (per-project config, MCP, meta-project, inbox)
- [`docs/FISHBOWL.md`](./docs/FISHBOWL.md) — Fishbowl integration as canonical per-project MCP example
- [`CHANGELOG.md`](./CHANGELOG.md) — release notes
- [`CLAUDE.md`](./CLAUDE.md) — operational brief for Claude Code sessions working on this repo

## License

MIT — see [`LICENSE`](./LICENSE).

---

*The firepit is cold. Gather a project to begin.*
