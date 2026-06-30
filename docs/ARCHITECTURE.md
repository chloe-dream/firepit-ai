# Architecture

Technical detail for Firepit V1. Companion to `SPEC.md` (product vision) and `ROADMAP.md` (delivery plan). Read SPEC first.

This document is the contract for *how* V1 is built. Decisions here are binding; deviations need a written reason.

> **Last verified against:** v0.5.x (2026-05-11). Document revision 0.3.

---

## 1. Solution Layout

```
firepit-ai/
├── Firepit.sln
├── src/
│   ├── Firepit/                # WPF host (.exe entry, App.xaml, MainWindow, Views)
│   ├── Firepit.Core/           # Domain models, abstractions — NO UI deps
│   ├── Firepit.Process/        # ConPTY wrapper, agent process lifecycle
│   ├── Firepit.Web/            # WebView2 hosting + xterm.js bundle (embedded resources)
│   └── Firepit.Adapters/       # Per-agent adapters (V1: ClaudeCode only)
├── tests/
│   ├── Firepit.Core.Tests/
│   ├── Firepit.Process.Tests/
│   └── Firepit.Adapters.Tests/
└── docs/
```

**Reference rules** (enforced by review, not yet by analyzers):

- `Firepit.Core` references nothing in this solution.
- `Firepit.Process` references `Firepit.Core`.
- `Firepit.Adapters` references `Firepit.Core`.
- `Firepit.Web` references `Firepit.Core`.
- `Firepit` (WPF) references all of the above.

If you find yourself adding `Firepit.Web` → `Firepit.Process`, stop. The shell wires them; lower layers do not know each other.

---

## 2. Core Abstractions (`Firepit.Core`)

### 2.1 `ITerminalView`

The single contract between tab UI and terminal renderer. V1 has one implementation; V2+ may add a native renderer.

```csharp
public interface ITerminalView
{
    // Bytes from the agent → display
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    // Bytes from the user (keystrokes, paste) → process
    event EventHandler<ReadOnlyMemory<byte>> InputReceived;

    // Resize hint from the renderer (cols, rows)
    event EventHandler<TerminalSize> Resized;

    // Lifecycle
    Task InitializeAsync(CancellationToken ct);
    void Focus();
    void Dispose();
}

public readonly record struct TerminalSize(int Cols, int Rows);
```

No code outside `Firepit.Web` may import `Microsoft.Web.WebView2.*` or know about xterm.js.

### 2.2 `IPtyChannel`

Abstracts ConPTY for testability. The real implementation lives in `Firepit.Process`; tests use a fake.

```csharp
public interface IPtyChannel : IAsyncDisposable
{
    int Pid { get; }
    Task<int> WaitForExitAsync(CancellationToken ct);   // returns exit code
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(CancellationToken ct);
    void Resize(int cols, int rows);
}
```

### 2.3 `IAgentAdapter`

Per-agent knowledge: how to launch, how to resume, what marks a folder as "this agent's project".

```csharp
public interface IAgentAdapter
{
    string Id { get; }                                // "claude-code", "aider", ...
    string DisplayName { get; }
    IReadOnlyList<string> ProjectMarkers { get; }     // e.g. ["CLAUDE.md", ".claude"]

    AgentLaunchSpec BuildLaunchSpec(ProjectContext ctx, AgentLaunchOptions opts);
}

public sealed record AgentLaunchSpec(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?> EnvironmentOverrides);

public sealed record AgentLaunchOptions(
    bool Resume,                                      // --continue
    string? SessionId);                               // --resume <id> (V2)
```

V1 ships exactly one: `ClaudeCodeAdapter` in `Firepit.Adapters`. Discovery markers (`CLAUDE.md`, `.claude/`) come from this adapter — the shell does not hardcode them.

### 2.4 `IActivityClock` and Activity States

```csharp
public interface IActivityClock
{
    DateTimeOffset UtcNow { get; }
}

public enum SessionState
{
    Cold,       // process never started
    Igniting,   // process spawned, no PTY output yet
    Burning,    // PTY output within burnWindowMs
    Embers,     // idle for >idleThresholdMs
    Dead        // process exited
}
```

Transition rules (see §6 for hysteresis detail):

