$ErrorActionPreference = "Stop"

function Convert-ToProjectXmlPath {
  param([Parameter(Mandatory = $true)][string]$Path)
  return [System.Security.SecurityElement]::Escape($Path.Replace('\', '/'))
}

function Resolve-UnityDataPath {
  $candidates = @()
  if (-not [string]::IsNullOrWhiteSpace($env:UNITY_EDITOR_DATA_PATH)) {
    $candidates += $env:UNITY_EDITOR_DATA_PATH
  }

  if (-not [string]::IsNullOrWhiteSpace($env:UNITY_EDITOR_PATH)) {
    $editorPath = $env:UNITY_EDITOR_PATH
    if ((Split-Path -Leaf $editorPath) -ieq "Unity.exe") {
      $candidates += (Join-Path (Split-Path -Parent $editorPath) "Data")
    }
    else {
      $candidates += (Join-Path $editorPath "Data")
    }
  }

  $candidates += "C:/Program Files/Unity/Editor/Data"
  $hubRoot = "C:/Program Files/Unity/Hub/Editor"
  if (Test-Path $hubRoot) {
    Get-ChildItem -Path $hubRoot -Directory | Sort-Object Name -Descending | ForEach-Object {
      $candidates += (Join-Path $_.FullName "Editor/Data")
    }
  }

  foreach ($candidate in $candidates) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
      continue
    }

    $managed = Join-Path $candidate "Managed"
    if ((Test-Path (Join-Path $managed "UnityEditor.dll")) -and
        (Test-Path (Join-Path $managed "UnityEngine.dll"))) {
      return (Resolve-Path $candidate).Path
    }
  }

  throw "Unity managed assemblies not found. Set UNITY_EDITOR_DATA_PATH to '<Unity>/Editor/Data' or UNITY_EDITOR_PATH to Unity.exe before running Unity package compile smoke."
}

function New-CompileItemXml {
  param([Parameter(Mandatory = $true)][System.IO.FileInfo[]]$Sources)
  return ($Sources | ForEach-Object {
    '    <Compile Include="' + (Convert-ToProjectXmlPath $_.FullName) + '" />'
  }) -join "`n"
}

function Invoke-UnityEditorCompileSmoke {
  $unityData = Resolve-UnityDataPath
  $managed = Join-Path $unityData "Managed"
  $workDir = Join-Path (Get-Location).Path "obj/unity-compile-smoke"
  if (Test-Path $workDir) {
    Remove-Item -LiteralPath $workDir -Recurse -Force
  }
  New-Item -ItemType Directory -Force -Path $workDir | Out-Null

  $coreSources = @(Get-ChildItem packages/unity/Runtime/Core -Filter *.cs | Sort-Object Name)
  $editorSources = @(Get-ChildItem packages/unity/Editor -Filter *.cs | Sort-Object Name)
  if ($coreSources.Count -eq 0 -or $editorSources.Count -eq 0) {
    throw "Unity compile smoke found no package C# sources."
  }

  $coreItems = New-CompileItemXml $coreSources
  $editorItems = New-CompileItemXml $editorSources
  $unityEditorDll = Convert-ToProjectXmlPath (Join-Path $managed "UnityEditor.dll")
  $unityEngineDll = Convert-ToProjectXmlPath (Join-Path $managed "UnityEngine.dll")

  $coreProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>ConfigSheetForge.Core</AssemblyName>
    <RootNamespace>ConfigSheetForge.Core</RootNamespace>
    <DefineConstants>UNITY_5_3_OR_NEWER</DefineConstants>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
$coreItems
  </ItemGroup>
</Project>
"@

  $editorProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>ConfigSheetForge.Editor</AssemblyName>
    <RootNamespace>ConfigSheetForge.Unity.Editor</RootNamespace>
    <DefineConstants>UNITY_5_3_OR_NEWER;UNITY_EDITOR</DefineConstants>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="ConfigSheetForge.Core.csproj" />
    <Reference Include="UnityEditor"><HintPath>$unityEditorDll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine"><HintPath>$unityEngineDll</HintPath><Private>false</Private></Reference>
$editorItems
  </ItemGroup>
</Project>
"@

  $coreProjectPath = Join-Path $workDir "ConfigSheetForge.Core.csproj"
  $editorProjectPath = Join-Path $workDir "ConfigSheetForge.Editor.csproj"
  Set-Content -LiteralPath $coreProjectPath -Value $coreProject -Encoding UTF8
  Set-Content -LiteralPath $editorProjectPath -Value $editorProject -Encoding UTF8

  dotnet build $editorProjectPath -nologo -v:minimal
  if ($LASTEXITCODE -ne 0) {
    throw "Unity Editor compile smoke failed."
  }

  $compiledEditorDll = Join-Path $workDir "bin/Debug/netstandard2.1/ConfigSheetForge.Editor.dll"
  if (-not (Test-Path $compiledEditorDll)) {
    throw "Unity Editor compile smoke did not produce ConfigSheetForge.Editor.dll."
  }
}

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

foreach ($requiredUiText in @("当前状态", "PR 合并上下文", "结果摘要", "详细日志")) {
  if ($window -notlike "*$requiredUiText*") {
    throw "Unity workflow UI is missing expected user-facing text: $requiredUiText"
  }
}

foreach ($retiredUiText in @("当前工作流", "合并输入")) {
  if ($window -like "*$retiredUiText*") {
    throw "Unity workflow UI still contains retired debug text: $retiredUiText"
  }
}

if ($editorSources -notlike "*new UTF8Encoding(false)*") {
  throw "Unity lifecycle inputs JSON must be written as UTF-8 without BOM."
}

Invoke-UnityEditorCompileSmoke

Write-Host "Unity package structure, import, and Editor assembly compile smoke checks look valid."
