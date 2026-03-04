# WinPan X.2

WinPan X.2 is a lightweight Windows 11 system tray utility that spatializes per‑application audio based on window position.

Move a window to the left side of your monitor — the sound moves left. Move it right — the sound follows. That’s it. No surround setup required.

This is the maintained and improved successor to the original WinPan projects:

- https://github.com/nexxyz/WinPanX
- https://github.com/nexxyz/WinPanX

Those repositories are now obsolete. WinPan X.2 replaces them.

---

## Features

- Per-window stereo spatialization (constant-power pan law)
- Multi-monitor support (virtual desktop aware)
- Multi-device support (Default or All active devices)
- Per-app exclusion list (config-based)
- Motion smoothing
- Device hot-swap handling (Bluetooth, HDMI, etc.)
- Crash dump generation
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
  - Switch device mode (Default / All)
  - Enable "Follow most recently active window"
  - Open config file
  - Open log file

Most changes apply immediately. No ritual restarts required.

### Device Modes

- **Default mode** spatializes audio on the current Windows default output device.
  - If you change the default output in Windows settings, WinPan X.2 automatically
    reinitializes and follows the new default device.

- **All mode** spatializes audio across all currently active stereo (2‑channel)
  render devices.
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
%AppData%\WinPanX2\config.json
```

Example configuration:

```json
{
  "FollowMostRecent": true,
  "SmoothingFactor": 0.25,
  "DeviceMode": "Default",
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
- Useful when you want Spotify to stay centered while everything else floats around

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

On pushing a tag (e.g. `v1.2.0`):

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

1. Enumerates audio sessions via WASAPI
2. Maps window position to normalized stereo position
3. Applies constant-power pan law
4. Smooths movement to avoid jitter
5. Updates per-session channel volumes

Architecture is modular, analyzer‑enforced, and intentionally boring in all the right ways.

---

## Crash Reporting

Crash dumps are written to:

```
%AppData%\WinPanX2\Crashes
```

If something explodes, attach the `.dmp` file when reporting issues.

---

## License

MIT License

Copyright (c) 2026 Thomas Steirer

---

If you encounter issues, open one here:

https://github.com/nexxyz/WinPanX.2/issues
