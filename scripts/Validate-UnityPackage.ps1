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

Write-Host "Unity package structure looks valid."
