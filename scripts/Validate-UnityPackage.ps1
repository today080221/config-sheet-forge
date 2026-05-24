$ErrorActionPreference = "Stop"

$required = @(
  "packages/unity/package.json",
  "packages/unity/Runtime/Core/ConfigSheetForge.Core.asmdef",
  "packages/unity/Editor/ConfigSheetForge.Editor.asmdef",
  "packages/unity/Editor/ConfigSheetForgeWindow.cs",
  "packages/unity/Tests/Editor/ConfigSheetForge.Editor.Tests.asmdef",
  "packages/unity/Tests/Editor/ConfigSheetForgeEditorUtilityTests.cs",
  "packages/unity/Samples~/MinimalConfig/config-sheet-forge.config.example.json"
)

foreach ($path in $required) {
  if (-not (Test-Path $path)) {
    throw "Missing Unity package file: $path"
  }
}

$package = Get-Content -Raw packages/unity/package.json | ConvertFrom-Json
if ($package.license -ne "Apache-2.0") {
  throw "Unity package license must be Apache-2.0"
}

$asmdefs = @(
  "packages/unity/Runtime/Core/ConfigSheetForge.Core.asmdef",
  "packages/unity/Editor/ConfigSheetForge.Editor.asmdef",
  "packages/unity/Tests/Editor/ConfigSheetForge.Editor.Tests.asmdef"
)

foreach ($asmdefPath in $asmdefs) {
  $asmdef = Get-Content -Raw $asmdefPath | ConvertFrom-Json
  if ([string]::IsNullOrWhiteSpace($asmdef.name)) {
    throw "Assembly definition has no name: $asmdefPath"
  }
}

$guidMap = @{}
Get-ChildItem packages/unity -Recurse -Filter *.meta | ForEach-Object {
  $content = Get-Content -Raw $_.FullName
  if ($content -match "guid:\s*([0-9a-fA-F]+)") {
    $guid = $Matches[1].ToLowerInvariant()
    if ($guidMap.ContainsKey($guid)) {
      throw "Duplicate Unity meta GUID '$guid' in '$($_.FullName)' and '$($guidMap[$guid])'"
    }
    $guidMap[$guid] = $_.FullName
  }
}

Get-ChildItem packages/unity -Recurse -Filter *.cs | ForEach-Object {
  $meta = $_.FullName + ".meta"
  if (-not (Test-Path $meta)) {
    throw "Missing Unity C# meta file: $meta"
  }
}

$portableStructures = Get-Content -Raw packages/unity/Runtime/Core/PortableStructures.cs
if ($portableStructures -notmatch "#if !UNITY_5_3_OR_NEWER") {
  throw "CLI-only xlsx reader must be excluded from Unity compilation."
}

Write-Host "Unity package structure and import smoke checks look valid."