- `Cold → Igniting`: user invokes summon
- `Igniting → Burning`: first PTY read
- `Burning ↔ Embers`: timestamp comparison with hysteresis
- `* → Dead`: process exit observed

---

## 3. WebView2 Hosting (`Firepit.Web`)

### 3.1 Loading Strategy

**Do not** load `terminal.html` from `file://`. Use virtual host mapping:

```csharp
webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
    hostName: "firepit.local",
    folderPath: <extracted-resources-path>,
    accessKind: CoreWebView2HostResourceAccessKind.Allow);
webView.Source = new Uri("https://firepit.local/terminal.html");
```

The xterm.js bundle, `terminal.html`, and any required fonts are embedded in `Firepit.Web` as `EmbeddedResource` and extracted to `%LOCALAPPDATA%\Firepit\WebAssets\<version>\` on first launch (idempotent — checked by version-stamped marker file).

### 3.2 Content Security Policy

The HTML ships with a strict CSP:

```
Content-Security-Policy:
  default-src 'self' https://firepit.local;
  script-src 'self' https://firepit.local;
  style-src 'self' https://firepit.local 'unsafe-inline';
  font-src 'self' https://firepit.local data:;
  img-src 'self' https://firepit.local data:;
  connect-src 'none';
```

`connect-src 'none'` is deliberate — xterm.js never needs network. If a future addon does, justify it explicitly.

### 3.3 Bridge Protocol

The bridge between WPF host and xterm.js carries two byte streams (PTY-out → render, key-in → PTY) plus control messages (resize, focus, ready).

**Default path: `WebMessage` strings, base64-encoded payloads.**

```jsonc
// host → web
{ "type": "data", "b64": "<base64>" }
{ "type": "resize", "cols": 120, "rows": 40 }
{ "type": "theme", "vars": { "--bg": "#1a1612", ... } }

// web → host
{ "type": "ready" }
{ "type": "input", "b64": "<base64>" }
{ "type": "resize", "cols": 120, "rows": 40 }   // user resized window
{ "type": "progress", "active": true }          // OSC 9;4 "thinking" signal (added V1.1.4)
```

**Throughput note.** `PostWebMessageAsString` round-trips through JSON serialization on the WebView2 side. For typical agent output (kilobytes per second) this is fine. If profiling shows the bridge as a bottleneck under heavy output (large diffs, file dumps), switch to `AddHostObjectToScript` exposing a host object whose method takes a byte-array transferable. Do not optimize prematurely; do measure once V1 dogfoods real workloads.

**Hard rule.** The bridge accepts exactly the message types above — extensions land here by amendment, not by ad-hoc additions. The web side is treated as a hostile renderer: no `eval`, no "execute command", no host-side property setters callable from JS.

### 3.4 Fonts

Bundle a single monospace font (Cascadia Code recommended — OFL) as a `data:` font in CSS or a `font-src 'self'`-served file. Do not depend on user-installed fonts; they vary per machine and cause layout drift.

---

## 4. ConPTY Layer (`Firepit.Process`)

### 4.1 Implementation Choice

Use the **Porta.Pty** NuGet package as the ConPTY wrapper for V1. Pulls in `Vanara.PInvoke.Kernel32` and a small native helper for the cross-platform paths we don't ship; the Windows path is pure managed Vanara P/Invoke into ConPTY's `CreatePseudoConsole` / `ResizePseudoConsole` / `ClosePseudoConsole`.

Why not direct hand-written `LibraryImport` P/Invoke: an early M1 attempt wired up the full ConPTY interop (pipes, attribute list, `STARTUPINFOEX`, `CreateProcessW`) with all calls reporting success and struct layout verified at runtime — but `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` did not actually attach the spawned child to the pseudo console. Spent enough time on it to confirm this isn't a five-minute fix; switched to the NuGet package and shipped. Direct interop is a V2 hardening candidate (see §16.5).

Caller surface (`IPtyChannel`) is unchanged either way — the choice is encapsulated in `ConPtyLauncher.SpawnAsync` and `ConPtyChannel`.

### 4.2 Process Lifecycle

```
[spawn]
  CreatePipe(in/out) → CreatePseudoConsole(size, in, out, 0, &hPC)
  STARTUPINFOEX with PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE
  CreateProcess(adapter.Executable, adapter.Arguments, env, cwd, &si, &pi)
  spawn read loop on out-pipe (fills IAsyncEnumerable<bytes>)
  spawn waiter on pi.hProcess (resolves WaitForExitAsync)

