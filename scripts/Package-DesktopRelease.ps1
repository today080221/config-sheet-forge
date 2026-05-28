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
$cliProjectPath = Join-Path $repoRoot "src/cli/ConfigSheetForge.Cli/ConfigSheetForge.Cli.csproj"
$releaseBuildRoot = Join-Path $repoRoot "obj/desktop-release"
$releaseTauriConfigPath = Join-Path $releaseBuildRoot "tauri.conf.release.json"
$releaseExe = Join-Path $tauriRoot "target/release/config-sheet-forge-desktop.exe"
$cliPublishDir = Join-Path $releaseBuildRoot "cli-win-x64"
$sidecarCliExe = Join-Path $cliPublishDir "config-sheet-forge.exe"

function Invoke-Native([string]$FileName, [string[]]$Arguments, [string]$WorkingDirectory) {
  Push-Location $WorkingDirectory
  try {
    & $FileName @Arguments
    if ($LASTEXITCODE -ne 0) {
      throw "$FileName $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
  }
  finally {
    Pop-Location
  }
}

function Assert-ContainsVersion([string]$Path, [string]$Needle, [string]$Name) {
  $text = Get-Content -Raw $Path
  if ($text -notlike "*$Needle*") {
    throw "$Name version does not match release $Needle in $Path"
  }
}

Assert-ContainsVersion $packageJsonPath "`"version`": `"$semver`"" "Desktop package.json"
Assert-ContainsVersion $tauriConfigPath "`"version`": `"$semver`"" "Tauri config"
Assert-ContainsVersion $cargoTomlPath "version = `"$semver`"" "Desktop Cargo.toml"

function Publish-SidecarCli {
  if (Test-Path $cliPublishDir) {
    Remove-Item -LiteralPath $cliPublishDir -Recurse -Force
  }

  New-Item -ItemType Directory -Force -Path $cliPublishDir | Out-Null
  Invoke-Native "dotnet" @(
    "publish",
    $cliProjectPath,
    "-c",
    "Release",
    "-r",
    "win-x64",
    "--self-contained",
    "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o",
    $cliPublishDir
  ) $repoRoot

  if (-not (Test-Path $sidecarCliExe)) {
    throw "Config Sheet Forge sidecar CLI publish did not produce expected executable: $sidecarCliExe"
  }

  & $sidecarCliExe help | Out-Null
  if ($LASTEXITCODE -ne 0) {
    throw "Config Sheet Forge sidecar CLI smoke failed: $sidecarCliExe help"
  }
}

function New-ReleaseTauriConfig {
  New-Item -ItemType Directory -Force -Path $releaseBuildRoot | Out-Null
  $config = Get-Content -Raw $tauriConfigPath | ConvertFrom-Json
  $config.version = $semver
  $config.build.devUrl = "http://config-sheet-forge-release.invalid"
  $config.build.beforeBuildCommand = ""
  $config | ConvertTo-Json -Depth 32 | Set-Content -Path $releaseTauriConfigPath -Encoding UTF8
  return $releaseTauriConfigPath
}

function Read-BinaryText([string]$Path) {
  $bytes = [System.IO.File]::ReadAllBytes($Path)
  return [System.Text.Encoding]::GetEncoding("ISO-8859-1").GetString($bytes)
}

function Assert-ReleaseDesktopDoesNotContainDevUrl([string]$Path) {
  $text = Read-BinaryText $Path
  foreach ($needle in @("http://127.0.0.1:1420", "127.0.0.1:1420", "http://localhost:1420", "localhost:1420")) {
    if ($text.Contains($needle)) {
      throw "Desktop release artifact contains development server URL '$needle'. Use tauri build production output, not cargo build/devUrl output: $Path"
    }
  }
}

function Invoke-DesktopReleaseSmoke([string]$ExecutablePath) {
  Assert-ReleaseDesktopDoesNotContainDevUrl $ExecutablePath
  & $ExecutablePath --smoke-release
  if ($LASTEXITCODE -ne 0) {
    throw "Desktop release smoke failed for $ExecutablePath"
  }
}

function Invoke-DesktopReleaseSidecarSmoke([string]$ExecutablePath) {
  Assert-ReleaseDesktopDoesNotContainDevUrl $ExecutablePath
  & $ExecutablePath --smoke-release --expect-sidecar
  if ($LASTEXITCODE -ne 0) {
    throw "Desktop release sidecar smoke failed for $ExecutablePath"
  }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
  $OutputDirectory = Join-Path $repoRoot "artifacts/desktop"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Push-Location $desktopRoot
try {
  if (Test-Path "package-lock.json") {
    Invoke-Native "npm" @("ci") $desktopRoot
  }
  else {
    Invoke-Native "npm" @("install") $desktopRoot
  }

  Invoke-Native "npm" @("run", "build") $desktopRoot
  $releaseTauriConfig = New-ReleaseTauriConfig
  $originalTauriConfig = Get-Content -Raw $tauriConfigPath
  try {
    Copy-Item -LiteralPath $releaseTauriConfig -Destination $tauriConfigPath -Force
    if (Test-Path $releaseExe) {
      Remove-Item -LiteralPath $releaseExe -Force
    }
    Invoke-Native "npm" @("run", "tauri", "--", "build", "--no-bundle") $desktopRoot
  }
  finally {
    $restoredConfig = $originalTauriConfig.TrimEnd("`r", "`n") + "`n"
    [System.IO.File]::WriteAllText($tauriConfigPath, $restoredConfig, [System.Text.UTF8Encoding]::new($false))
  }
}
finally {
  Pop-Location
}

