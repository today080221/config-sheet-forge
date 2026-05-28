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

foreach ($requiredUiMarker in @("DrawOnboardingCard", "BuildRecommendationText", "DrawWorkflowGuideCards", "DrawGateReportCards", "DrawProjectNewTableActions", "DrawCollapsedOutputStatusBar", "ShowHelpMenu")) {
  if ($window -notlike "*$requiredUiMarker*") {
    throw "Unity workflow UI is missing expected source marker: $requiredUiMarker"
  }
}

foreach ($requiredV413Marker in @("DrawTargetBranchPicker", "GitHub PR 识别", "DrawOwnerRolePicker", "DrawStructuredFieldEditor", "DrawEnumValuesEditor", "已用时间", "后台任务运行中，完成后自动恢复")) {
  if ($window -notlike "*$requiredV413Marker*") {
    throw "Unity v0.4.13 UX source marker is missing: $requiredV413Marker"
  }
}

foreach ($requiredV415Marker in @("ProgramViewPrefKey", "RiskModePrefKey", "策划视图", "程序视图", "DrawViewModeToggle", "DrawRiskModeToggle", "风险配置")) {
  if ($window -notlike "*$requiredV415Marker*") {
    throw "Unity v0.4.15 view/risk mode source marker is missing: $requiredV415Marker"
  }
}

$coreContracts = Get-Content -Raw packages/unity/Runtime/Core/LifecycleContracts.cs
foreach ($requiredV416Marker in @("MergeInputsContract", "merge.inputs.prepare", "merge.compare", "target_branch_workspace.resolve", "missingTargetTables", "tableCount")) {
  if ($coreContracts -notlike "*$requiredV416Marker*") {
    throw "Unity v0.4.16 compare-merge source marker is missing: $requiredV416Marker"
  }
}

foreach ($requiredV416UiMarker in @("ReadonlyRefreshThrottleSeconds", "MaxOutputCharacters", "SetOutputText", "BuildCliStartStatus", "ResultJsonDeclaresFailure(operation")) {
  if ($window -notlike "*$requiredV416UiMarker*") {
    throw "Unity v0.4.16 responsiveness source marker is missing: $requiredV416UiMarker"
  }
}

foreach ($requiredV417CoreMarker in @("TargetBranchBootstrapContract", "bootstrap-target-branch-from-local-xlsx", "ConfirmCreateOnlineSheets", "target_branch.bootstrap.plan")) {
  if (($coreContracts + "`n" + (Get-Content -Raw packages/unity/Runtime/Core/SeedLifecycle.cs)) -notlike "*$requiredV417CoreMarker*") {
    throw "Unity v0.4.17 target-branch bootstrap core marker is missing: $requiredV417CoreMarker"
  }
}

foreach ($requiredV417UiMarker in @("DrawTargetBranchBootstrapCard", "初始化目标分支", "confirmRegistryUpsert", "confirmWriteProjectConfig", "confirmExcelToSoSettings")) {
  if ($window -notlike "*$requiredV417UiMarker*") {
    throw "Unity v0.4.17 target-branch bootstrap UI marker is missing: $requiredV417UiMarker"
  }
}

$seedLifecycle = Get-Content -Raw packages/unity/Runtime/Core/SeedLifecycle.cs
$cliProgram = Get-Content -Raw src/cli/ConfigSheetForge.Cli/Program.cs
foreach ($requiredV418Marker in @("RequiredPreviewFingerprint", "target_branch.bootstrap.postflight", "requestFingerprint", "ValidateTargetBranchBootstrapPostflightAsync", "RequireMatchingTargetBootstrapPreviewAsync", "--preview-result")) {
  if (($coreContracts + "`n" + $seedLifecycle + "`n" + $cliProgram) -notlike "*$requiredV418Marker*") {
    throw "Unity v0.4.18 target-branch apply guard marker is missing: $requiredV418Marker"
  }
}

foreach ($requiredV418UiMarker in @("将写飞书", "--preview-result", "request fingerprint", "postflight: 已通过")) {
  if ($window -notlike "*$requiredV418UiMarker*") {
    throw "Unity v0.4.18 bootstrap apply wizard marker is missing: $requiredV418UiMarker"
  }
}

foreach ($requiredV419CoreMarker in @("MergeReviewContract", "submit-merge-review", "registry.merge_reviews.upsert", "BuildMergeReviewInputSummary", "HydratePrGateReportFromRegistrySnapshot")) {
  if (($coreContracts + "`n" + $cliProgram) -notlike "*$requiredV419CoreMarker*") {
    throw "Unity v0.4.19 merge review source marker is missing: $requiredV419CoreMarker"
  }
}

foreach ($requiredV419UiMarker in @("提交合并审查记录", "RunSubmitMergeReview", "BuildSubmitMergeReviewRequestJson", "approve-schema-review", "approve-waiver")) {
  if ($window -notlike "*$requiredV419UiMarker*") {
    throw "Unity v0.4.19 manual review UI marker is missing: $requiredV419UiMarker"
  }
}

