# Firepit

> *Summon your agents.*

A local, personal workspace for AI coding agents. Tabs, status indicators, and a project switcher around the CLI tools you already use — without dragging you into a cloud or an editor.

> **Status: pre-alpha, design phase.** No code yet. Specs and a delivery plan only. If you stumbled in here, you're early.

---

## What it is

You're running Claude Code (or Aider, or another agent CLI) across three PowerShell windows on two monitors. You can't remember which one is doing what. Firepit is meant to fix that with a native Windows shell that hosts your agent sessions in tabs, shows you which one is *burning* vs *embers* vs *out*, and gives you a one-click toolbar for the things you constantly need.

It is **not** a cloud orchestrator. It is **not** an IDE. It is a console workspace with manners.

## What it isn't

- A VS Code extension
- An Electron app
- A team / enterprise governance tool (see GitHub's Agent HQ for that layer — Firepit is the local, personal tier)
- macOS or Linux (V1 is Windows-only)

## Stack

.NET 10 · WPF · WebView2 + xterm.js · ConPTY. Native Windows app, no Electron, no Node runtime.

## Where things live

- [`SPEC.md`](./SPEC.md) — vision, scope, brand language
- [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) — technical contract
- [`docs/ROADMAP.md`](./docs/ROADMAP.md) — V1 delivery plan, milestone-by-milestone
- [`CLAUDE.md`](./CLAUDE.md) — operational brief for Claude Code sessions working on this repo

## License

MIT (tentative — see SPEC §License Direction).

---

*The firepit is cold. Gather a project to begin.*