[resize]
  ResizePseudoConsole(hPC, {cols, rows})

[teardown]
  ClosePseudoConsole(hPC) → child sees EOF → exits
  Wait for exit, dispose pipes, dispose handles
```

### 4.3 Threading Model

- PTY read loop runs on a dedicated background task per session, writing into a `Channel<ReadOnlyMemory<byte>>`.
- The channel is consumed by the bridge writer, which marshals to the WebView2 thread via `Dispatcher.InvokeAsync`.
- Activity detector subscribes to read events on the same channel (or via a tee) — must not block the read loop.
- UI thread never reads from the PTY directly.

### 4.4 Errors

- Process fails to spawn (executable not found): surface `AgentLaunchException` with the executable name. Tab transitions `Igniting → Dead` immediately. User-facing status template: *"Cannot summon agent: `<command>` not found on PATH."* — `<command>` is interpolated from the failing adapter's launch spec, never hardcoded.
- Process exits with non-zero code: tab transitions to `Dead`; exit code stored on the session for the toolbar tooltip.
- PTY write fails (pipe closed): treat as process death; trigger waiter.
- Configured-but-unspawnable MCP server: see §9.7 — symmetric surface, non-fatal to the session.

---

## 5. Threading & Async Model

| Component | Thread |
|---|---|
| WPF UI / `Dispatcher` | UI thread |
| PTY read loop | dedicated `Task` per session |
| WebView2 callbacks | WebView2 message thread (marshal to UI for UI ops) |
| Activity ticker | `System.Threading.Timer` callback (200 ms) |
| `FileSystemWatcher` (project discovery) | watcher thread |

**Rules**:

- All `ITerminalView.WriteAsync` calls are made from a single producer per session — serialize via the channel.
- `INotifyPropertyChanged` events for tab state must fire on the UI thread.
- Cancellation tokens flow from the session to all its tasks; closing a tab cancels them all.

---

## 6. Activity Detection (Hysteresis)

The naive "compare last-read timestamp every 200 ms against a 500 ms threshold" flickers when an agent streams slowly (one token every ~400 ms during LLM output). Apply hysteresis:

```
config:
  burnWindowMs        = 500   // Burning if read within this window
  idleThresholdMs     = 1500  // Embers requires this much continuous silence
  ignitingTimeoutMs   = 10000 // If no first read by this, transition to Dead-ish "stuck"

state machine (per session):
  on read:
    lastReadAt = now
    if state == Igniting: state = Burning
    if state == Embers:   state = Burning
  on tick (every 200ms):
    silence = now - lastReadAt
    if state == Burning and silence > idleThresholdMs: state = Embers
    if state == Igniting and (now - igniteAt) > ignitingTimeoutMs: state = Embers