foreach ($requiredV420CoreMarker in @("registry.field.options.ensure", "StatusOptionsReady", "WaivedFailures", "GateState", "missingStatusOptions")) {
  if (($coreContracts + "`n" + $cliProgram) -notlike "*$requiredV420CoreMarker*") {
    throw "Unity v0.4.20 waiver/status-options marker is missing: $requiredV420CoreMarker"
  }
}

foreach ($requiredV420UiMarker in @("已由配置负责人 waiver 临时放行", "BuildRegistryMigrateDryRunCommand", "注册中心字段需要迁移", "写入对象")) {
  if ($window -notlike "*$requiredV420UiMarker*") {
    throw "Unity v0.4.20 PR gate UX marker is missing: $requiredV420UiMarker"
  }
}

foreach ($requiredV421Marker in @("review-status-options", "OnlyReviewStatusOptions", "复制状态选项窄迁移命令", "registry migration review-status-only narrows actions", "registry migrate review-status-only apply skips schema cleanup")) {
  if (($coreContracts + "`n" + $cliProgram + "`n" + $window + "`n" + (Get-Content -Raw tests/ConfigSheetForge.Tests/Program.cs)) -notlike "*$requiredV421Marker*") {
    throw "Unity v0.4.21 review-status-options narrow migration marker is missing: $requiredV421Marker"
  }
}

foreach ($requiredV422Marker in @("NormalizeReviewStatus", "GetRegistryStatusValue", "ParseLarkRecordId", "review status normalizes feishu single select values", "pr gate hydrates json array merge review status", "submit-merge-review apply returns nested record id")) {
  if (($coreContracts + "`n" + $cliProgram + "`n" + (Get-Content -Raw tests/ConfigSheetForge.Tests/Program.cs)) -notlike "*$requiredV422Marker*") {
    throw "Unity v0.4.22 single-select status marker is missing: $requiredV422Marker"
  }
}

foreach ($requiredV423Marker in @("BranchStatusSummary", "SyncCacheSummary", "registry-status", "ResolvedOnlineTables", "bootstrap-current-branch-from-target", "ProjectConfigProbeTrustsLiveRegistryLocatorsOverEmptyProjectSettings")) {
  if (($coreContracts + "`n" + $cliProgram + "`n" + $window + "`n" + (Get-Content -Raw tests/ConfigSheetForge.Tests/Program.cs)) -notlike "*$requiredV423Marker*") {
    throw "Unity v0.4.23 live registry/source-of-truth marker is missing: $requiredV423Marker"
  }
}

$larkProvider = Get-Content -Raw src/providers/lark/ConfigSheetForge.Providers.Lark/LarkCliWorkbookProvider.cs
foreach ($requiredV424Marker in @("lark.read_retry_success", "BuildDefaultReadRange", "TryBuildRangeFromXlsx", "sync-status.local_cache.inspect", "DrawCurrentBranchBootstrapCard", "RunCurrentBranchBootstrapApply", "修复同步预检问题", "cell.bool_invalid", "lark read wrong startRange retries explicit range")) {
  if (($coreContracts + "`n" + $cliProgram + "`n" + $window + "`n" + $larkProvider + "`n" + (Get-Content -Raw tests/ConfigSheetForge.Tests/Program.cs)) -notlike "*$requiredV424Marker*") {
    throw "Unity v0.4.24 sync-cache/current-branch bootstrap marker is missing: $requiredV424Marker"
  }
}

foreach ($requiredV425Marker in @("TryReadXlsxDimensionInfo", "xlsxCellRows", "finalRange", "triangulation reports right-side extra shape", "xlsx dimension a1 uses sheet data used range", "lark read uses xlsx sheet data range when dimension is stale", "多出列", "多出行")) {
  if (($coreContracts + "`n" + $cliProgram + "`n" + $window + "`n" + $larkProvider + "`n" + (Get-Content -Raw tests/ConfigSheetForge.Tests/Program.cs)) -notlike "*$requiredV425Marker*") {
    throw "Unity v0.4.25 stale-dimension/triangulation marker is missing: $requiredV425Marker"
  }
}

foreach ($requiredV426Marker in @("ConfigSheetForgeExcelToSoImporter", "ExcelToScriptableObjectApi", "导入 Unity 配表资产", "当前 ExcelToSO 还指向旧 Excel 路径", "更新 ExcelToSO settings 到 cache", "ProjectSettings/ExcelToScriptableObjectSettings.asset", "ExcelToSO public API 可用")) {
  if (($editorSources + "`n" + $window + "`n" + (Get-Content -Raw packages/unity/README.md) + "`n" + (Get-Content -Raw docs/unity-window.md)) -notlike "*$requiredV426Marker*") {
    throw "Unity v0.4.26 ExcelToSO asset import marker is missing: $requiredV426Marker"
  }
}

