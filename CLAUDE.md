# traymirror: agent guidance

Repository: `latticelabs-au/traymirror` (public, MIT, Copyright (c) 2026 Lattice Labs)

traymirror is a Windows 11 utility that mirrors the primary system tray onto secondary monitors.
Windows 11's "show taskbar on all displays" draws Start, task buttons, the clock and the
notification bell on every monitor, but it omits the notification area. This repo fills exactly
that gap.

The mechanism: `DwmRegisterThumbnail` plus `DwmUpdateThumbnailProperties` render a live,
GPU-composited, source-cropped copy of the primary taskbar's tray strip into a borderless topmost
window on each secondary monitor, right aligned to that monitor's clock. UI Automation is used only
to locate the two crop boundaries. DWM thumbnails are visual only and do not hit test, so input is
handled separately: a click on a mirror is translated back to primary-screen coordinates by plain
arithmetic and synthesised there. Validated on Windows 11 build 26100 (24H2).

---

## Current repository state, read this first

**The three projects exist and build.** `traymirror.sln`, `src/TrayMirror/`, `src/TrayMirror.Core/`
and `tests/TrayMirror.Core.Tests/` are on disk. `dotnet build -c Release` is clean with zero
warnings, `dotnet format --verify-no-changes --severity error` exits 0, and `dotnet test` runs 86
tests against Core.

Phase 1 (the mirror core) and phase 2 (input routing and menu relocation) of the design document
are implemented. Phase 4 (flyout parity) is partly done: the quick-settings panel relocates onto
the mirror, which the design spec had left as an open question. Phase 3 (the overflow flyout) is
not done.

Because the projects are present, the `hashFiles('traymirror.sln')` guards in `ci.yml` are now
satisfied and the `build` and `format` jobs do real work. Nothing in the workflow needed editing
when the projects landed, which was the point of writing the guards that way.

Verified by hand on Windows 11 build 26200 (24H2), on a 1920x1080 primary at (0,0) with a
1920x1080 secondary at (-1920,1), both at 100%: the tray strip resolves to 228x48 between the
overflow chevron at x=1602 and the clock at x=1830, the mirror lands at (-318,1033) flush against
the secondary clock, a routed left-click on the mirrored volume icon opens the quick-settings
panel, and the OneDrive panel and its Win32 submenu relocate onto the secondary monitor.

The design of record is `docs/specs/2026-08-10-traymirror-design.md`. Where that spec and this file
disagree, the spec wins on behaviour and this file wins on process.

---

## Architecture map

```
traymirror.sln
src/TrayMirror/               WPF shell (WinExe)
src/TrayMirror.Core/          Interop logic, no WPF
tests/TrayMirror.Core.Tests/  xUnit tests for Core only
```

### TrayMirror (WPF shell)

Owns everything that requires a window handle or the WPF runtime:

- `Program.cs`, the entry point and the object graph. There is no `App.xaml`: every window is
  borderless with no WPF content, so a XAML application definition would add a build step and a
  generated entry point for nothing.
- Win32 and DWM P/Invoke declarations (`NativeMethods.cs`, `DwmApi.cs`).
- `MirrorWindow`: the borderless topmost window rendered per secondary monitor.
- UI Automation client calls to locate the two tray strip boundaries (these run on a background
  thread, see the thread rules below).
- Message pump integration for the registered `TaskbarCreated` message, `WM_DISPLAYCHANGE` and
  `WM_DPICHANGED`.

### TrayMirror.Core (no WPF, headlessly testable)

**Hard rule: TrayMirror.Core must never reference WPF, System.Windows, PresentationCore,
PresentationFramework, or WindowsBase. Not even for structs.** If a type would require those
assemblies it belongs in the WPF shell, or you define a plain C# equivalent in Core and convert
at the boundary.

Why: the test suite runs headlessly without a WPF dispatcher. Any WPF reference in Core breaks
that invariant, forces mock dispatchers, and couples pure logic to a UI framework that may change.

Core owns:

- Monitor topology (connected monitors, primary vs secondary, working area geometry).
- DPI maths (logical-to-physical conversion, source-rect computation from raw geometry).
- Tray strip placement and anchoring logic (where the mirror window sits on each monitor).
- Config parsing and validation (`%APPDATA%\TrayMirror\traymirror.jsonc`).
- Hit-test translation. A click at mirror-local offset `dx` maps to primary screen x of
  `stripLeft + dx`. This is coordinate arithmetic, not a UIA query and not a DWM query.
