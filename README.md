# WinPan X.2

WinPan X.2 is a lightweight Windows 11 system tray utility that spatializes per‑application audio based on window position.

Move a window to the left side of your monitor, and the sound moves left. Move it right, and the sound follows. That’s it. No surround setup required.

This is the maintained and improved successor to the original WinPan projects:

- https://github.com/nexxyz/WinPan
- https://github.com/nexxyz/WinPanX

Those repositories are now obsolete. WinPan X.2 replaces them.

---

## Features

- Per-window stereo spatialization (constant-power pan law)
- Multi-monitor support (virtual desktop aware)
- Multi-device support (Default or All active devices)
- Per-app exclusion list (config-based)
- Motion smoothing
- Newly active sessions transition smoothly from their current balance toward the window position
- Automatically restores original per-app stereo balance on exit
- Device hot-swap handling (Bluetooth, HDMI, etc.)
- Single-instance tray app (second launch exits politely)
- Self-contained installer
- Fully automated CI/CD releases

---

## Installation

1. Go to the Releases page:

   https://github.com/nexxyz/WinPanX.2/releases

2. Download the latest `WinPanX2-Setup.exe`.

3. Run the installer.

The installer:

- Installs per-user by default
- Supports optional per-machine install
- Supports silent install:

  ```
  WinPanX2-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  ```

---

## Usage

After installation:

- WinPan X.2 runs in the system tray
- Spatial audio is enabled automatically
- Use the tray icon to:
  - Toggle spatial audio
  - Adjust panning width
  - Choose follow mode (Original / Most recently active / Most recently opened)
  - Apply to all stereo output devices
  - Open config file
  - Open log file

Most changes apply immediately. No ritual restarts required.

### Output Devices

- By default, WinPan X.2 spatializes audio on the current Windows default output device.
  - If you change the default output in Windows settings, WinPan X.2 automatically
    reinitializes and follows the new default device.

- If you enable "Apply to all stereo output devices", WinPan X.2 spatializes audio across
  all currently active stereo (2+ channel) render devices.
  - When devices are added, removed, enabled, disabled, or when the default device
    changes, WinPan X.2 automatically rebuilds its device list.

### Device Changes

WinPan X.2 automatically adapts when you:

- Plug in or unplug audio devices (USB, Bluetooth, HDMI, etc.)
- Enable or disable devices in Windows sound settings
- Change the Windows default output device

No manual restart is required.

---

## Configuration

Config file location:

```
%AppData%\WinPanX.2\config.json
```

Example configuration:

```jsonc
{
  // Used as a reference time step for smoothing.
  // WinPan X.2 is primarily event-driven and does not continuously poll at this rate.
  "PollingIntervalMs": 30,
  "SmoothingFactor": 0.5,
  "LogLevel": "Info",
  "BindingMode": "Sticky",
  "MaxPan": 1.0,

  // Stored value (0.0 = widest panning, 1.0 = most center-focused).
  // Use the tray menu "Panning Width" for an easy UI.
  "CenterBias": 0.3,

  "ApplyToAllDevices": false,
  "ExcludedProcesses": [
    "explorer",
    "discord"
  ]
}
```

### ExcludedProcesses

- Case-insensitive
- Match by process name (no .exe needed)
- Changes reload automatically (no restart required)
- Excluded apps are left untouched (if WinPan previously modified them, it restores the original balance and then stops touching them)

### BindingMode

Values:

- `Sticky`: Original window (bind once; rebind only when the bound window becomes invalid)
- `FollowMostRecent`: Most recently active window
- `FollowMostRecentOpened`: Most recently opened window for that application

Notes:

- Values are case-sensitive and must match exactly (WinPan X.2 does not auto-normalize whitespace)

---

## Building From Source

Requirements:

- .NET 8 SDK
- Windows 11

Build:

```
dotnet build -c Release
```

Publish self-contained:

```
dotnet publish src/WinPanX2/WinPanX2.csproj -c Release -r win-x64 --self-contained true
```

Build installer (requires Inno Setup 6):

```
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\WinPanX2.iss
```

---

## Automated Releases

Releases are fully automated via GitHub Actions.

On pushing a tag (e.g. `v1.4.0`):

- CI runs
- Project builds
- Installer compiles
- GitHub Release is created
- `WinPanX2-Setup.exe` is uploaded automatically

See workflows here:

- CI: `.github/workflows/ci.yml`
- Release: `.github/workflows/release.yml`

---

## How It Works

WinPan X.2:

1. Tracks window changes via WinEvent hooks (no busy polling)
2. Detects audio sessions via WASAPI/CoreAudio (active and inactive)
3. Maps window position to normalized stereo position
4. Applies constant-power pan law
5. Smooths movement (time-based) to avoid jitter
6. Updates per-session channel volumes
7. Restores original stereo balance on exit

Architecture is modular, analyzer‑enforced, and intentionally boring in all the right ways.

---

## License

MIT License

Copyright (c) 2026 Thomas Steirer

---

If you encounter issues, open one here:

https://github.com/nexxyz/WinPanX.2/issues
