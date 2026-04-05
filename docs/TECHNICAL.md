# WinPan X.2 Technical Notes

## Local Development

Build:

```powershell
dotnet build -c Release
```

Unit tests:

```powershell
dotnet test -c Release
```

## Harness Commands

Full all-devices harness:

```powershell
dotnet run -c Release --project src/WinPanX2/WinPanX2.csproj -- --test-all
```

Lifecycle sequence harness:

```powershell
dotnet run -c Release --project src/WinPanX2/WinPanX2.csproj -- --test-sequence
```

Optional explicit logs:

```powershell
dotnet run -c Release --project src/WinPanX2/WinPanX2.csproj -- --test-all --log _logs/harness-all.log
dotnet run -c Release --project src/WinPanX2/WinPanX2.csproj -- --test-sequence --log _logs/harness-sequence.log
```

Default runtime log location:

- `%AppData%\WinPanX.2\winpan-x.2.log`

## Run Locally

Release binary:

```powershell
src/WinPanX2/bin/Release/net8.0-windows/WinPanX2.exe
```

## Packaging

Publish app:

```powershell
dotnet publish src/WinPanX2/WinPanX2.csproj -c Release -r win-x64 --self-contained true
```

Build installer (helper script):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/build-installer.ps1 -Configuration Release -Runtime win-x64
```

Build installer (direct Inno Setup):

```powershell
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\WinPanX2.iss
```

Installer output:

- `installer/Output/WinPanX2-Setup.exe`

## Release Flow

1. Bump versions:
   - `src/WinPanX2/WinPanX2.csproj`
   - `installer/WinPanX2.iss`
2. Run tests and harness commands.
3. Build installer.
4. Commit release changes.
5. Tag and push (example):

```powershell
git tag vX.Y.Z
git push origin main
git push origin vX.Y.Z
```

6. GitHub Actions release workflow publishes installer asset.

## Startup Registry Key

Startup uses:

- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- Value name: `WinPanX2`

## Existing Checklist

See also:

- `RELEASE_CHECKLIST.md`