- Any pure algorithm: rectangle arithmetic, coordinate transforms, debounce logic.

### TrayMirror.Core.Tests

Tests Core only. It inherits `net10.0-windows10.0.22621.0` from `Directory.Build.props` because the
monitor and DPI helpers use Windows APIs, but nothing in the test project requires a running WPF
application or a real display.

---

## What UI Automation is and is not used for

UIA does two jobs, boundaries and activation.

**Boundaries.** It queries the primary taskbar for exactly two things: the left edge of the first
notification-area element, and the left edge of the clock. The difference between them is the strip
to mirror.

**Activation.** A left-click on a mirror is delivered by invoking the real tray icon's
`InvokePattern`, not by synthesising a mouse click on it. Measured on build 26200: all ten tray
elements expose `InvokePattern`, invoking one opens its flyout without moving the cursor and
without taking the foreground, and that is what makes a flyout's open-and-close behaviour survive
being driven from a mirror. `SendInput` remains the fallback, and remains the only route for right
clicks, middle clicks and the wheel, because `Invoke` is the element's default action and has no
equivalent for those.

Two measured facts that the implementation depends on, both worth keeping written down:

- `AutomationElement.FromPoint` over a tray icon returns only the `Shell_TrayWnd` pane and its
  desktop parent, neither of which supports any pattern. The XAML tray provider does not implement
  hit testing from a point, so the element is found by walking the tree and comparing rectangles.
- Each tray button contains a 16x16 `Image` child that encloses the same point and supports no
  pattern. Selecting the smallest enclosing element lands on that image, so the search must select
  the smallest enclosing element **that supports `InvokePattern`**.

UIA is still **not** used for hit-testing and **not** used for rendering:

- Rendering is DWM, which composites the real pixels for us.
- Hit-testing is the arithmetic translation in Core described above. Deciding *which* icon was
  clicked is coordinate arithmetic; UIA is only handed the resulting point.

Do not write a sentence that gives UIA either of those two jobs.

### What may go on MenuRelocator's class allow list

Only a window that carries the pixels the user is meant to see. Not the window that hosts it.

Measured on build 26200, the language switcher makes the distinction concrete. Clicking the input
indicator raises three windows: a `Shell_InputSwitchDismissOverlay` spanning the whole virtual
desktop, a `Shell_InputSwitchTopLevelWindow` the size of the primary monitor, and then the list
itself, a 328x105 `Xaml_WindowedPopupClass` resting just above the taskbar. Only the last one is
worth moving, and the general shape rule already accepts it.

Putting the host on the allow list actively breaks the feature, in a way that looks like success:
the host is raised first, so it is accepted and relocated, that consumes the arming, and the real
popup is then never examined. Moving the host also moves nothing visible, because the content is
composed separately, and it leaves an invisible monitor-sized window over the target monitor that
swallows clicks until it closes.

`ControlCenterWindow` is on the list because it is genuinely the window carrying the pixels, even
though it is created at the full height of the screen and animates its content into the corner.

### MenuRelocator hooks

`MenuRelocator` registers **two** out-of-process `SetWinEventHook` hooks, and every description of
it must name both:

- `EVENT_SYSTEM_MENUPOPUPSTART` catches classic Win32 `#32768` tray menus.
- `EVENT_OBJECT_SHOW` catches XAML island flyouts, such as the quick-settings panels, which never
  raise the menu event.

Neither hook requires injection, because both are registered out of process. Naming only one hook,
or implying a DLL is loaded into Explorer, is wrong.

---

## DWM interop invariants

These rules apply to every `DwmRegisterThumbnail` handle the code creates. Violating any of them
leaks DWM composition resources, which are session-global and do not recover until logoff.

### Handle lifetime

Every thumbnail handle obtained from `DwmRegisterThumbnail` must be released with
`DwmUnregisterThumbnail` in each of the following conditions:

- **Monitor removal.** When a secondary monitor disconnects, unregister all handles targeting
  windows on that monitor before discarding the `MirrorWindow`.
