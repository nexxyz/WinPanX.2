Param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\WinPanX2\WinPanX2.csproj"
$iss = Join-Path $PSScriptRoot "WinPanX2.iss"
$publishDir = Join-Path $repoRoot "src\WinPanX2\bin\$Configuration\net8.0-windows\$Runtime\publish"

Write-Host "Publishing $Runtime ($Configuration)..."
dotnet publish $project -c $Configuration -r $Runtime --self-contained true

if (!(Test-Path $publishDir)) {
  throw "Publish output not found: $publishDir"
}

$pfX86 = ${env:ProgramFiles(x86)}

$isccCandidates = @(
  (Join-Path $pfX86 "Inno Setup 6\ISCC.exe"),
  (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)

try {
  $appPaths = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe'
  )
  foreach ($k in $appPaths) {
    $p = Get-ItemProperty $k -ErrorAction SilentlyContinue
    if ($p -and $p."(default)") { $isccCandidates += $p."(default)" }
  }
} catch { }

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$iscc) {
  throw "ISCC.exe not found. Install Inno Setup 6, then rerun."
}

Write-Host "Building installer via ISCC..."
& $iscc $iss

Write-Host "Done. Check installer\\Output\\WinPanX2-Setup.exe"
