param(
  [string]$Version = "",
  [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

# Package-DesktopRelease.ps1: builds the portable Config Sheet Forge Desktop release artifact.

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
  $Version = (git -C $repoRoot describe --tags --abbrev=0)
}

if (-not $Version.StartsWith("v")) {
  $Version = "v$Version"
}

$semver = $Version.TrimStart("v")
$desktopRoot = Join-Path $repoRoot "apps/desktop"
$tauriRoot = Join-Path $desktopRoot "src-tauri"
$packageJsonPath = Join-Path $desktopRoot "package.json"
$tauriConfigPath = Join-Path $tauriRoot "tauri.conf.json"
$cargoTomlPath = Join-Path $tauriRoot "Cargo.toml"

function Assert-ContainsVersion([string]$Path, [string]$Needle, [string]$Name) {
  $text = Get-Content -Raw $Path
  if ($text -notlike "*$Needle*") {
    throw "$Name version does not match release $Needle in $Path"
  }
}

Assert-ContainsVersion $packageJsonPath "`"version`": `"$semver`"" "Desktop package.json"
Assert-ContainsVersion $tauriConfigPath "`"version`": `"$semver`"" "Tauri config"
Assert-ContainsVersion $cargoTomlPath "version = `"$semver`"" "Desktop Cargo.toml"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
  $OutputDirectory = Join-Path $repoRoot "artifacts/desktop"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Push-Location $desktopRoot
try {
  if (Test-Path "package-lock.json") {
    npm ci
  }
  else {
    npm install
  }

  npm run build
  cargo build --release --manifest-path src-tauri/Cargo.toml
}
finally {
  Pop-Location
}

$releaseExe = Join-Path $tauriRoot "target/release/config-sheet-forge-desktop.exe"
if (-not (Test-Path $releaseExe)) {
  throw "Tauri build did not produce expected executable: $releaseExe"
}

$artifactName = "config-sheet-forge-desktop-windows-x64-$Version.zip"
$staging = Join-Path $OutputDirectory "config-sheet-forge-desktop-windows-x64-$Version"
if (Test-Path $staging) {
  Remove-Item -LiteralPath $staging -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $staging | Out-Null
Copy-Item -LiteralPath $releaseExe -Destination (Join-Path $staging "ConfigSheetForgeDesktop.exe") -Force
Set-Content -Path (Join-Path $staging "VERSION.txt") -Value $Version -NoNewline -Encoding UTF8
Set-Content -Path (Join-Path $staging "README.txt") -Value @"
Config Sheet Forge Desktop $Version

Portable Windows x64 build.
Launch ConfigSheetForgeDesktop.exe, or install/open it from the Unity UPM bridge.
"@ -Encoding UTF8

$zipPath = Join-Path $OutputDirectory $artifactName
if (Test-Path $zipPath) {
  Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -Force
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$shaPath = "$zipPath.sha256"
Set-Content -Path $shaPath -Value "$hash  $artifactName" -Encoding ASCII

$manifestPath = Join-Path $OutputDirectory "config-sheet-forge-desktop-windows-x64-$Version.manifest.json"
$manifest = [ordered]@{
  version = $Version
  platform = "windows-x64"
  artifact = $artifactName
  sha256 = $hash
  executable = "ConfigSheetForgeDesktop.exe"
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Desktop artifact: $zipPath"
Write-Host "SHA256: $hash"