- **Taskbar recreation.** When Explorer restarts it destroys `Shell_TrayWnd` and all thumbnails
  that referenced it. On the `TaskbarCreated` message you must unregister all existing handles
  before re-registering. Do not assume old handles are still valid after Explorer comes back.
- **App shutdown.** Unregister every handle in the `Closing` handler and in a `WM_DESTROY` path.
  Do not rely on GC finalisation; the DWM session outlives the process.
- **DPI or display-change rebuild.** If you tear down and recreate mirror windows on a DPI change,
  follow the monitor-removal path first.

Wrap each handle in a `SafeHandle` subclass (name it `SafeDwmThumbnailHandle`) that calls
`DwmUnregisterThumbnail` in `ReleaseHandle`. This ensures the handle is released even if an
exception fires between register and the explicit unregister call.

### Source-rect coordinates

`DWM_THUMBNAIL_PROPERTIES.rcSource` is in **source window coordinates**, not screen coordinates.

The source window is `Shell_TrayWnd`. Its client origin is not necessarily at (0, 0) in screen
space. When you compute the crop rectangle for the tray strip:

1. Call `GetWindowRect` on `Shell_TrayWnd` to get its screen-space origin.
2. Subtract that origin from your screen-space tray-strip bounds before writing `rcSource`.