```

This makes Burning the "fresh activity" state and Embers the "definitely waiting for you" state. The two thresholds (500 and 1500) intentionally do not match — that gap *is* the hysteresis.

Defaults are configurable via `tabs.activityIdleThresholdMs` (the only knob exposed to users for V1).

---

## 7. Configuration

### 7.1 Files

- `%APPDATA%\Firepit\settings.json` — user settings (Roaming, follows the user)
- `%LOCALAPPDATA%\Firepit\logs\firepit-YYYY-MM-DD.log` — Serilog rotating files
- `%LOCALAPPDATA%\Firepit\WebAssets\<version>\` — extracted xterm.js bundle
- `%LOCALAPPDATA%\Firepit\state.json` — open-tab restoration data (Local, machine-specific)

`APPDATA` for preferences, `LOCALAPPDATA` for caches and machine-state. Standard Windows convention.

### 7.2 Schema

See `SPEC.md §Configuration` for the user-facing JSON. Internally model as immutable records loaded once at startup; reload triggered by Settings dialog save (V1: minimal — just edit the file and restart).

### 7.3 Defaults

Defaults are hardcoded in `Firepit.Core.Settings.Defaults`. Settings file is created on first user-initiated change, not on first launch.

---

## 8. Project Discovery

### 8.1 Markers

A folder under `projectsRoot` qualifies as a project if **any** registered `IAgentAdapter` claims it. For V1:

```csharp
ClaudeCodeAdapter.ProjectMarkers = ["CLAUDE.md", ".claude"];
```

The discovery service iterates `projectsRoot` one level deep and, for each child directory, asks each adapter "do you recognize this?". A directory matched by multiple adapters keeps a list; the user picks (V2).

### 8.2 Manual Entries

`settings.projects[]` lists explicit projects with paths outside `projectsRoot` and/or non-default adapter selection. Manual entries take precedence over auto-discovery (no duplicates).

### 8.3 Refresh

`FileSystemWatcher` on `projectsRoot` (non-recursive — we only care about top-level folders). Debounce events 500 ms before refreshing. New folders trigger marker re-check; deleted folders drop projects (closed tabs go cold first, not silently disappear).

---

## 9. MCP Server Registry

The registry is a global catalog of MCP servers; projects opt into a subset and may override per server. The registry itself is agent-agnostic — each `IAgentAdapter` is responsible for translating the *active set for a given session* into whatever format the agent expects.

### 9.1 Data Model

```csharp
public sealed record McpRegistryEntry(
    string Id,
    string DisplayName,
    string? Description,
    McpTransport Transport,
    string? Command,                                     // stdio
    IReadOnlyList<string> Args,                          // stdio
    IReadOnlyDictionary<string, string?> Environment,    // stdio
    string? Url,                                         // http | sse
    IReadOnlyDictionary<string, string?> Headers);       // http | sse

public enum McpTransport { Stdio, Http, Sse }

// Per-project activation lives in <project>/.firepit/config.json — one
// record per activated server, with optional inline overrides. The legacy
// settings.Projects[] shape (separate ActiveIds + Overrides dict) is still
// accepted as a fallback during migration but is deprecated.
public sealed record ProjectMcpActivation(
    string Id,
    IReadOnlyList<string>? ArgOverrides = null,          // null = inherit registry default
    IReadOnlyDictionary<string, string?>? EnvOverrides = null,
    IReadOnlyDictionary<string, string?>? HeaderOverrides = null);
```

Strings within `ArgOverrides`, `EnvOverrides`, and `HeaderOverrides` may contain reference tokens (`${env:NAME}`, `${cred:firepit/<key>}`) that are resolved at session start, not at config-load time. The same applies to the corresponding fields on the registry entry.

### 9.2 Service Surface

```csharp
public interface IMcpRegistry
{
    IReadOnlyList<McpRegistryEntry> All { get; }
    McpRegistryEntry? Find(string id);
    IReadOnlyList<ResolvedMcpServer> ResolveForProject(ProjectContext ctx, CancellationToken ct);
}

public sealed record ResolvedMcpServer(
    string Id,
    string DisplayName,
    McpTransport Transport,
    string? Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Environment,     // tokens already resolved
    string? Url,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<string> ResolutionWarnings);           // failed-to-resolve tokens are reported, not thrown
