- Build + tests
  - `dotnet build -c Release`
  - `dotnet test -c Release`

- CLI harness (writes logs under `_logs/`)
  - `dotnet run -c Release --project src/WinPanX2/WinPanX2.csproj -- --test-sequence --log _logs/harness-sequence.log`
  - `dotnet run -c Release --project src/WinPanX2/WinPanX2.csproj -- --test-all --log _logs/harness-all.log`
  - Open the log files and verify there are no errors/exceptions

- Smoke run
  - Run `src/WinPanX2/bin/Release/net8.0-windows/WinPanX2.exe`
  - Verify tray icon appears; "Enable Spatial Audio" toggles on/off
  - Play audio, move window, confirm panning follows
  - Pause/resume audio, confirm no busy CPU while idle
  - Exit from tray; confirm audio balance restores

- Display / device changes
  - Change resolution / monitor layout; confirm panning still maps correctly
  - Switch default output device; confirm app rebuilds and continues

- Packaging
  - Bump version in `src/WinPanX2/WinPanX2.csproj`
  - `dotnet publish src/WinPanX2/WinPanX2.csproj -c Release -r win-x64 --self-contained true`
  - Build installer: `"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\WinPanX2.iss`
  - Optional helper: `powershell -NoProfile -ExecutionPolicy Bypass -File installer/build-installer.ps1`

- Release
  - Tag version (e.g. `vX.Y.Z`) and push tag
  - Confirm GitHub Actions release workflow uploads `WinPanX2-Setup.exe`