Forgetting step 2 is the single most common mistake. The crop will appear correct on a single
primary monitor at 100% DPI (because the taskbar's screen origin is often close to 0, 0), and
will silently break on scaled or non-zero-origin primary taskbars.

### HRESULT checking

Every `dwmapi.dll` call returns `HRESULT`. None of them are fire-and-forget.

- Declare all DWM P/Invoke methods with return type `int` (the raw HRESULT).
- Call `Marshal.ThrowExceptionForHR(hr)` after each call, or use a helper that does the same.
- Do not declare a DWM P/Invoke with a `void` return type. That discards the HRESULT at the
  declaration site, where no reviewer and no later caller can recover it.

Nothing checks this for you automatically. Write the check by hand at every call site.

---

## UI thread rules

These rules exist because DWM and most Win32 calls are apartment-threaded, while UIA calls block
on cross-process round-trips.

### What must run on the UI thread

- All `DwmRegisterThumbnail`, `DwmUnregisterThumbnail`, and `DwmUpdateThumbnailProperties` calls.
- All Win32 window operations: `CreateWindowEx`, `SetWindowPos`, `GetWindowRect`, `SendMessage`,
  and related calls on any window owned by this process.
- `WndProc` and message-loop callbacks.
- Any access to WPF `Dispatcher` or `DispatcherObject`-derived types.

Use `Application.Current.Dispatcher.InvokeAsync` or `Dispatcher.BeginInvoke` to marshal work back
from a background thread. Never call DWM APIs from a `Task.Run` body or a `ThreadPool` callback
without first returning to the UI thread.

### What must NOT run on the UI thread

**UI Automation queries must not run on the UI thread.**

UIA client calls (`FindAll`, `GetCurrentPropertyValue`, and similar) make cross-process COM calls
into Explorer's UIA provider. These can block for tens to hundreds of milliseconds. Running them on
the UI thread stalls the message pump, which delays `WM_PAINT`, input processing, and DWM frame
callbacks.

Pattern: start UIA queries on a background thread via `Task.Run`. When results arrive, marshal
the computed geometry back to the UI thread with `Dispatcher.InvokeAsync` before touching any
window or DWM state.

---

## Resilience invariants

Windows can reconfigure the display stack at any time. The code must handle each of the following
without crashing or leaking.

### Explorer restart (TaskbarCreated)

Explorer can exit and restart independently of the session. When it does:

- `Shell_TrayWnd` is destroyed and all thumbnails that targeted it become invalid.
- A new `Shell_TrayWnd` is created and a registered window message named `"TaskbarCreated"` is
  broadcast to all top-level windows.

You must register this message with `RegisterWindowMessage("TaskbarCreated")` at startup, handle
it in the `WndProc`, and trigger a full rebuild: unregister all existing thumbnail handles,
re-query UIA for the new tray strip boundaries, and re-register thumbnails.

Do not skip the unregister step even though the old handles are already dead. DWM may reuse handle
values; calling `DwmUnregisterThumbnail` on a stale handle is safe (it returns a non-fatal
HRESULT), but omitting it leaks your bookkeeping.

### Monitor hotplug

Listen for `WM_DISPLAYCHANGE`. On receipt:

1. Enumerate the current monitor set with `EnumDisplayMonitors`.
2. Release and unregister thumbnails for monitors that are no longer present.
3. Create mirror windows and register thumbnails for newly connected monitors.
4. Update geometry on monitors that remain but have changed resolution or position.

Do not assume the monitor set is stable between `WM_DISPLAYCHANGE` messages. Rapid plug-unplug
sequences arrive as multiple messages; rebuild idempotently.

### Full-screen applications

**The shell does not hide the taskbar when an application goes full screen.** Measured on build
26200 with a real full-screen window on the primary: `Shell_TrayWnd` keeps `IsWindowVisible` true
and keeps exactly the same rectangle. It is simply covered, by z-order. Nothing is reported: no
`WM_DISPLAYCHANGE`, because the resolution did not change; no working-area `WM_SETTINGCHANGE`,
because a covering window displaces nothing; and no visibility event, because nothing was hidden.

Three shell mechanisms were checked and all three are dead ends. `ABN_FULLSCREENAPP` is documented
but carries one boolean with no indication of which monitor. `SHQueryUserNotificationState` is a
getter with no notification and is global rather than per monitor. `IAppVisibility` reports whether
a monitor shows immersive Windows 8 shell surfaces, which on Windows 11 is essentially never.

So `FullScreenWatcher` looks at the windows, which is what the shell itself does. Two out-of-process
hooks: `EVENT_SYSTEM_FOREGROUND` for an application brought up full screen, and
`EVENT_OBJECT_LOCATIONCHANGE` re-scoped to the foreground window's own thread, for a window already
in front that then resizes itself (a video going full screen in a browser raises no foreground
event). Each event triggers a sweep of the top-level windows, which is a response to an event and
never a timer.

Rules the sweep depends on, each of them load-bearing:

- Compare against the monitor's **full bounds**, never its working area. That single choice is what
  separates full screen from merely maximised, because a maximised window stops at the working area
  and so leaves the taskbar's height uncovered.
- Ignore `Progman` and `WorkerW`. The desktop is exactly monitor sized, so without this every
  monitor reports a full-screen application permanently.
- Ignore cloaked windows (`DWMWA_CLOAKED`). A suspended packaged application or a window on another
  virtual desktop has a perfectly valid full-screen rectangle and is not on screen at all.
- Ignore this process's own windows. Mirrors are topmost and sit on the monitors being judged.
- Sweep all top-level windows rather than only the foreground one. A video playing full screen on
  the secondary monitor stays full screen when focus moves to the primary, and judging by the
  foreground alone would un-hide that mirror the moment the user clicked away.

A mirror is hidden only when a full-screen application covers **the monitor it sits on**. One on
the primary deliberately does not hide it: watching something full screen on one screen is exactly
when the tray still being readable on the other is worth having. The strip does become a view of a
tray that cannot be clicked through to until the user leaves full screen, and that is the accepted
trade.

Hiding and showing are applied the moment the event arrives, not deferred to a rebuild. A rebuild
waits for the coalescer's quiet window and then re-reads the tray over UI Automation, which would
put roughly half a second between an application going full screen and the strip getting out of the
way. The windows already exist and already know where they go, so visibility costs one
`SetWindowPos`. Measured on build 26200: 115ms to hide, 91ms to come back, both including the test
harness's own 50ms polling granularity.

### Taskbar visibility

Separately from the above, a taskbar can genuinely be hidden, by auto-hide or by the shell. That
does raise visibility events, and `ShellWatcher` registers a third out-of-process
`SetWinEventHook`, for `EVENT_OBJECT_SHOW` through `EVENT_OBJECT_HIDE`, scoped to the shell's
process and filtered to the two taskbar classes. This is separate from `MenuRelocator`'s two hooks
and does not change that component's contract. Like those, it is out of process and injects nothing.

A mirror must follow the visibility of the taskbar it is anchored to:

- **Primary taskbar hidden.** There is nothing to mirror while the source is off screen. Hide every
  mirror, and keep the windows and their thumbnail registrations so that coming back is a
  placement rather than a full rebuild.
- **A secondary taskbar hidden.** Hide that monitor's mirror only.
- **A monitor with no secondary taskbar at all.** This is not the same case. That user has "show my
  taskbar on all displays" turned off, and the mirror is anchored to the monitor's bottom right
  corner instead. Treating hidden and absent alike is what leaves a strip floating over a
  full-screen application.

Never destroy mirrors on a transient failure. A boundary read can fail while the shell is settling,
and a torn-down mirror stays gone until the next shell event, which may be a long way off. Hide
instead, and retry the read: hiding alone still leaves recovery to an event that may never arrive,
which was observed on build 26200 as mirrors staying hidden indefinitely on an otherwise idle
desktop. The retry is bounded, five attempts one second apart, and stops as soon as a read
succeeds. A bounded retry after a failure is error recovery, not the polling the rule above
forbids.

### DPI change

Listen for `WM_DPICHANGED` per monitor. On receipt for a mirror window:

1. Update the window's size and position using the `RECT` passed in `lParam`.
2. Recompute the source crop for the tray strip using the new DPI scale.
3. Call `DwmUpdateThumbnailProperties` with the corrected `rcSource` and `rcDestination`.

If the primary monitor's DPI changes, re-query the tray strip boundaries from UIA (they are in
physical pixels and change with the primary DPI scale) before recomputing any crop rectangles.

