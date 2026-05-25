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

$window = Get-Content -Raw packages/unity/Editor/ConfigSheetForgeWindow.cs
$editorSources = (Get-ChildItem packages/unity/Editor -Recurse -Filter *.cs | ForEach-Object { Get-Content -Raw $_.FullName }) -join "`n"
$requiredMenus = @(
  'MenuItem("Tools/Config Sheet Forge"',
  'MenuItem("Tools/Config Sheet Forge/打开同步窗口"',
  'MenuItem("Tools/Config Sheet Forge/新建配表向导"',
  'MenuItem("Tools/Config Sheet Forge/三方比较与合并"',
  'MenuItem("Tools/Config Sheet Forge/PR 同步检查"'
)
foreach ($menu in $requiredMenus) {
  if ($window -notlike "*$menu*") {
    throw "Missing stable Unity menu contract: $menu"
  }
}

$requiredApis = @(
  "OpenStatusWindow",
  "OpenNewTableWizard",
  "OpenCompareMerge",
  "OpenPrGate"
)
foreach ($api in $requiredApis) {
  if ($window -notmatch "public static void\s+$api\s*\(") {
    throw "Missing stable Unity Editor API: $api"
  }
}

foreach ($requiredText in @("BuildLifecycleInputsJson", "--inputs", "gateReportPath", "Final gate report")) {
  if ($editorSources -notlike "*$requiredText*") {
    throw "Unity project lifecycle UI is missing expected text: $requiredText"
  }
}

if ($editorSources -notlike "*new UTF8Encoding(false)*") {
  throw "Unity lifecycle inputs JSON must be written as UTF-8 without BOM."
}

Write-Host "Unity package structure and import smoke checks look valid."