Publish-SidecarCli

if (-not (Test-Path $releaseExe)) {
  throw "Tauri build did not produce expected executable: $releaseExe"
}

Invoke-DesktopReleaseSmoke $releaseExe

$artifactName = "config-sheet-forge-desktop-windows-x64-$Version.zip"
$staging = Join-Path $OutputDirectory "config-sheet-forge-desktop-windows-x64-$Version"
if (Test-Path $staging) {
  Remove-Item -LiteralPath $staging -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $staging | Out-Null
Copy-Item -LiteralPath $releaseExe -Destination (Join-Path $staging "ConfigSheetForgeDesktop.exe") -Force
Copy-Item -LiteralPath $cliPublishDir -Destination (Join-Path $staging "cli") -Recurse -Force
Set-Content -Path (Join-Path $staging "VERSION.txt") -Value $Version -NoNewline -Encoding UTF8
Set-Content -Path (Join-Path $staging "README.txt") -Value @"
Config Sheet Forge Desktop $Version

Portable Windows x64 build.
Launch ConfigSheetForgeDesktop.exe, or install/open it from the Unity UPM bridge.
Includes cli/config-sheet-forge.exe as a sidecar CLI, so users do not need a global config-sheet-forge install.
"@ -Encoding UTF8

$zipPath = Join-Path $OutputDirectory $artifactName
if (Test-Path $zipPath) {
  Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -Force

$smokeExtract = Join-Path $OutputDirectory "smoke-extract-$Version"
if (Test-Path $smokeExtract) {
  Remove-Item -LiteralPath $smokeExtract -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $smokeExtract | Out-Null
Expand-Archive -LiteralPath $zipPath -DestinationPath $smokeExtract -Force
$extractedExe = Get-ChildItem -Path $smokeExtract -Filter "ConfigSheetForgeDesktop.exe" -Recurse | Select-Object -First 1
if ($null -eq $extractedExe) {
  throw "Desktop zip smoke did not find ConfigSheetForgeDesktop.exe in $zipPath"
}

Invoke-DesktopReleaseSmoke $extractedExe.FullName
Invoke-DesktopReleaseSidecarSmoke $extractedExe.FullName

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
  sidecarCli = "cli/config-sheet-forge.exe"
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Desktop artifact: $zipPath"
Write-Host "SHA256: $hash"