---

## Build and test commands

```powershell
# Restore dependencies
dotnet restore

# Build
dotnet build -c Release

# Run all tests
dotnet test -c Release

# Check formatting without modifying files
dotnet format --verify-no-changes --severity error

# Apply formatting fixes locally
dotnet format
```

Warnings are errors, but not because of a command-line flag. `TreatWarningsAsErrors`,
`CodeAnalysisTreatWarningsAsErrors` and `AnalysisMode=All` live in `Directory.Build.props`, which
is where that policy belongs. Never write `dotnet build -warnaserror`: setting it a second time on
the command line hides where the policy actually lives, and the two can then drift apart.

There is no code coverage gate. Do not document a coverage threshold, do not add a
coverage-collecting variant of the test command, and do not add coverage to CI.

The SDK version is pinned in `global.json` (10.0.302, rollForward latestFeature). If `dotnet`
resolves a different SDK version than expected, check that `global.json` is present and that
`dotnet --version` agrees.

### What CI actually runs

`.github/workflows/ci.yml` has exactly three jobs: `build`, `format` and `prose`. There is no job
called `em-dash-gate`. If you need to name the em dash gate, its name is `prose`.

All three jobs run on `windows-latest`. None of them runs on `ubuntu-latest`. WPF and the DWM
interop layer cannot be built or tested on Linux at all, and the prose gate is pure text that could
run cheaper on Linux but stays on the same runner deliberately, so contributors debug one
environment instead of two.

| Job | What it runs |
|---|---|
| `build` | `dotnet restore`, then `dotnet build -c Release --no-restore`, then `dotnet test -c Release --no-build` |
| `format` | `dotnet format --verify-no-changes --severity error` |
| `prose` | `git grep` for U+2014 across `'*.md'` and `'docs/**'` |

---

## House style

### Spelling and prose

Australian spelling in all prose: behaviour, colour, initialise, centre, licence (noun), license
(verb). Code identifiers, .NET API names, file paths, and XML elements stay US-spelled (`Color`,
`Initialize`, `SystemColors`). Never rename a framework symbol for spelling.

Sentence case headings. Second person, present tense. Technical and direct, no marketing
superlatives ("blazing", "seamless", "powerful"). Explain the why next to the what. Be frank
about failure modes rather than papering over them.

### Em dash policy, and what CI actually enforces

These are two different things. State them separately and never let one imply the other.

**Policy.** The character U+2014 (em dash) is banned in every tracked file as a house rule:
Markdown, XML doc comments, code comments, UI strings, resource files, commit messages and release
notes. If a sentence seems to need an em dash, the sentence is structurally wrong. Use a colon, a
comma pair, parentheses, or two sentences.

**Enforcement.** Narrower than the policy:

- The CI `prose` job greps for U+2014 in `'*.md'` and `'docs/**'` only.
- Source code, resource files and commit messages are reviewer enforced, not CI enforced.
- The en dash (U+2013) is not checked by CI at all. Avoid it in prose regardless. It is tolerable
  only inside numeric ranges in tables, for example `10-20`, and `to` is better even there.

Never imply CI catches more than that.