```

`ResolveForProject` merges registry defaults with per-project overrides, resolves secret tokens, and returns the effective configuration. Entries with unresolved required tokens are dropped from the result and a warning is appended; the session still starts, the missing server is just absent.

### 9.3 Adapter Contract

```csharp
public interface IAgentMcpProjector
{
    Task ApplyAsync(
        ProjectContext ctx,
        IReadOnlyList<ResolvedMcpServer> activeServers,
        CancellationToken ct);
}
```

Each `IAgentAdapter` provides an `IAgentMcpProjector`. For Claude Code in V1 the projector writes a session-local `.claude/mcp.json` (or runs `claude mcp add` invocations as a pre-launch step — implementation choice during M6). The shell never invokes Claude-specific knowledge directly.

### 9.4 Secret References

Two reference forms in V1:

| Form | Resolution |
|---|---|
| `${env:NAME}` | `Environment.GetEnvironmentVariable("NAME")` at resolve time |
| `${cred:firepit/<key>}` | Windows Credential Manager via `CredRead`, target `firepit/<key>`, `CRED_TYPE_GENERIC` |

Implementation lives in `Firepit.Process` (Credential Manager P/Invoke) but is exposed via `Firepit.Core.Secrets.ISecretResolver` so other layers can mock it. Tokens may appear inside any string value of an entry or override; the resolver scans for them with a deliberately narrow regex (`\$\{(env|cred):[^}]+\}`) and refuses any unrecognized scheme.

DPAPI-encrypted in-file secret storage is on the V1.1 candidate list. Plain-text secrets in `settings.json` are explicitly out of scope.

### 9.5 Lifecycle

The registry is loaded once at startup. Resolution happens **per session start** (not per launch of Firepit), so secret rotation in Credential Manager picks up on the next Rekindle without requiring a Firepit restart. The active set for a project is recomputed if the user edits the project's activation list during the session — the next Rekindle will use the new set.

### 9.6 What's Out of Scope for V1

- Discovery of MCP servers from a public marketplace
- Auto-update of MCP server binaries
- Schema validation of an MCP server's tool list before launch
- DPAPI-encrypted in-file secret storage (V1.1 candidate)
- A graphical registry editor — V1 ships the JSON file plus a minimal "open in editor" affordance

### 9.7 Lifecycle errors

Symmetric to §4.4 (agent spawn failure). For each resolved stdio server with a `Command`, a pre-flight PATH check runs at session start. Failures are non-fatal — the session still launches, the missing server is just absent — but they surface to the user via a non-modal banner pinned to the workspace tab. User-facing template: *"⚠ MCP server failed: `<id>` — `<command>` not found on PATH."* The check covers the "binary missing" failure mode that produced issue #4; runtime crashes inside an actually-launched server are out of scope for V1 (would require parsing Claude Code's `/mcp` output).

HTTP/SSE servers are not pre-flighted (no PATH dependency); their failure surface is left to the agent's own MCP output.

### 9.8 Git hygiene: versioned vs local

A project's Firepit/Claude footprint splits cleanly into two halves, and the
split is what Firepit's first-scaffold hardening (`ProjectScaffolding`) encodes:

| Versioned (commit it) | Local / ephemeral (ignore it) |
|---|---|
| `.firepit/config.json` | `.firepit/inbox/`, `.firepit/runs/` |
| `.claude/mcp.json`, `.claude/settings.json` | `.claude/settings.local.json` |
| `.claude/commands/`, `.claude/agents/` | `.claude/*.lock`, `.claude/agent-memory/` |

The versioned half is **declarative and shareable** and — by design — carries
**no plaintext secrets**: every credential is a `${cred:...}` reference resolved
through the Windows Credential Manager (§9.4), so the committed files are safe in
a shared repo. The local half is per-machine or personal (cross-project inbox
notes, scheduled-run outputs, machine-local overrides, lockfiles, agent memory).

The first time Firepit scaffolds a project's `config.json`, it idempotently
appends the granular ignore block to the root `.gitignore`, seeds the
"read `.firepit/inbox/` on session start" convention into `CLAUDE.md`, and — if
it finds a **blanket** `.firepit/` or `.claude/` ignore (which would swallow the
shared config) — warns and offers to migrate it to the granular form. The
hardening fires only on the initial scaffold, so existing repos are untouched.

---

## 10. Quick Links

A small feature with disproportionate value: per-project URL buttons in the toolbar that open in the system browser. Designed in V1 with the V2 sub-tab cockpit in mind so no schema change is needed when sub-tabs land.

### 10.1 Data Model

```csharp
public sealed record QuickLinkEntry(
    string Name,
    string UrlTemplate,
    QuickLinkTarget Target,                              // V1: only External is legal
    string? Icon,
    bool DisabledForProject);                            // per-project entries can disable a global default

public enum QuickLinkTarget
{
    External,                                            // V1: opens in system browser
    SubTab,                                              // V2+: hosts as a sub-tab
}
```

### 10.2 Templating

`QuickLinkEntry.UrlTemplate` may contain placeholders that are substituted at click time:

| Placeholder | Source |
|---|---|
| `{projectName}` | `ProjectContext.Name` |
| `{projectPath}` | `ProjectContext.Path` (URL-encoded if used in an `https://...` URL) |

Unknown placeholders cause the link to be rendered as disabled with a tooltip ("missing variable: …") rather than firing a half-substituted URL.

### 10.3 Resolution

```csharp
public interface IQuickLinkService
{
    IReadOnlyList<ResolvedQuickLink> ResolveForProject(ProjectContext ctx);
    void Open(ResolvedQuickLink link);  // V1: Process.Start with UseShellExecute=true
}
```

The same global-defaults-plus-per-project-overrides resolution shape as the MCP registry. Yes, the pattern repeats — it's deliberately not extracted to a shared abstraction in V1; if a third user emerges, refactor then. Two consumers don't justify an `IRegistry<T>` yet.

### 10.4 V2 Forward Compatibility

When V2 introduces sub-tabs, the only change required is the `Target` enum gains real meaning for `SubTab`, the data model is unchanged. See §17.1.

---

## 11. Single-Instance Behavior

**Mechanism: named pipe**, not a mutex. Mutex is simpler but cannot pass arguments; named pipe lets `firepit.exe summon --project lighthouse` reach a running instance.

```
On startup:
  try Open named pipe "firepit-singleton"
    success → send args, wait for ack, exit
    failure (no server) → become server: create pipe, listen, run UI

On running instance:
  pipe server accepts → reads args → marshals to UI ("focus tab X" or "open project Y") → ack
```

Pipe payload is a small JSON envelope: `{ "command": "focus" | "summon", "project": "<name>" }`.

---

## 12. Logging

Serilog, rotating file sink. Levels:

- `Information` for lifecycle (start/stop, project loaded, session started/exited)
- `Debug` for PTY write/read sizes (not contents — PII)
- `Warning` for recoverable issues (adapter not found, settings parse fallback)
- `Error` for unhandled exceptions before they bubble to UI

Retention: 14 days, max 10 MB per file. Format: compact JSON for grep-ability.

**Never log PTY contents.** Agent conversations may include user code, secrets, prompts. Log byte counts and timestamps, never bytes.

---

## 13. Distribution

### 13.1 Publishing

```
dotnet publish src/Firepit -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `Firepit.exe` plus a small handful of native deps (WebView2 SDK natives extracted on first run). The `firepit-mcp.exe` stdio MCP bridge is published as a sibling single-file binary (`src/Firepit.Mcp/`) and shipped alongside `Firepit.exe`. The Inno Setup installer (V1.12) places both in `%LOCALAPPDATA%\Programs\Firepit\` and adds that directory to the user's PATH so a workspace's `.claude/settings.json` can reference `firepit-mcp` by bare name — see `docs/V1.12-INSTALLER.md` for the resolution decision.

### 13.2 WebView2 Runtime Prerequisite

Modern Win10 (≥ 21H2) and all Win11 ship the Evergreen WebView2 Runtime. Older systems need the bootstrapper. V1: detect on startup, show a dialog with a download link if missing. **Do not** bundle the bootstrapper in V1 — adds ~150 MB and almost no users will need it.

```csharp
var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
// throws if missing — catch, show dialog, exit
```

### 13.3 Embedded Resources

`xterm.bundle.js` (≈400 KB minified), `terminal.html`, font file, CSS — all embedded in `Firepit.Web` as `EmbeddedResource` items. Build step: an MSBuild `Target BeforeBuild` runs `npm install && npm run bundle` in `src/Firepit.Web/Resources/` to produce the bundle. Lock file is committed.

### 13.4 Releases

GitHub Releases, manual. Tag `vX.Y.Z`, attach the published archive plus the Inno Setup installer (`FirepitSetup-<version>-win-x64.exe`), write release notes. No auto-update in V1.

---

## 14. Testing Strategy

### 14.1 Layers

| Layer | Test type | Approach |
|---|---|---|
| `Firepit.Core` | Unit | Pure-C# tests, no PTY, no UI. State machines, settings parsing, project discovery (with mocked filesystem). |
| `Firepit.Process` | Unit + light integration | `IPtyChannel` against a fake; integration test launches `cmd /c echo hi` under real ConPTY (Windows-only, CI-tagged). |
| `Firepit.Adapters` | Unit | Adapter `BuildLaunchSpec` against golden expected outputs. |
| `Firepit.Web` | Integration | WebView2 round-trip ("write hello, expect hello rendered, type 'q', expect bytes received"). CI-skippable on non-Windows. |
| `Firepit` (WPF) | Manual / smoke | No automated UI tests in V1. Manual checklist in ROADMAP per milestone. |

### 14.2 Seams

- `IPtyChannel` makes the process host testable without a real child.
- `IActivityClock` makes time-dependent state machine deterministic — drive `UtcNow` from the test.
- `IAgentAdapter` enables a `FakeAdapter` that runs `pwsh.exe -NoLogo -Command "while ($true) { Write-Host hi; Start-Sleep 1 }"` for integration tests.

### 14.3 What Not to Mock

- Don't mock `CoreWebView2`. Either use it for real or don't test that code.
- Don't mock the filesystem in adapter tests (golden files are simple I/O).
- Don't mock `Process` directly; wrap it in `IPtyChannel`.

---

## 15. Security Boundary Summary

- WebView2 → Host: only the four message types in §3.3. Anything else is dropped and logged at `Warning`.
- Host → WebView2: only `data`, `resize`, `theme`. No code injection paths.
- Local network: `connect-src 'none'` in the embedded HTML.
- File system: WebView2 has no file access; the host owns all I/O.
- Logging: never log PTY contents.

---

## 16. Open Architectural Questions

These are decisions that don't block starting V1 but need answers before relevant milestones land. Tracked here so they're not forgotten.

1. **Bridge optimization**: stay on string/JSON for V1; profile under real load before switching to host-object. Owner: M3 dogfooding.
2. **Tab persistence depth**: V1 restores tab list and re-runs `--continue`; scrollback restore via `xterm-addon-serialize` is a V2 candidate.
3. **Theme tokens**: minimum viable set is background, foreground, cursor, selection, ANSI 16. Defer full theming to V2.
4. **Resize policy on focus change**: cheaper to resize PTY only when a tab is visible? Acceptable lag? Decide in M3.
5. **ConPTY interop strategy**: M1 dogfooding showed direct `LibraryImport`-based P/Invoke wired up correctly (every Win32 call returns success, struct layout verified) but `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` did not actually attach the spawned child to the pseudo console — child output bypassed the console pipe. After several debugging passes it was deemed cheaper to consume a NuGet-packaged ConPTY wrapper (e.g. `Porta.Pty`) for V1 than to keep grinding on the interop. The `IPtyChannel` abstraction makes this a swap, not a refactor; revisiting direct P/Invoke is a V2 hardening candidate.

---

## 17. Forward-Compatibility Notes

V2 will land features (sub-tab cockpit, file/markdown/image viewers, embedded shell, session history) that are out of V1 scope but whose presence in the design space affects V1 abstractions. This section is the explicit list of "V1 made this decision *aware of* what V2 needs" — it costs nothing in V1 and prevents refactor surprises later.

### 17.1 `ITerminalView` Generalizes to `IProjectPaneView`

In V2, a project tab hosts multiple panes (terminal, web view, file browser, markdown viewer, image viewer). The V1 `ITerminalView` contract — bytes in, bytes out, `InitializeAsync`, `Focus`, `Resized` — is a strict subset of what an `IProjectPaneView` will need (URL navigation, file path, save/dirty state, etc.). V2 generalizes the interface; V1 ships the terminal-only form unchanged. Don't pre-bake V2 needs into V1.

### 17.2 Quick-Link `Target` Field

V1 ships `QuickLinkEntry.Target = External` as the only legal value. The enum exists in V1 — see §10.1 — solely so V2 can add `SubTab` without a schema change. V1 must not branch on `Target` for any logic beyond the trivial `External`-opens-browser path.

### 17.3 Tab State Pluralizes to Pane State

Tab restoration (§11/M7) persists a list of `(projectName, lastSessionResumable)`. In V2, with sub-tabs, persisted state needs to extend to `(projectName, panes[])`. Schema migration is acceptable — `state.json` is local-only — but the V1 code that reads/writes `state.json` should treat the file as a versioned schema (`{ "version": 1, ... }`) so V2 can detect the upgrade rather than guess.

### 17.4 Registry Pattern Repeats

Both `IMcpRegistry` (§9) and `IQuickLinkService` (§10) implement the same shape: globally-defined entries activated and optionally overridden per project. A future addition (themes, env-bundles, agent presets) might also fit. The pattern is **not** extracted to a shared `IRegistry<T>` abstraction in V1 — two implementations doesn't yet justify the indirection. If a third consumer emerges, extract then; until then, the duplicated resolution logic is acceptable cost.

---

*Document version: 0.2 — adds MCP registry, quick links, forward-compat notes*
