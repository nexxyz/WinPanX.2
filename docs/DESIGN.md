# WinPan X.2 Design

This document defines the intended behavior of WinPan X.2 so future changes can be validated against a stable product contract.

## Product Goal

WinPan X.2 is a Windows tray utility that spatializes per-application stereo audio based on where that app's window is located on the desktop.

Core promise:

- Move a window left/right -> audio for that app pans left/right.
- No per-app setup UI required.
- Safe exit restores original per-session stereo balance.

## Scope and Boundaries

In scope:

- Tray-only UX (no main window)
- Per-session stereo panning based on window position
- Multi-monitor virtual desktop support
- Default-device mode and all-devices mode
- Config file and tray menu controls

Out of scope:

- Surround/Atmos room simulation
- DSP effects beyond balance/panning behavior
- Per-app profile editor UI

## Tray UX Contract

- App runs in notification area with a context menu.
- Left-click and right-click both open the same tray menu.
- Menu controls apply immediately and persist where relevant.

Primary tray controls:

- `Enable Spatial Audio`
- `Start with Windows`
- `Follow Mode` (`Original window`, `Most recently active`, `Most recently opened`)
- `Width Limit` (`50%`, `65%`, `80%`, `90%`, `100%`)
- `Center Bias` (`Off`, `Low`, `Medium`, `High`)
- `Apply to all stereo output devices`
- `Open Config`, `Open Log`, `Exit`

Preset/custom menu behavior:

- Preset rows are checked only when the current value matches a preset.
- Non-preset values are shown as disabled checked `Custom (from config: XX%)`.
- The separator before `Custom (from config)` is only visible when that custom row is visible.

## Audio Model

Effective panning has two independent user controls:

- `Width Limit` maps to `MaxPan` (hard cap on left/right extent).
- `Center Bias` maps to `CenterBias` (how strongly positions are pulled toward center).

Defaults:

- `MaxPan = 1.0` (100%)
- `CenterBias = 0.0` (Off)

Smoothing:

- Motion smoothing applies to reduce jitter and abrupt changes.
- New/transitioned sessions move smoothly toward the computed target.

Safety:

- Original stereo state is tracked and restored on exit.

## Window Binding Model

Supported binding behaviors:

- `Sticky` (Original window): stick to initial/selected window until invalid.
- `FollowMostRecent`: follow foreground-most recent app window.
- `FollowMostRecentOpened`: follow newest opened window for that app/process.

Sticky stability behavior:

- Sticky binding is stabilized across topology/device rebuild churn.
- Sticky tracking is maintained at pid-level to avoid rebinding to an unintended newer window.

## Device Model

Modes:

- Default output device only
- All active stereo render devices

Topology handling:

- Device changes (default switch, plug/unplug, enable/disable) trigger rebuild handling.
- Rebuilds are debounced to reduce churn.

## Startup Model

- Startup toggle and installer use:
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  - value name `WinPanX2`
- Legacy `WinPan X.2` run-value is cleaned up by app/installer behavior.

## Configuration Contract

Config path:

- `%AppData%\WinPanX.2\config.json`

Important fields:

- `PollingIntervalMs`
- `SmoothingFactor`
- `LogLevel`
- `BindingMode`
- `MaxPan`
- `CenterBias`
- `ApplyToAllDevices`
- `ExcludedProcesses`

Notes:

- Manual config edits are supported.
- Config is normalized on load/save (clamped numeric bounds, sanitized process list, canonical fallback behavior).
- For preset-backed menu settings, non-preset values remain active and show as `Custom (from config)` in tray.

## Runtime and Reliability

- Single-instance app behavior (second launch exits politely).
- Built-in harness modes for regression checks:
  - `--test-all`
  - `--test-sequence`

## Validation Matrix (Release-Oriented)

- Tray open/close behavior is identical for left and right click.
- Width Limit and Center Bias presets/custom display are correct.
- Sticky mode stays bound to intended window through topology churn.
- Startup toggle and installer both use `WinPanX2` run value.
- Device change events rebuild cleanly without crashes.
- Exit restores original stereo balance.