There is no `.githooks` directory and no `commit-msg` hook in this repo. Do not add a reference to
one, and in particular never tell anyone to run `git config core.hooksPath .githooks`: that path
does not exist, and setting it silently disables every hook the developer currently has.

### Conventional Commits

```
type(scope): lowercase imperative summary, max 72 chars, no trailing period
```

Types: `feat`, `fix`, `docs`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `security`.

Scopes (optional, use sparingly): `dwm`, `tray`, `monitors`, `dpi`, `config`, `ui`, `interop`,
`ci`, `release`.

Body: blank line after the subject, wrapped at 72 columns. State what was tested, what was found
empirically, and why the decision was made. Close with a `Verified:` line listing concrete
evidence:

```
Verified: dotnet build -c Release clean, dotnet format clean, manual two-monitor
check at 100% and 150% on build 26100
```

No AI attribution of any kind in commits. No `Co-Authored-By: Claude`, no generated-with trailer.

---

## Claims that must never appear in this repo's docs

Each of these has been checked against the repository. None of them exists, so writing any of them
would be a fabrication.

- **No code signing.** No cosign, no sigstore, no SLSA provenance, no `.pem` or `.sig` release
  assets, no `gh attestation verify`. The only verification that exists is: download
  `SHA256SUMS.txt` from the release and compare with PowerShell `Get-FileHash`. The binaries are
  unsigned, so users see a SmartScreen warning on first run. Say that plainly rather than implying
  the download carries a signature.
- **No `win-arm64` build.** `release.yml` packages `win-x64` only, in two forms: a self-contained
  single-file `.exe`, and a framework-dependent `.zip` that requires the .NET 10 Desktop Runtime.
- **No `assets/` directory and no logo image.** Do not reference one from any Markdown file.
- **No settings window and no "enable diagnostics" menu item.** The tray context menu is exactly,
  in this order: Reload config, Start with Windows (a checkable toggle), Open config file, Open
  diagnostics log, Exit. Diagnostics logging is turned on by setting the `diagnosticsLog` key in
  the config file, so bug-report instructions must tell the user to edit that key.
- **No unrelated cross-references in `Directory.Build.props`.** Its comments describe this project
  only, with no other project, person, or language named.

### Reference machine geometry, so examples stay possible

On the development machine used for the measured baseline in the design spec, `\\.\DISPLAY2` is the
**primary** (3440x1440 at origin 0,0) and `\\.\DISPLAY1` is the **secondary** (2560x1440 at origin
3440,0). traymirror mirrors onto secondary monitors, so config examples name `\\.\DISPLAY1`. An
example that mirrors onto `\\.\DISPLAY2` describes an impossible configuration, because that is the
monitor being mirrored from.

---

## Do not do this

**Do not add a bitmap capture loop.** The mechanism is DWM thumbnails: live, GPU-composited,
zero CPU overhead. A bitmap capture loop (GDI `BitBlt`, D3D readback, or any polling screenshot)
defeats the entire architecture, introduces tearing, and burns CPU.

**Do not poll for tray changes.** Use the registered `TaskbarCreated` message, `WM_DISPLAYCHANGE`,
`WM_DPICHANGED`, the working-area `WM_SETTINGCHANGE`, `MenuRelocator`'s two out-of-process WinEvent
hooks, `ShellWatcher`'s out-of-process taskbar visibility hook, `FullScreenWatcher`'s two
out-of-process hooks, and the UI Automation structure-changed subscription on the tray. Every one of those is a subscription the shell raises.
Polling is brittle, power-expensive, and races with the very restarts it tries to detect.

**Do not inject into explorer.exe.** traymirror is designed to be safe on locked-down corporate
machines. In-process injection (DLL injection, `SetWindowsHookEx` with a DLL, `WriteProcessMemory`)
would trigger antivirus and endpoint detection tools, require elevated privileges, and break the
safety guarantee. This constraint is deliberate and permanent. Out-of-process `SetWinEventHook`
registrations, which is what `MenuRelocator` uses, are not injection and are allowed.

**Do not read explorer.exe memory for undocumented structures.** Tray icon positions, counts, and
order must be obtained from UIA or the documented `Shell_TrayWnd` child window hierarchy, not
from `ReadProcessMemory` over explorer's private heaps. Undocumented offsets change without notice
across cumulative updates.
