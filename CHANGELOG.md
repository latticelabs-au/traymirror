# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial repository scaffold: documentation, repository configuration (`global.json` SDK pin
  10.0.302, `Directory.Build.props` with strict Roslyn analyser settings, `.editorconfig`),
  CI and release workflows (`ci.yml`, `release.yml`), community health files, and MIT licence.
- `traymirror.sln` and the three projects: `TrayMirror` (WPF shell), `TrayMirror.Core` (WPF-free
  logic) and `TrayMirror.Core.Tests` (86 xUnit tests). This activates the `hashFiles` guards in
  `ci.yml`, so the `build` and `format` jobs now do real work.
- Phase 1, the mirror core. Tray boundaries are resolved from UI Automation, cropped relative to
  the `Shell_TrayWnd` origin, and composited onto each secondary monitor with
  `DwmRegisterThumbnail`, right aligned to that monitor's clock. Rebuilds are driven by the
  registered `TaskbarCreated` message, `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, a working-area change,
  and a UI Automation structure-changed subscription on the tray. Nothing polls.
- Phase 2, input routing. A click on a mirror is translated to the matching primary-screen point by
  arithmetic in `TrayMirror.Core` and synthesised there with `SendInput`, and the flyout it opens is
  moved onto the mirror by `MenuRelocator` through two out-of-process `SetWinEventHook`
  registrations.
- `traymirror --probe`, which prints the monitor topology, both taskbar windows, the resolved
  boundaries and the full UI Automation element list of every taskbar. Boundary detection has to
  cope with a XAML tray that changes between Windows updates and differs by display language, so a
  bug report needs to say what the tray on that machine actually looks like.
- Configuration at `%APPDATA%\TrayMirror\traymirror.jsonc`, reloaded when the file changes, with
  per-monitor overrides, and a tray menu of exactly: Reload config, Start with Windows, Open config
  file, Open diagnostics log, Exit.

### Fixed

- A second click on a mirrored icon whose flyout was already open bounced the flyout to the primary
  monitor and back instead of closing it. The routed click reached the tray icon after the
  application had hidden the flyout on losing activation, so the icon handler read that as "not
  showing" and showed it again. traymirror now treats that click as a dismissal and hands the
  foreground back to the shell rather than routing anything.
- A mirror stayed on screen when an application went full screen and the shell hid the taskbar, then
  disappeared when the taskbar came back, which is exactly backwards. A hidden taskbar was being
  read as an absent one, which selected the corner-placement fallback meant for monitors that have
  no secondary taskbar at all. `ShellWatcher` now subscribes to taskbar show and hide events, and
  mirrors are hidden rather than destroyed, so they return with the taskbar.

### Changed

- A left-click on a mirrored icon now activates the real tray icon through UI Automation's
  `InvokePattern` instead of warping the cursor and synthesising a click with `SendInput`. Measured
  on build 26200: every tray element exposes the pattern, invoking one opens its flyout, and the
  cursor does not move. `SendInput` stays as the fallback and as the only route for right clicks,
  middle clicks and the wheel.

### Fixed

- A second click on a mirrored icon whose flyout was already open bounced the flyout to the primary
  monitor and back instead of closing it. Two causes, both now addressed: the routed click reached
  the tray icon after the application had hidden the flyout on losing activation, so the icon
  handler read that as "not showing" and showed it again; and a synthesised click took the
  foreground in the first place. Invoking takes no foreground, and a second click on an open flyout
  is now treated as a dismissal rather than routed. Verified end to end on kDrive: open, relocate to
  the secondary monitor, close, with the cursor unmoved throughout.

- A mirror stayed on screen while an application ran full screen, and the mirror is topmost, so
  the strip floated over the full-screen application. Detection was the missing piece and the
  previous attempt was built on a false premise: measured on build 26200, the shell does not hide
  the taskbar for a full-screen application, it leaves the window visible at the same rectangle and
  lets it be covered, so no visibility event is ever raised. `FullScreenWatcher` now watches the
  windows themselves, per monitor, comparing against monitor bounds rather than the working area so
  that maximised windows are not mistaken for full-screen ones.

- The quick-settings panel behind the Volume and Network icons opened on the primary monitor
  rather than on the mirror. Two separate causes: the shape rule that keeps host windows out also
  excluded it, because it really is created at the full height of the screen; and opening it also
  creates a 0x0 popup that was being accepted as the flyout, relocating nothing while consuming the
  arming so the real panel never moved. An empty rectangle is now rejected before the window class
  is considered.

- A failed tray boundary read hid the mirrors and then left them hidden. Hiding rather than
  tearing down was already the right response, but recovery was left to the next shell event, and
  observed on build 26200 there may not be one: a single failed read left the mirrors hidden with
  the desktop otherwise idle. A failed read is now retried up to five times, one second apart, and
  gives up in favour of waiting for a shell event after that.

### Known limits

- The quick-settings panel stays on the primary monitor. Windows shows it first as a full-height
  `ControlCenterWindow` and moving it during its opening animation dismisses it, so windows taller
  than three quarters of the primary monitor are left alone.
- The mirrored strip does not take the taskbar's tint while transparency effects are on. A DWM
  thumbnail carries the source window's own surface, and the tint comes from an acrylic backdrop
  drawn behind the taskbar during composition. Turning transparency effects off makes both bars a
  flat colour that the mirror matches exactly.

---

[Unreleased]: https://github.com/latticelabs-au/traymirror/commits/HEAD