$editorAsmdef = Get-Content -Raw packages/unity/Editor/ConfigSheetForge.Editor.asmdef
if ($editorAsmdef -like "*GreatClock.ExcelToScriptableObject.Editor*") {
  throw "ExcelToSO must remain an optional peer backend; do not add a hard asmdef reference."
}

foreach ($retiredViewText in @("new GUIContent(`"高级模式`"", "高级模式：显示 canonical")) {
  if ($window -like "*$retiredViewText*") {
    throw "Unity view mode still contains retired advanced-mode wording: $retiredViewText"
  }
}

foreach ($requiredV414Marker in @("ConfigureToolProcessEnvironment", "CONFIG_SHEET_FORGE_LARK_CLI", "源码 fallback，首次启动较慢", "本机没有找到 lark-cli", "BuildRemoteBranchOptionsFromRefs")) {
  if ($editorSources -notlike "*$requiredV414Marker*") {
    throw "Unity v0.4.14 resolver/nonblocking source marker is missing: $requiredV414Marker"
  }
}

if ($window -like "*EditorGUILayout.Popup(selected, _targetBranchOptions.ToArray())*") {
  throw "Target branch selector must not regress to a non-searchable Popup."
}

foreach ($requiredLayoutText in @("CollapsedOutputBarHeight = 34f", "DrawCollapsedOutputStatusBar", "BottomOutputExpandedPrefKey", "BottomOutputHeightPrefKey", "SetBottomOutputExpanded", "OnboardingDismissedPrefKey", "ShowHelpMenu", "documentationTargets")) {
  if ($window -notlike "*$requiredLayoutText*") {
    throw "Unity output drawer layout is missing expected source marker: $requiredLayoutText"
  }
}

if (-not (Test-Path docs/unity-window.md)) {
  throw "Unity window tutorial docs/unity-window.md is required."
}

$collapsedMatch = [regex]::Match($window, "private void DrawCollapsedOutputStatusBar\(\)(?<body>[\s\S]*?)\r?\n        private void DrawOutputTab")
if (-not $collapsedMatch.Success) {
  throw "Could not inspect collapsed output status bar implementation."
}
$collapsedBody = $collapsedMatch.Groups["body"].Value
foreach ($forbiddenCollapsedLayout in @("BeginScrollView", "ExpandHeight")) {
  if ($collapsedBody -like "*$forbiddenCollapsedLayout*") {
    throw "Collapsed output status bar must not allocate drawer-style layout: $forbiddenCollapsedLayout"
  }
}

foreach ($retiredUiText in @("passed=false", "failures=")) {
  if ($window -like "*$retiredUiText*") {
    throw "Unity workflow UI still contains retired debug text: $retiredUiText"
  }
}

foreach ($retiredPlannerText in @("合并输入", "例如 configOwner")) {
  if ($window -like "*$retiredPlannerText*") {
    throw "Unity workflow UI still contains retired planner-hostile text: $retiredPlannerText"
  }
}

foreach ($retiredLayoutText in @("GUILayout.MaxHeight(contentHeight)", "return 58f;")) {
  if ($window -like "*$retiredLayoutText*") {
    throw "Unity output drawer still contains retired collapsed-layout code: $retiredLayoutText"
  }
}

if ($editorSources -notlike "*new UTF8Encoding(false)*") {
  throw "Unity lifecycle inputs JSON must be written as UTF-8 without BOM."
}

$refreshMergeMatch = [regex]::Match($window, "private void RefreshMergeContext\(\)(?<body>[\s\S]*?)\r?\n        private bool ApplyMergeContextProbeIfDone")
if (-not $refreshMergeMatch.Success) {
  throw "Could not inspect RefreshMergeContext implementation."
}
$refreshMergeBody = $refreshMergeMatch.Groups["body"].Value
foreach ($forbiddenRefreshCall in @("TryRunTool(", "ProbeGitHubPreflight(", "ReadRemoteBranchOptions(")) {
  if ($refreshMergeBody -like "*$forbiddenRefreshCall*") {
    throw "RefreshMergeContext must not synchronously launch external tools on the IMGUI thread: $forbiddenRefreshCall"
  }
}

$applyMergeMatch = [regex]::Match($window, "private bool ApplyMergeContextProbeIfDone\(\)(?<body>[\s\S]*?)\r?\n        private bool TryApplyCachedMergeContextProbe")
if (-not $applyMergeMatch.Success) {
  throw "Could not inspect ApplyMergeContextProbeIfDone implementation."
}
if ($applyMergeMatch.Groups["body"].Value -like "*ReadRemoteBranchOptions(*") {
  throw "ApplyMergeContextProbeIfDone must use the background probe result instead of synchronously rereading remote branches."
}

Invoke-UnityEditorCompileSmoke

Write-Host "Unity package structure, import, and Editor assembly compile smoke checks look valid."
