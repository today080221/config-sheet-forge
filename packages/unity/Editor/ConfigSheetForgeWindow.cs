using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConfigSheetForge.Core;
using UnityEditor;
using UnityEngine;

namespace ConfigSheetForge.Unity.Editor
{
    public sealed class ConfigSheetForgeWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "状态", "配表", "合并", "PR 检查", "输出" };
        private const string PackageVersion = "v0.4.27";
        private const int StatusTab = 0;
        private const int TablesTab = 1;
        private const int MergeTab = 2;
        private const int GateTab = 3;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private const string BottomOutputExpandedPrefKey = "ConfigSheetForge.Unity.BottomOutputExpanded";
        private const string BottomOutputHeightPrefKey = "ConfigSheetForge.Unity.BottomOutputHeight";
        private const string OnboardingDismissedPrefKey = "ConfigSheetForge.Unity.OnboardingDismissed";
        private const string ProgramViewPrefKey = "ConfigSheetForge.Unity.ProgramView";
        private const string LegacyAdvancedModePrefKey = "ConfigSheetForge.Unity.AdvancedMode";
        private const string RiskModePrefKey = "ConfigSheetForge.Unity.RiskMode";
        private const float CollapsedOutputBarHeight = 34f;
        private const float MinBottomDrawerHeight = 220f;
        private const float DefaultBottomDrawerHeight = 260f;
        private const double MergeProbeCacheSeconds = 30;
        private const double RegistryStatusProbeCacheSeconds = 60;
        private const double ReadonlyRefreshThrottleSeconds = 1.5;
        private const int MaxOutputCharacters = 120000;

        private enum WorkflowStatusKind
        {
            Neutral,
            Ok,
            Warning,
            Error,
            Pending
        }

        private struct WorkflowStatusCard
        {
            public string Label;
            public string Status;
            public string Detail;
            public WorkflowStatusKind Kind;

            public WorkflowStatusCard(string label, string status, string detail, WorkflowStatusKind kind)
            {
                Label = label;
                Status = status;
                Detail = detail;
                Kind = kind;
            }
        }

        private struct DashboardAction
        {
            public string Label;
            public string Tooltip;
            public string SafetyText;
            public Action Execute;

            public DashboardAction(string label, string tooltip, string safetyText, Action execute)
            {
                Label = label;
                Tooltip = tooltip;
                SafetyText = safetyText;
                Execute = execute;
            }
        }

        private string _cliPath = "config-sheet-forge";
        private string _rootQuery = "";
        private string _tableId = "";
        private string _tableName = "";
        private string _spreadsheet = "";
        private string _sheetId = "";
        private string _range = "A1:Z500";
        private string _ownerRole = "";
        private string _schemaChangeSummary = "";
        private string _excelPath = "";
        private string _sheetName = "";
        private string _fieldsText = "id | ID | string | 唯一ID" + "\n" + "name | 名称 | string | 显示名称";
        private readonly List<ProjectFieldInput> _fieldRows = new List<ProjectFieldInput>();
        private bool _fieldRowsInitialized;
        private string _basePath = "";
        private string _oursPath = "";
        private string _theirsPath = "";
        private string _mergeReportPath = "merge-report.md";
        private string _mergedPath = "merged.semantic.json";
        private string _mergeTableId = "";
        private string _mergeSourceBranch = "";
        private string _targetBranch = "";
        private string _defaultTargetBranch = "main";
        private string _mergeBase = "";
        private string _prNumber = "";
        private string _prUrl = "";
        private string _githubRepository = "";
        private string _targetFeishuProfile = "";
        private string _targetBranchWikiNodeTitle = "";
        private string _targetBranchWikiNodeUrl = "";
        private string _targetBranchWikiNodeToken = "";
        private bool _allowPrAutoDetect = true;
        private bool _manualTargetBranchOverride;
        private string _mergeContextStatus = "";
        private string _targetBranchSearch = "";
        private List<TargetBranchOption> _targetBranchOptions = new List<TargetBranchOption>();
        private Vector2 _targetBranchScroll;
        private GitHubPreflightSummary _githubPreflight = GitHubPreflightSummary.Unknown();
        private Task<MergeContextProbeResult> _mergeContextTask;
        private MergeContextProbeResult _cachedMergeContextProbe;
        private string _mergeContextProbeKey = "";
        private string _cachedMergeContextProbeKey = "";
        private DateTime _cachedMergeContextProbeUtc;
        private bool _writeBackToMain;
        private bool _confirmWriteMain;
        private bool _confirmSeedApply;
        private bool _confirmSeedExcelToSo;
        private bool _confirmTargetCreateOnlineSheets;
        private bool _confirmTargetRegistryUpsert;
        private bool _confirmTargetSchemaReviews;
        private bool _confirmTargetWriteLocalCache;
        private bool _confirmTargetWriteProjectConfig;
        private bool _confirmTargetExcelToSo;
        private bool _confirmCurrentBranchCreateOnlineSheets;
        private bool _confirmCurrentBranchRegistryUpsert;
        private bool _confirmCurrentBranchSchemaReviews;
        private bool _confirmSyncApply;
        private bool _confirmExcelToSoSettingsToCache;
        private bool _confirmNewTableApply;
        private bool _showAdvancedDiagnostics;
        private bool _showSyncSection = true;
        private bool _showNewTableSection;
        private bool _showSeedSection;
        private bool _showMergeAdvancedOptions;
        private bool _showFieldTemplateEditor;
        private bool _showOnboarding;
        private bool _showWorkflowGuide;
        private bool _showBottomOutput;
        private bool _programView;
        private bool _riskModeUnlocked;
        private bool _isResizingOutputPanel;
        private float _bottomOutputHeight = 260f;
        private string _output = "";
        private readonly StringBuilder _outputBuilder = new StringBuilder();
        private string _resultSummary = "";
        private string _lastCommand = "";
        private string _lastResultPath = "";
        private string _lastLifecycleDir = "";
        private string _lastCompareMergeResultPath = "";
        private string _lastCompareMergeRequestFingerprint = "";
        private string _mergeReviewComment = "";
        private bool _highlightMergeReview;
        private bool _showSchemaReviewEntry;
        private bool _showWaiverEntry;
        private string _schemaReviewTableId = "";
        private string _schemaReviewComment = "";
        private string _waiverTableId = "__project_pr_gate__";
        private string _waiverReason = "";
        private string _waiverExpiresAt = "";
        private string _lastCompletedOperation = "";
        private string _lastCompletedInputFingerprint = "";
        private bool _lastCompletedDryRun;
        private bool _lastCompletedSuccess;
        private bool _showRecentCommand;
        private bool _showDetailedLogs;
        private int _selectedTab;
        private ProjectConfigSummary _projectConfig = new ProjectConfigSummary();
        private string _currentGitBranch = "";
        private GateReportSummaryView _gateReportSummary = GateReportSummaryView.NotFound("");
        private ConfigSheetForgeCliInvocation _cliInvocation = ConfigSheetForgeCliInvocation.Unresolved("config-sheet-forge", "未刷新", "尚未解析 CLI。");
        private ConfigSheetForgeBackgroundJob _activeJob;
        private ExcelToSoUnityImportSession _excelToSoImportSession;
        private string _activeJobStatus = "";
        private Vector2 _mainScroll;
        private Vector2 _outputScroll;
        private DateTime _lastReadonlyRefreshUtc;
        private DateTime _lastRegistryStatusProbeUtc;
        private string _lastRegistryStatusProbeKey = "";

        [MenuItem("Tools/Config Sheet Forge", false, 1000)]
        public static void OpenStatusWindow()
        {
            OpenTab(StatusTab);
        }

        [MenuItem("Tools/Config Sheet Forge/打开同步窗口")]
        public static void OpenStatusWindowMenu()
        {
            OpenStatusWindow();
        }

        [MenuItem("Tools/Config Sheet Forge/新建配表向导")]
        public static void OpenNewTableWizard()
        {
            OpenTab(TablesTab);
        }

        [MenuItem("Tools/Config Sheet Forge/本地 Excel Seed")]
        public static void OpenSeedFromLocalXlsx()
        {
            OpenTab(TablesTab);
        }

        [MenuItem("Tools/Config Sheet Forge/同步在线 Cache")]
        public static void OpenSyncCache()
        {
            OpenTab(StatusTab);
        }

        [MenuItem("Tools/Config Sheet Forge/三方比较与合并")]
        public static void OpenCompareMerge()
        {
            OpenTab(MergeTab);
        }

        [MenuItem("Tools/Config Sheet Forge/PR 同步检查")]
        public static void OpenPrGate()
        {
            OpenTab(GateTab);
        }

        private static void OpenTab(int tab)
        {
            var window = GetWindow<ConfigSheetForgeWindow>("配表 Source of Truth");
            window.titleContent = new GUIContent("配表 Source of Truth");
            window.minSize = new Vector2(640, 520);
            window._selectedTab = tab;
            window.RefreshReadonlyStatus(force: true);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("配表 Source of Truth");
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            _showBottomOutput = EditorPrefs.GetBool(BottomOutputExpandedPrefKey, false);
            _bottomOutputHeight = EditorPrefs.HasKey(BottomOutputHeightPrefKey) ? EditorPrefs.GetFloat(BottomOutputHeightPrefKey, DefaultBottomDrawerHeight) : 0f;
            _showOnboarding = !EditorPrefs.GetBool(OnboardingDismissedPrefKey, false);
            _programView = EditorPrefs.GetBool(ProgramViewPrefKey, EditorPrefs.GetBool(LegacyAdvancedModePrefKey, false));
            _riskModeUnlocked = EditorPrefs.GetBool(RiskModePrefKey, false);
            RefreshReadonlyStatus(force: true);
            EnsureNewTableDefaults();
            _resultSummary = "配表 Source of Truth 窗口已打开。" + Environment.NewLine +
                             "这里只刷新本地状态，不会下载、不导出、不改文件。" + Environment.NewLine +
                             "主流程只保留刷新状态、预览同步计划、运行 PR 检查。";
            SetOutputText("");
            StartRegistryStatusProbeIfNeeded(force: false);
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (IsJobRunning)
            {
                _activeJob.Cancel("窗口已关闭，已取消，未写本地 cache。");
            }

            if (IsExcelToSoImportRunning)
            {
                _excelToSoImportSession.Cancel("窗口已关闭，已取消后续 Unity asset 导入。");
            }
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawProjectSummary();
            DrawActiveJobStatus();

            _selectedTab = GUILayout.Toolbar(_selectedTab, Tabs);

            if (_selectedTab == 4)
            {
                DrawOutputTab(fullPage: true, preferredHeight: Math.Max(320f, position.height - 230f));
                return;
            }

            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll, GUILayout.ExpandHeight(true));
            switch (_selectedTab)
            {
                case 0:
                    DrawStartTab();
                    break;
                case 1:
                    DrawTablesTab();
                    break;
                case 2:
                    DrawMergeTab();
                    break;
                case 3:
                    DrawGateTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
            if (_showBottomOutput)
            {
                DrawOutputResizeHandle();
                DrawOutputTab(fullPage: false, preferredHeight: CalculateInlineOutputHeight());
            }
            else
            {
                DrawCollapsedOutputStatusBar();
            }
        }

        private void DrawHeader()
        {
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("配表 Source of Truth", EditorStyles.boldLabel);
            DrawViewModeToggle();
            DrawRiskModeToggle();

            if (GUILayout.Button(new GUIContent("教程", "打开 5 分钟入门、项目文档或飞书入口。"), GUILayout.Width(64)))
            {
                ShowHelpMenu();
            }

            if (GUILayout.Button(new GUIContent("复制 UPM", "复制通过 Unity Package Manager 安装此包的 Git URL。"), GUILayout.Width(88)))
            {
                EditorGUIUtility.systemCopyBuffer = "https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#" + PackageVersion;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("飞书在线 Sheet 是 Source of Truth，本地 Excel 只是兼容缓存。", EditorStyles.miniLabel);
            GUILayout.Space(4);
        }

        private void DrawViewModeToggle()
        {
            var oldProgramView = _programView;
            EditorGUILayout.BeginHorizontal(GUILayout.Width(132));
            if (GUILayout.Toggle(!_programView, new GUIContent("策划视图", "默认视图：用人话说明当前状态、下一步和安全性。"), EditorStyles.toolbarButton, GUILayout.Width(66)))
            {
                _programView = false;
            }

            if (GUILayout.Toggle(_programView, new GUIContent("程序视图", "显示内部 key、canonical 类型、路径和命令摘要，但不自动解锁危险配置。"), EditorStyles.toolbarButton, GUILayout.Width(66)))
            {
                _programView = true;
            }
            EditorGUILayout.EndHorizontal();
            if (oldProgramView != _programView)
            {
                EditorPrefs.SetBool(ProgramViewPrefKey, _programView);
            }
        }

        private void DrawRiskModeToggle()
        {
            var oldRiskMode = _riskModeUnlocked;
            var tooltip = "解锁风险配置入口，例如手动覆盖路径、raw 字段模板、手动覆盖目标分支。危险写入仍需要预览成功、勾选确认和二次确认。";
            _riskModeUnlocked = GUILayout.Toggle(_riskModeUnlocked, new GUIContent("高级", tooltip), EditorStyles.toolbarButton, GUILayout.Width(54));
            if (oldRiskMode != _riskModeUnlocked)
            {
                if (_riskModeUnlocked)
                {
                    var confirmed = EditorUtility.DisplayDialog(
                        "开启高级入口",
                        "高级入口会显示手动路径、raw 模板、目标分支覆盖等容易误操作的配置项。它不会自动执行写入；危险操作仍需要预览成功、勾选确认和二次确认。",
                        "开启",
                        "取消");
                    if (!confirmed)
                    {
                        _riskModeUnlocked = false;
                    }
                }

                EditorPrefs.SetBool(RiskModePrefKey, _riskModeUnlocked);
            }
        }

        private void ShowHelpMenu()
        {
            var menu = new GenericMenu();
            var projectRoot = FindProjectRoot();
            AddHelpMenuItem(menu, "5 分钟入门", "https://github.com/today080221/config-sheet-forge/blob/main/docs/unity-window.md");
            AddHelpMenuItem(menu, "策划改表流程", "https://github.com/today080221/config-sheet-forge/blob/main/docs/unity-window.md#策划改表-5-分钟流程");
            AddHelpMenuItem(menu, "新建配表流程", "https://github.com/today080221/config-sheet-forge/blob/main/docs/unity-window.md#新建配表流程");
            AddHelpMenuItem(menu, "PR 合并流程", "https://github.com/today080221/config-sheet-forge/blob/main/docs/unity-window.md#pr-合并流程");
            AddHelpMenuItem(menu, "常见失败原因", "https://github.com/today080221/config-sheet-forge/blob/main/docs/unity-window.md#常见失败原因");
            menu.AddSeparator("");

            var addedProjectLink = false;
            foreach (var pair in _projectConfig.DocumentationTargets)
            {
                var label = string.IsNullOrWhiteSpace(pair.Key) ? "项目文档" : "项目文档/" + pair.Key;
                AddHelpMenuItem(menu, label, pair.Value);
                addedProjectLink = true;
            }

            var defaultProjectDoc = Path.Combine(projectRoot, "docs", "tooling", "config-sheet-source-of-truth.md");
            if (File.Exists(defaultProjectDoc))
            {
                AddHelpMenuItem(menu, "项目文档/Source of Truth 工具说明", defaultProjectDoc);
                addedProjectLink = true;
            }

            if (!string.IsNullOrWhiteSpace(_projectConfig.BranchWorkspaceRootWikiUrl))
            {
                AddHelpMenuItem(menu, "项目文档/飞书项目配置入口", _projectConfig.BranchWorkspaceRootWikiUrl);
                addedProjectLink = true;
            }

            if (!addedProjectLink)
            {
                menu.AddDisabledItem(new GUIContent("项目文档/未在项目配置中声明"));
            }

            menu.AddSeparator("");
            AddHelpMenuItem(menu, "config-sheet-forge README", "https://github.com/today080221/config-sheet-forge");
            menu.ShowAsContext();
        }

        private void AddHelpMenuItem(GenericMenu menu, string label, string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                menu.AddDisabledItem(new GUIContent(label + "（未配置）"));
                return;
            }

            menu.AddItem(new GUIContent(label), false, () => OpenDocumentationTarget(target));
        }

        private void OpenDocumentationTarget(string target)
        {
            target = target ?? "";
            if (IsUrl(target))
            {
                Application.OpenURL(target);
                return;
            }

            var projectRoot = FindProjectRoot();
            var resolved = Path.IsPathRooted(target)
                ? target
                : Path.GetFullPath(Path.Combine(projectRoot, target.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(resolved) || Directory.Exists(resolved))
            {
                Application.OpenURL(new Uri(resolved).AbsoluteUri);
                return;
            }

            SetImmediateOutput(
                "没有找到项目文档：" + target,
                "请确认项目配置 documentationTargets/localDocs/feishuRootUrl 是否正确，或在项目中添加 docs/tooling/config-sheet-source-of-truth.md。");
        }

        private static bool IsUrl(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("file://", StringComparison.OrdinalIgnoreCase));
        }

        private void DrawActiveJobStatus()
        {
            if (IsExcelToSoImportRunning)
            {
                DrawExcelToSoImportStatus();
                return;
            }

            if (!IsJobRunning)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            var spinner = "|/-\\"[(int)(EditorApplication.timeSinceStartup * 4) % 4].ToString();
            var elapsed = _activeJob == null ? 0 : _activeJob.ElapsedSeconds;
            EditorGUILayout.LabelField(spinner + " 正在运行：" + OperationDisplayName(_activeJob == null ? "" : _activeJob.Operation), EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("取消", "终止当前 adapter / CLI / lark-cli 进程树。"), GUILayout.Width(80)))
            {
                CancelActiveJob();
            }
            EditorGUILayout.EndHorizontal();

            DrawReadonlyRow("当前阶段", FirstNonEmpty(_activeJobStatus, "后台任务运行中"), "后台 job 当前阶段。");
            DrawReadonlyRow("已用时间", Math.Floor(elapsed).ToString("0") + " 秒", "长时间读取飞书或导出 xlsx 时可能需要等待。");
            EditorGUILayout.LabelField(BuildJobSafetyText(_activeJob), EditorStyles.wordWrappedMiniLabel);
            if (elapsed >= 60)
            {
                EditorGUILayout.HelpBox("耗时较长，如怀疑卡住可取消后重试；取消不会写入本地 cache。", MessageType.Warning);
            }
            else if (elapsed >= 15)
            {
                EditorGUILayout.HelpBox("仍在处理。飞书读取/导出可能需要一点时间，可以切到“输出”页看日志。", MessageType.Info);
            }
            else if (_activeJob != null && _activeJob.DryRun)
            {
                EditorGUILayout.HelpBox("dry-run：只生成预览，不写飞书、不改本地 cache、不改 ProjectSettings。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("任务在后台运行；窗口仍可滚动、复制命令和切换 tab。运行中会禁用相关执行按钮，避免重复点击。", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawExcelToSoImportStatus()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            var spinner = "|/-\\"[(int)(EditorApplication.timeSinceStartup * 4) % 4].ToString();
            EditorGUILayout.LabelField(spinner + " 正在导入 Unity 配表资产", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("取消", "当前表导入完成后停止后续表。"), GUILayout.Width(80)))
            {
                if (_excelToSoImportSession != null)
                {
                    _excelToSoImportSession.Cancel("已取消，未继续导入后续 Unity asset。");
                }
            }
            EditorGUILayout.EndHorizontal();
            if (_excelToSoImportSession != null)
            {
                DrawReadonlyRow("当前阶段", _excelToSoImportSession.Status, "Unity Editor 本地导入，不访问飞书、不写 registry。");
                DrawReadonlyRow("进度", _excelToSoImportSession.ProgressText, "按表逐个调用 ExcelToSO public API。");
                DrawReadonlyRow("已用时间", Math.Floor(_excelToSoImportSession.ElapsedSeconds).ToString("0") + " 秒", "导入 ScriptableObject asset 可能需要等待 Unity 序列化。");
            }
            EditorGUILayout.HelpBox("只写 Unity asset；不会写飞书、不会改在线表、不会改 registry、不会写 main。", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private static string OperationDisplayName(string operation)
        {
            if (string.Equals(operation, "sync-cache", StringComparison.OrdinalIgnoreCase))
            {
                return "预览同步计划 / 写入本地 cache";
            }

            if (string.Equals(operation, "registry-status", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "branch-status", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "sync-status", StringComparison.OrdinalIgnoreCase))
            {
                return "读取在线注册中心状态";
            }

            if (string.Equals(operation, "new-table", StringComparison.OrdinalIgnoreCase))
            {
                return "新建配表";
            }

            if (string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase))
            {
                return "合并预览";
            }

            if (string.Equals(operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return "初始化目标分支";
            }

            if (string.Equals(operation, "bootstrap-current-branch-from-target", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "branch-workspace-bootstrap-from-target", StringComparison.OrdinalIgnoreCase))
            {
                return "从目标分支初始化当前分支";
            }

            if (string.Equals(operation, "pr-gate-report", StringComparison.OrdinalIgnoreCase))
            {
                return "PR 检查";
            }

            if (string.Equals(operation, "submit-merge-review", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "approve-merge-review", StringComparison.OrdinalIgnoreCase))
            {
                return "提交合并审查记录";
            }

            if (string.Equals(operation, "approve-schema-review", StringComparison.OrdinalIgnoreCase))
            {
                return "处理 Schema 审查";
            }

            if (string.Equals(operation, "approve-waiver", StringComparison.OrdinalIgnoreCase))
            {
                return "批准 waiver";
            }

            if (string.Equals(operation, "seed-from-local-xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "bootstrap-from-local-xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return "本地 Excel Seed";
            }

            return FirstNonEmpty(operation, "后台任务");
        }

        private static string BuildJobSafetyText(ConfigSheetForgeBackgroundJob job)
        {
            if (job == null)
            {
                return "后台任务运行中，完成后按钮会自动恢复。";
            }

            if (job.DryRun)
            {
                if (string.Equals(job.Operation, "registry-status", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(job.Operation, "branch-status", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(job.Operation, "sync-status", StringComparison.OrdinalIgnoreCase))
                {
                    return "安全性：只读注册中心状态，不读取/导出在线 Sheet，不写飞书、不改本地 cache。";
                }

                return "安全性：dry-run 只读取和生成预览，不写飞书、不改本地 cache、不改 ProjectSettings。";
            }

            if (string.Equals(job.Operation, "sync-cache", StringComparison.OrdinalIgnoreCase))
            {
                return "安全性：apply 会读取在线 Sheet、导出 xlsx，三方一致后才可能写入本地 cache。";
            }

            if (string.Equals(job.Operation, "new-table", StringComparison.OrdinalIgnoreCase))
            {
                return "安全性：apply 会创建或复用在线 Sheet，并登记到在线注册中心。";
            }

            if (string.Equals(job.Operation, "compare-merge", StringComparison.OrdinalIgnoreCase))
            {
                return "安全性：写回 main 只有在显式确认后才会发生。";
            }

            if (string.Equals(job.Operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return "安全性：初始化目标分支按分项确认写入；未勾选的 cache、ProjectSettings、ExcelToSO 不会写。";
            }

            if (string.Equals(job.Operation, "bootstrap-current-branch-from-target", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(job.Operation, "branch-workspace-bootstrap-from-target", StringComparison.OrdinalIgnoreCase))
            {
                return "安全性：当前入口先生成从目标分支派生当前分支的预览，不写本地 cache、不改 ProjectSettings。";
            }

            if (string.Equals(job.Operation, "submit-merge-review", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(job.Operation, "approve-merge-review", StringComparison.OrdinalIgnoreCase))
            {
                return "安全性：只写 Base MergeReviews，不写 main、不写本地 cache、不改 ProjectSettings 或 ExcelToSO。";
            }

            if (string.Equals(job.Operation, "approve-schema-review", StringComparison.OrdinalIgnoreCase))
            {
                return "安全性：只写 Base SchemaReviews，不写 main、不写本地 cache、不改 ProjectSettings 或 ExcelToSO。";
            }

            if (string.Equals(job.Operation, "approve-waiver", StringComparison.OrdinalIgnoreCase))
            {
                return "安全性：只写 Base Waivers；waiver 必须有原因和过期时间，不会写 main 或 cache。";
            }

            return "安全性：后台任务已通过 UI 确认，完成后按钮会自动恢复。";
        }

        private void DrawTargetBranchField()
        {
            if (!string.IsNullOrWhiteSpace(_prNumber))
            {
                DrawReadonlyRow("目标分支", FirstNonEmpty(_targetBranch, _defaultTargetBranch, "main"), "来自 GitHub PR base branch。一般不需要手动修改。");
                EditorGUILayout.LabelField("已识别 GitHub PR #" + _prNumber + "，目标分支来自 PR：" + FirstNonEmpty(_targetBranch, _defaultTargetBranch, "main"), EditorStyles.wordWrappedMiniLabel);
                return;
            }

            DrawTargetBranchPicker("目标分支", "没有识别到 PR 时，从远端分支中搜索并选择目标分支。", manualOverride: false);
        }

        private void DrawTargetBranchPicker(string label, string tooltip, bool manualOverride)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), GUILayout.Width(110));
            var previous = _targetBranch;
            _targetBranch = EditorGUILayout.TextField(_targetBranch);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("搜索分支", "支持子串和简单 fuzzy 搜索。"), GUILayout.Width(110));
            _targetBranchSearch = EditorGUILayout.TextField(_targetBranchSearch);
            EditorGUILayout.EndHorizontal();

            if (_targetBranchOptions.Count == 0)
            {
                EditorGUILayout.HelpBox("没有读取到远端分支列表。可以先 fetch，或直接在上方输入目标分支。", MessageType.Warning);
            }
            else
            {
                var matches = BuildFilteredTargetBranchOptions(_targetBranchSearch);
                var rowCount = Math.Min(Math.Max(matches.Count, 1), 6);
                _targetBranchScroll = EditorGUILayout.BeginScrollView(_targetBranchScroll, GUILayout.Height(24f * rowCount + 10f));
                for (var i = 0; i < matches.Count; i++)
                {
                    var option = matches[i];
                    EditorGUILayout.BeginHorizontal();
                    var selected = string.Equals(option.Name, _targetBranch, StringComparison.OrdinalIgnoreCase);
                    if (GUILayout.Button(new GUIContent((selected ? "✓ " : "  ") + BuildBranchOptionLabel(option), BuildBranchOptionTooltip(option)), selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(22)))
                    {
                        _targetBranch = option.Name;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (matches.Count == 0)
                {
                    EditorGUILayout.LabelField("没有匹配的远端分支，可直接输入目标分支。", EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.EndScrollView();
            }

            if (!string.Equals(previous, _targetBranch, StringComparison.OrdinalIgnoreCase))
            {
                OnTargetBranchChanged(manualOverride);
            }

            EditorGUILayout.EndVertical();
        }

        private List<TargetBranchOption> BuildFilteredTargetBranchOptions(string query)
        {
            var matches = new List<TargetBranchOption>();
            query = (query ?? "").Trim();
            for (var i = 0; i < _targetBranchOptions.Count; i++)
            {
                var option = _targetBranchOptions[i];
                if (string.IsNullOrWhiteSpace(query) ||
                    option.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    IsFuzzyMatch(option.Name, query))
                {
                    matches.Add(option);
                }

                if (string.IsNullOrWhiteSpace(query) && matches.Count >= 80)
                {
                    break;
                }
            }

            return matches;
        }

        private static bool IsFuzzyMatch(string text, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            var source = (text ?? "").ToLowerInvariant();
            var pattern = query.ToLowerInvariant();
            var index = 0;
            for (var i = 0; i < source.Length && index < pattern.Length; i++)
            {
                if (source[i] == pattern[index])
                {
                    index++;
                }
            }

            return index == pattern.Length;
        }

        private string BuildBranchOptionLabel(TargetBranchOption option)
        {
            var label = option.Name;
            if (option.IsPrBase)
            {
                label += "  [PR 目标]";
            }
            else if (option.IsDefault)
            {
                label += "  [项目默认]";
            }

            if (!string.IsNullOrWhiteSpace(option.LastCommitText))
            {
                label += "  " + option.LastCommitText;
            }

            if (_programView && !string.IsNullOrWhiteSpace(option.Source))
            {
                label += "  source=" + option.Source;
            }

            return label;
        }

        private static string BuildBranchOptionTooltip(TargetBranchOption option)
        {
            return "分支：" + option.Name + Environment.NewLine +
                   (option.IsPrBase ? "来源：GitHub PR base" : option.IsDefault ? "来源：项目默认目标分支" : "来源：远端分支") + Environment.NewLine +
                   "最近提交：" + FirstNonEmpty(option.LastCommitText, "未读取到");
        }

        private void OnTargetBranchChanged(bool manualOverride)
        {
            _manualTargetBranchOverride = manualOverride;
            _mergeContextTask = null;
            _prNumber = "";
            _prUrl = "";
            RefreshMergeContext();
        }

        private string BuildPrText()
        {
            if (!string.IsNullOrWhiteSpace(_prNumber))
            {
                return "#" + _prNumber + " -> " + FirstNonEmpty(_prUrl, "未记录 URL");
            }

            if (_manualTargetBranchOverride)
            {
                return "已手动覆盖目标分支：" + FirstNonEmpty(_targetBranch, _defaultTargetBranch, "main");
            }

            return _allowPrAutoDetect ? "未识别到 PR，使用目标分支 fallback" : "项目配置关闭 PR 自动识别";
        }

        private void DrawGitHubPreflightCard()
        {
            if (_githubPreflight == null)
            {
                return;
            }

            var messageType = _githubPreflight.IsReady ? MessageType.Info : MessageType.Warning;
            EditorGUILayout.HelpBox(_githubPreflight.Status + "\n下一步：" + _githubPreflight.NextStep, messageType);
            if (!_githubPreflight.IsReady && !_githubPreflight.GhAvailable && GUILayout.Button(new GUIContent("打开 GitHub CLI 安装页", "安装后运行 gh auth login，就能自动识别 PR base branch。"), GUILayout.Height(22)))
            {
                Application.OpenURL(FirstNonEmpty(_githubPreflight.InstallHelpUrl, "https://cli.github.com/"));
            }

            if (_programView)
            {
                DrawReadonlyRow("GitHub remote", _githubPreflight.RemoteIsGitHub ? FirstNonEmpty(_githubRepository, "已识别") : "未识别", "来自项目 config 或 git remote。");
                DrawReadonlyRow("gh", _githubPreflight.GhAvailable ? (_githubPreflight.GhAuthenticated ? "已安装且已登录" : "已安装但未登录") : "未安装/不可用", "用于自动识别 GitHub PR。");
            }
        }

        private string BuildMergeTablesText()
        {
            if (_projectConfig.CurrentBranchTables.Count == 0)
            {
                return "当前 branch/profile 暂无在线表记录";
            }

            if (!string.IsNullOrWhiteSpace(_mergeTableId))
            {
                return "单表：" + _mergeTableId;
            }

            return _projectConfig.CurrentBranchTables.Count.ToString() + " 张当前分支在线表";
        }

        private string BuildMergeStatusText()
        {
            if (!string.IsNullOrWhiteSpace(_prNumber))
            {
                return "已识别 PR，目标分支为 " + FirstNonEmpty(_targetBranch, _defaultTargetBranch, "main");
            }

            if (string.IsNullOrWhiteSpace(_mergeSourceBranch) || string.IsNullOrWhiteSpace(_targetBranch))
            {
                return "正在识别分支";
            }

            return "使用目标分支 fallback";
        }

        private string BuildMergeNextStepText()
        {
            if (_writeBackToMain && _confirmWriteMain)
            {
                return LastPreviewPassed("compare-merge") ? "确认写回 main" : "先生成合并预览";
            }

            return "生成合并预览";
        }

        private string BuildTargetWikiText()
        {
            if (!string.IsNullOrWhiteSpace(_targetBranchWikiNodeTitle))
            {
                return _targetBranchWikiNodeTitle;
            }

            if (!string.IsNullOrWhiteSpace(_targetBranchWikiNodeUrl) || !string.IsNullOrWhiteSpace(_targetBranchWikiNodeToken))
            {
                return "已绑定 Wiki 节点";
            }

            return "按分支规则推导中";
        }

        private void DrawProjectSummary()
        {
            var projectRoot = FindProjectRoot();
            if (_projectConfig == null)
            {
                RefreshReadonlyStatus();
            }

            DrawOnboardingCard();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("推荐下一步", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(BuildRecommendationText(projectRoot), EditorStyles.wordWrappedLabel);

            var primary = BuildPrimaryDashboardAction(projectRoot);
            var secondary = BuildSecondaryDashboardAction(projectRoot, primary.Label);
            EditorGUILayout.BeginHorizontal();
            DrawDashboardActionButton(primary, true);
            DrawDashboardActionButton(secondary, false);
            if (GUILayout.Button(new GUIContent(_showBottomOutput ? "收起结果" : "展开结果", "显示或隐藏底部结果摘要面板。"), GUILayout.Width(92), GUILayout.Height(32)))
            {
                SetBottomOutputExpanded(!_showBottomOutput);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(primary.SafetyText, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(secondary.SafetyText, EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(94);
            EditorGUILayout.EndHorizontal();

            _showWorkflowGuide = EditorGUILayout.Foldout(_showWorkflowGuide, "我该做什么", true);
            if (_showWorkflowGuide)
            {
                DrawWorkflowGuideCards();
            }

            GUILayout.Space(4);
            EditorGUILayout.LabelField("当前状态", EditorStyles.boldLabel);
            DrawStatusCardGrid(BuildWorkflowStatusCards(projectRoot));

            _cliInvocation = ConfigSheetForgeEditorUtility.ResolveCoreCli(_projectConfig, projectRoot, _cliPath);
            EditorGUILayout.EndVertical();
        }

        private void DrawOnboardingCard()
        {
            if (!_showOnboarding)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("配表 Source of Truth 怎么用？", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("飞书在线表是正式源头；本地 Excel 只是自动生成的 cache。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("“预览”永远安全，不会写飞书，也不会改本地文件。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("“写入 / 创建 / 写回”都需要确认；不知道下一步时先点“预览同步计划”。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("我知道了", "只在本次窗口会话中收起说明。"), GUILayout.Width(92)))
            {
                _showOnboarding = false;
            }

            if (GUILayout.Button(new GUIContent("打开教程", "打开 Unity 窗口 5 分钟入门。"), GUILayout.Width(92)))
            {
                OpenDocumentationTarget("https://github.com/today080221/config-sheet-forge/blob/main/docs/unity-window.md");
            }

            if (GUILayout.Button(new GUIContent("不再提示", "以后打开窗口不再显示这段说明。"), GUILayout.Width(92)))
            {
                _showOnboarding = false;
                EditorPrefs.SetBool(OnboardingDismissedPrefKey, true);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawUnityAssetImportCard()
        {
            var projectRoot = FindProjectRoot();
            var backend = ConfigSheetForgeExcelToSoImporter.Probe();
            var importItems = BuildExcelToSoImportItems(projectRoot);
            var settingsPreflight = InspectExcelToSoSettings(projectRoot, importItems);
            var cacheTypePreflight = ConfigSheetForgeExcelToSoImporter.InspectCacheTypes(importItems, settingsPreflight.TypeRow);
            var syncReady = IsSyncCacheReadyForUnityImport(projectRoot, out var syncReason);
            var cacheReady = importItems.Count > 0 && importItems.All(item => File.Exists(item.CacheXlsxPath));
            var ready = backend.Available && syncReady && cacheReady && settingsPreflight.Ready && cacheTypePreflight.Ready;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("导入 Unity 配表资产", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("把 Source of Truth cache xlsx 导入到 Unity ScriptableObject asset。只写 Unity asset，不写飞书、不改在线表、不改 registry、不写 main。", EditorStyles.wordWrappedLabel);
            DrawReadonlyRow("前置条件", BuildUnityAssetImportPreflightText(backend, syncReady, syncReason, cacheReady, settingsPreflight, cacheTypePreflight), "必须最近一次 sync-cache 成功且 cache 已是最新。");

            if (!backend.Available)
            {
                EditorGUILayout.HelpBox(backend.Message, MessageType.Warning);
            }
            else if (!syncReady)
            {
                EditorGUILayout.HelpBox(syncReason, MessageType.Warning);
            }
            else if (!cacheReady)
            {
                EditorGUILayout.HelpBox("还有 cache xlsx 不存在。请先预览同步计划；如有变化或缺 cache，再确认写入本地 cache。", MessageType.Warning);
            }
            else if (!settingsPreflight.Ready)
            {
                EditorGUILayout.HelpBox(settingsPreflight.Message, settingsPreflight.HasOldExcelReferences ? MessageType.Error : MessageType.Warning);
                if (settingsPreflight.CanUpdateToCache)
                {
                    _confirmExcelToSoSettingsToCache = EditorGUILayout.Toggle(new GUIContent("确认更新 ExcelToSO settings 到 Source of Truth cache", "会写 ProjectSettings/ExcelToScriptableObjectSettings.asset，只把对应表的 excel_name 从旧 Excel/cache 路径改到 .config-sheet-forge/excel-cache。"), _confirmExcelToSoSettingsToCache);
                    if (DrawJobButton(new GUIContent("更新 ExcelToSO settings 到 cache", "单独写 ProjectSettings/ExcelToScriptableObjectSettings.asset；不写飞书、不导入 asset。"), _confirmExcelToSoSettingsToCache, GUILayout.Height(26)))
                    {
                        if (EditorUtility.DisplayDialog("确认更新 ExcelToSO settings", "将把 ExcelToSO settings 中对应配表的 excel_name 改为 Source of Truth cache 路径。\n\n不会写飞书，不会写旧 Excel/，不会导入 asset。", "确认更新", "取消"))
                        {
                            UpdateExcelToSoSettingsToCache(projectRoot, importItems);
                        }
                    }
                }
            }
            else if (!cacheTypePreflight.Ready)
            {
                EditorGUILayout.HelpBox(cacheTypePreflight.Message, MessageType.Error);
            }

            if (_programView || _riskModeUnlocked)
            {
                DrawReadonlyRow("ExcelToSO API", backend.Available ? backend.ApiTypeName : "未找到", "通过反射发现，可选 peer dependency，不会让未安装项目编译失败。");
                DrawReadonlyRow("导入表数量", importItems.Count.ToString(), "来自项目配置 tables 和当前分支表列表。");
            }

            if (DrawJobButton(new GUIContent("导入 Unity 配表资产", "调用 ExcelToSO public API 导入 .config-sheet-forge/excel-cache/*.xlsx 到 ScriptableObject asset。"), ready, GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("确认导入 Unity 配表资产", "将把本地 Source of Truth cache xlsx 导入 Unity ScriptableObject asset。\n\n只写 Unity asset；不会写飞书、不会改在线表、不会改 registry、不会写 main，也不会写旧 Excel/。", "确认导入", "取消"))
                {
                    StartExcelToSoUnityAssetImport(importItems);
                }
            }

            if (ready)
            {
                EditorGUILayout.LabelField("下一步：导入成功后运行 PR 检查，或提交包含 cache 与 asset 的 PR。", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private string BuildUnityAssetImportPreflightText(ExcelToSoImportBackendStatus backend, bool syncReady, string syncReason, bool cacheReady, ExcelToSoSettingsPreflight settingsPreflight, ExcelToSoCacheTypePreflight cacheTypePreflight)
        {
            if (!backend.Available)
            {
                return "缺少 ExcelToSO API";
            }

            if (!syncReady)
            {
                return syncReason;
            }

            if (!cacheReady)
            {
                return "cache xlsx 不完整";
            }

            if (!settingsPreflight.Ready)
            {
                return settingsPreflight.ShortStatus;
            }

            if (!cacheTypePreflight.Ready)
            {
                return cacheTypePreflight.ShortStatus;
            }

            return "已就绪：cache 最新，ExcelToSO settings 指向 Source of Truth cache";
        }

        private bool IsSyncCacheReadyForUnityImport(string projectRoot, out string reason)
        {
            var resultPath = GetUnityLifecyclePath(projectRoot, "sync-cache.result.json");
            if (!File.Exists(resultPath))
            {
                reason = "请先生成同步预览；只有最近一次 sync-cache 成功且 cacheStatus=upToDate 后才能导入 Unity asset。";
                return false;
            }

            var json = File.ReadAllText(resultPath);
            var success = ExtractBoolean(json, "success");
            var cacheStatus = FirstNonEmpty(ExtractString(json, "cacheStatus"), _projectConfig.SyncCacheStatus);
            var blocked = ExtractStringArray(json, "blockedTables");
            var triangulationFailed = ExtractInt(json, "triangulationFailedCount") ?? 0;
            if (success.HasValue && !success.Value)
            {
                reason = "最近一次 sync-cache 没有通过，请先修复同步预检问题。";
                return false;
            }

            if (!string.Equals(cacheStatus, "upToDate", StringComparison.OrdinalIgnoreCase))
            {
                reason = "最近一次同步结论不是“无变化，cache 已是最新”。当前状态：" + HumanizeCacheStatus(cacheStatus);
                return false;
            }

            if (blocked.Count > 0)
            {
                reason = "同步预检仍有阻断表：" + string.Join(", ", blocked);
                return false;
            }

            if (triangulationFailed > 0)
            {
                reason = "三方一致性检查仍有失败表：" + triangulationFailed.ToString() + " 张。";
                return false;
            }

            reason = "最近一次 sync-cache 成功且 cacheStatus=upToDate。";
            return true;
        }

        private List<ExcelToSoImportItem> BuildExcelToSoImportItems(string projectRoot)
        {
            var tables = _projectConfig.CurrentBranchTables != null && _projectConfig.CurrentBranchTables.Count > 0
                ? _projectConfig.CurrentBranchTables
                : _projectConfig.Tables;
            var items = new List<ExcelToSoImportItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables)
            {
                if (table == null || string.IsNullOrWhiteSpace(table.TableId) || !seen.Add(table.TableId))
                {
                    continue;
                }

                items.Add(new ExcelToSoImportItem
                {
                    TableId = table.TableId,
                    DisplayName = table.DisplayName,
                    CacheXlsxPath = ResolveExcelCacheXlsxPath(projectRoot, table),
                    OldExcelPath = ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, table.OldExcelPath),
                    AssetDirectory = table.AssetDirectory,
                    Namespace = table.Namespace
                });
            }

            return items;
        }

        private static string ResolveExcelCacheXlsxPath(string projectRoot, ProjectConfigTableSummary table)
        {
            if (table != null && !string.IsNullOrWhiteSpace(table.CacheXlsxPath))
            {
                return ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, table.CacheXlsxPath);
            }

            return Path.Combine(projectRoot, ".config-sheet-forge", "excel-cache", (table == null ? "" : table.TableId) + ".xlsx");
        }

        private ExcelToSoSettingsPreflight InspectExcelToSoSettings(string projectRoot, List<ExcelToSoImportItem> importItems)
        {
            var settingsPath = Path.Combine(projectRoot, "ProjectSettings", "ExcelToScriptableObjectSettings.asset");
            var preflight = new ExcelToSoSettingsPreflight { SettingsPath = settingsPath };
            if (!File.Exists(settingsPath))
            {
                preflight.ShortStatus = "缺少 ExcelToSO settings";
                preflight.Message = "没有找到 ProjectSettings/ExcelToScriptableObjectSettings.asset。请先在 ExcelToSO 中配置这些表，或由负责人更新 settings 到 Source of Truth cache。";
                return preflight;
            }

            ExcelToSoSettingsDocument document;
            try
            {
                document = JsonUtility.FromJson<ExcelToSoSettingsDocument>(File.ReadAllText(settingsPath));
                preflight.TypeRow = document != null && document.configs != null ? document.configs.type_row : 1;
            }
            catch (Exception ex)
            {
                preflight.ShortStatus = "ExcelToSO settings 无法解析";
                preflight.Message = "ExcelToSO settings 无法解析：" + ex.Message;
                return preflight;
            }

            var entries = FlattenExcelToSoSettings(document);
            var missing = new List<string>();
            var old = new List<string>();
            foreach (var item in importItems)
            {
                var cacheKey = NormalizeProjectPathKey(item.CacheXlsxPath);
                var matchingCache = entries.Any(entry => !string.IsNullOrWhiteSpace(entry.ExcelName) && NormalizeProjectPathKey(ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, entry.ExcelName)) == cacheKey);
                if (matchingCache)
                {
                    continue;
                }

                var oldKey = NormalizeProjectPathKey(item.OldExcelPath);
                var matchingOld = entries.Any(entry =>
                {
                    if (string.IsNullOrWhiteSpace(entry.ExcelName))
                    {
                        return false;
                    }

                    var resolved = NormalizeProjectPathKey(ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, entry.ExcelName));
                    return (!string.IsNullOrWhiteSpace(oldKey) && resolved == oldKey) ||
                           string.Equals(Path.GetFileNameWithoutExtension(entry.ExcelName), item.TableId, StringComparison.OrdinalIgnoreCase);
                });

                if (matchingOld)
                {
                    old.Add(item.TableId);
                }
                else
                {
                    missing.Add(item.TableId);
                }
            }

            if (old.Count == 0 && missing.Count == 0)
            {
                preflight.Ready = true;
                preflight.ShortStatus = "settings 已指向 cache";
                preflight.Message = "ExcelToSO settings 已指向 Source of Truth cache。";
                return preflight;
            }

            preflight.HasOldExcelReferences = old.Count > 0;
            preflight.CanUpdateToCache = old.Count > 0;
            preflight.ShortStatus = old.Count > 0 ? "settings 仍指向旧 Excel" : "settings 缺少 cache 条目";
            var builder = new StringBuilder();
            if (old.Count > 0)
            {
                builder.AppendLine("当前 ExcelToSO 还指向旧 Excel 路径，请先更新到 Source of Truth cache。涉及：" + string.Join(", ", old));
            }

            if (missing.Count > 0)
            {
                builder.AppendLine("ExcelToSO settings 中没有找到这些表的配置：" + string.Join(", ", missing));
            }

            builder.AppendLine("不会直接导旧表，也不会写旧 Excel/。");
            preflight.Message = builder.ToString().TrimEnd();
            return preflight;
        }

        private void UpdateExcelToSoSettingsToCache(string projectRoot, List<ExcelToSoImportItem> importItems)
        {
            var settingsPath = Path.Combine(projectRoot, "ProjectSettings", "ExcelToScriptableObjectSettings.asset");
            if (!File.Exists(settingsPath))
            {
                SetImmediateOutput("无法更新 ExcelToSO settings。", "没有找到 " + settingsPath);
                return;
            }

            ExcelToSoSettingsDocument document;
            try
            {
                document = JsonUtility.FromJson<ExcelToSoSettingsDocument>(File.ReadAllText(settingsPath));
            }
            catch (Exception ex)
            {
                SetImmediateOutput("无法更新 ExcelToSO settings。", ex.Message);
                return;
            }

            var changed = 0;
            foreach (var item in importItems)
            {
                if (TryUpdateExcelToSoEntry(document, projectRoot, item))
                {
                    changed++;
                }
            }

            if (changed <= 0)
            {
                SetImmediateOutput("没有更新 ExcelToSO settings。", "未找到可安全改写的既有表项。请让负责人先在 ExcelToSO settings 中登记这些表。");
                return;
            }

            File.WriteAllText(settingsPath, JsonUtility.ToJson(document, true), Utf8NoBom);
            _confirmExcelToSoSettingsToCache = false;
            SetImmediateOutput(
                "已更新 ExcelToSO settings 到 Source of Truth cache。",
                "更新表数: " + changed.ToString() + Environment.NewLine +
                "写入文件: " + settingsPath + Environment.NewLine +
                "没有写飞书、没有导入 asset、没有写旧 Excel/。");
            RefreshReadonlyStatus(force: true);
        }

        private static bool TryUpdateExcelToSoEntry(ExcelToSoSettingsDocument document, string projectRoot, ExcelToSoImportItem item)
        {
            if (document == null || document.excels == null || item == null)
            {
                return false;
            }

            foreach (var setting in document.excels)
            {
                if (setting == null)
                {
                    continue;
                }

                if (EntryLooksLikeTable(projectRoot, setting.excel_name, item))
                {
                    setting.excel_name = ToProjectRelativePath(projectRoot, item.CacheXlsxPath);
                    if (!string.IsNullOrWhiteSpace(item.AssetDirectory) && string.IsNullOrWhiteSpace(setting.asset_directory))
                    {
                        setting.asset_directory = item.AssetDirectory;
                    }
                    if (!string.IsNullOrWhiteSpace(item.Namespace) && string.IsNullOrWhiteSpace(setting.name_space))
                    {
                        setting.name_space = item.Namespace;
                    }
                    return true;
                }

                if (setting.slaves == null)
                {
                    continue;
                }

                foreach (var slave in setting.slaves)
                {
                    if (slave == null || !EntryLooksLikeTable(projectRoot, slave.excel_name, item))
                    {
                        continue;
                    }

                    slave.excel_name = ToProjectRelativePath(projectRoot, item.CacheXlsxPath);
                    if (!string.IsNullOrWhiteSpace(item.AssetDirectory) && string.IsNullOrWhiteSpace(slave.asset_directory))
                    {
                        slave.asset_directory = item.AssetDirectory;
                    }
                    return true;
                }
            }

            return false;
        }

        private static bool EntryLooksLikeTable(string projectRoot, string excelName, ExcelToSoImportItem item)
        {
            if (string.IsNullOrWhiteSpace(excelName) || item == null)
            {
                return false;
            }

            var entryPath = NormalizeProjectPathKey(ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, excelName));
            var oldPath = NormalizeProjectPathKey(item.OldExcelPath);
            var cachePath = NormalizeProjectPathKey(item.CacheXlsxPath);
            return entryPath == cachePath ||
                   (!string.IsNullOrWhiteSpace(oldPath) && entryPath == oldPath) ||
                   string.Equals(Path.GetFileNameWithoutExtension(excelName), item.TableId, StringComparison.OrdinalIgnoreCase);
        }

        private static List<ExcelToSoSettingsEntry> FlattenExcelToSoSettings(ExcelToSoSettingsDocument document)
        {
            var entries = new List<ExcelToSoSettingsEntry>();
            if (document == null || document.excels == null)
            {
                return entries;
            }

            foreach (var setting in document.excels)
            {
                if (setting == null)
                {
                    continue;
                }

                entries.Add(new ExcelToSoSettingsEntry { ExcelName = setting.excel_name });
                if (setting.slaves == null)
                {
                    continue;
                }

                foreach (var slave in setting.slaves)
                {
                    if (slave != null)
                    {
                        entries.Add(new ExcelToSoSettingsEntry { ExcelName = slave.excel_name });
                    }
                }
            }

            return entries;
        }

        private void StartExcelToSoUnityAssetImport(List<ExcelToSoImportItem> importItems)
        {
            if (IsAnyTaskRunning)
            {
                SetImmediateOutput("已有后台任务正在运行。", "请等待完成或先取消。");
                return;
            }

            _excelToSoImportSession = new ExcelToSoUnityImportSession(importItems);
            _lastCommand = "Unity Editor: ExcelToScriptableObjectApi.ImportExcelPaths(.config-sheet-forge/excel-cache/*.xlsx)";
            _lastResultPath = "";
            _lastLifecycleDir = "";
            _outputScroll = Vector2.zero;
            _resultSummary = _excelToSoImportSession.BuildSummary();
            SetOutputText("已启动 Unity 本地导入。不会写飞书、不会改 registry、不会写旧 Excel/。" + Environment.NewLine);
            SetBottomOutputExpanded(false, persist: false);
            Repaint();
        }

        private static string NormalizeProjectPathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                // Keep the original path when it cannot be resolved.
            }

            return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }

        private static string ToProjectRelativePath(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(path))
            {
                return path ?? "";
            }

            try
            {
                var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var full = Path.GetFullPath(path);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return full.Substring(root.Length).Replace('\\', '/');
                }
            }
            catch
            {
                // Fall back to the input path.
            }

            return path.Replace('\\', '/');
        }

        private string BuildRecommendationText(string projectRoot)
        {
            if (LastPreviewPassed("compare-merge"))
            {
                return GateHasMergeReviewFailure()
                    ? "最近一次合并预览有效；PR gate 缺合并审查记录，下一步点“提交合并审查记录”。"
                    : "最近一次合并预览有效，下一步可以提交合并审查记录或运行 PR 检查。";
            }

            if (LastPreviewPassed("sync-cache") && !CacheLooksFresh(projectRoot))
            {
                return "最近一次同步预览已通过；如果确认要更新本地 cache，请去“配表”页勾选确认后执行写入。";
            }

            var next = BuildNextStepText(projectRoot);
            if (string.Equals(next, "修复同步预检问题", StringComparison.OrdinalIgnoreCase))
            {
                return "同步预检未通过，先修复在线读取或三方一致性问题；本次不会推荐写入本地 cache。";
            }

            if (string.Equals(next, "初始化当前分支在线表", StringComparison.OrdinalIgnoreCase))
            {
                return "当前 Git 分支还没有完整在线工作区；下一步从 main/目标分支初始化当前分支在线表，先生成预览，不写本地文件。";
            }

            if (string.Equals(next, "写入本地 cache", StringComparison.OrdinalIgnoreCase))
            {
                return "最近一次同步预览发现本地 cache 需要更新；确认后才会写入本地 cache。";
            }

            if (string.Equals(next, "预览同步计划", StringComparison.OrdinalIgnoreCase))
            {
                return "当前最稳妥的下一步是先预览同步计划：只读取在线信息，不写飞书、不改本地 cache。";
            }

            if (string.Equals(next, "运行 PR 检查", StringComparison.OrdinalIgnoreCase))
            {
                return "当前 cache 看起来已就绪，下一步跑 PR 检查；失败时会告诉你找谁处理。";
            }

            if (string.Equals(next, "可以提交 PR", StringComparison.OrdinalIgnoreCase))
            {
                return "当前状态看起来可以进入 PR 流程；提交前可再刷新状态或运行 PR 检查。";
            }

            return "先确认项目配置是否已接入；刷新状态不会写任何文件。";
        }

        private DashboardAction BuildPrimaryDashboardAction(string projectRoot)
        {
            if (LastPreviewPassed("compare-merge"))
            {
                if (GateHasMergeReviewFailure() || CanSubmitMergeReview())
                {
                    return new DashboardAction(
                        "提交合并审查记录",
                        "写入 Base MergeReviews；不写 main、不写 cache。",
                        "中风险：会写在线注册中心 MergeReviews，必须确认且最近一次合并预览通过。",
                        () => { _selectedTab = MergeTab; _highlightMergeReview = true; });
                }

                return new DashboardAction(
                    "运行 PR 检查",
                    "合并预览有效后生成 pr-gate-report。",
                    "安全：只生成检查报告，不写飞书、不改本地 cache。",
                    RunPrGateReport);
            }

            if (LastPreviewPassed("sync-cache") && !CacheLooksFresh(projectRoot))
            {
                return new DashboardAction(
                    "去写入本地 cache",
                    "跳到配表页；写入前仍需要勾选确认。",
                    "中风险：会更新本地 cache，必须勾选确认且预览通过。",
                    () => { _selectedTab = TablesTab; _showSyncSection = true; });
            }

            var next = BuildNextStepText(projectRoot);
            if (string.Equals(next, "修复同步预检问题", StringComparison.OrdinalIgnoreCase))
            {
                return new DashboardAction(
                    "查看同步预检问题",
                    "打开最近结果，先处理 blocked tables；同步预检失败时不会写 cache。",
                    "安全：只查看诊断，不写飞书、不改本地 cache。",
                    () =>
                    {
                        _selectedTab = TablesTab;
                        _showSyncSection = true;
                        SetBottomOutputExpanded(true, persist: true);
                        SetImmediateOutput("同步预检未通过，先修复在线读取/三方一致性问题。", BuildSyncCacheStatusText());
                    });
            }

            if (string.Equals(next, "初始化当前分支在线表", StringComparison.OrdinalIgnoreCase))
            {
                return new DashboardAction(
                    "初始化当前分支在线表",
                    "从 main/目标分支为当前 Git 分支创建或复用在线工作区；先生成 dry-run 预览。",
                    "安全：先预览，不写飞书、不改本地 cache、不改 ProjectSettings。",
                    RunCurrentBranchBootstrapPreview);
            }

            if (string.Equals(next, "写入本地 cache", StringComparison.OrdinalIgnoreCase))
            {
                return new DashboardAction(
                    "去写入本地 cache",
                    "跳到配表页；写入前仍需要勾选确认。",
                    "中风险：会更新本地 cache，必须勾选确认且预览通过。",
                    () => { _selectedTab = TablesTab; _showSyncSection = true; });
            }

            if (string.Equals(next, "运行 PR 检查", StringComparison.OrdinalIgnoreCase))
            {
                return new DashboardAction(
                    "运行 PR 检查",
                    "生成最近一次 pr-gate-report，失败会给出中文下一步。",
                    "安全：只生成检查报告，不写飞书、不改本地 cache。",
                    RunPrGateReport);
            }

            if (string.Equals(next, "可以提交 PR", StringComparison.OrdinalIgnoreCase))
            {
                return new DashboardAction(
                    "刷新状态",
                    "重新读取本地配置、git branch、最近 gate report 和 cache 状态。",
                    "安全：只读刷新，不下载、不导出、不写文件。",
                    RefreshStatusAction);
            }

            return new DashboardAction(
                "预览同步计划",
                "读取当前分支在线注册中心并生成 sync-cache dry-run。",
                "安全：只读取，不写飞书、不改本地 cache、不改 ProjectSettings。",
                () => RunSyncCache(apply: false));
        }

        private bool GateHasMergeReviewFailure()
        {
            return _gateReportSummary != null &&
                   _gateReportSummary.Failures.Any(f => f.IsMergeReviewMissing);
        }

        private DashboardAction BuildSecondaryDashboardAction(string projectRoot, string primaryLabel)
        {
            if (!string.Equals(primaryLabel, "运行 PR 检查", StringComparison.OrdinalIgnoreCase))
            {
                return new DashboardAction(
                    "运行 PR 检查",
                    "合 PR 前跑 gate；失败会展示原因和下一步。",
                    "安全：只生成检查报告，不写在线表。",
                    RunPrGateReport);
            }

            return new DashboardAction(
                "预览同步计划",
                "先看当前分支在线表和 cache 是否需要同步。",
                "安全：只读取，不写任何文件。",
                () => RunSyncCache(apply: false));
        }

        private void DrawDashboardActionButton(DashboardAction action, bool primary)
        {
            var width = primary ? 170f : 140f;
            if (DrawJobButton(new GUIContent(action.Label, action.Tooltip), GUILayout.Width(width), GUILayout.Height(32)))
            {
                if (action.Execute != null)
                {
                    action.Execute();
                }
            }
        }

        private void RefreshStatusAction()
        {
            RefreshReadonlyStatus(force: true);
            _lastCommand = "只读刷新状态";
            SetImmediateOutput("已刷新状态。没有下载、导出或写入任何文件。", "");
            StartRegistryStatusProbeIfNeeded(force: true);
        }

        private void DrawWorkflowGuideCards()
        {
            EditorGUILayout.BeginHorizontal();
            DrawWorkflowGuideCard(
                "策划改表",
                "1. 在飞书在线表改数据\n2. 回 Unity 点“预览同步计划”\n3. 通过后勾选确认并“写入本地 cache”\n4. 提交 PR 或找主程合并",
                "预览同步计划",
                "安全：只读取，不写文件。",
                () => RunSyncCache(apply: false));
            DrawWorkflowGuideCard(
                "新建配表",
                "1. 填表 ID 和中文名\n2. 点“预览新建配表”\n3. 找配置负责人确认\n4. 确认后创建在线表并登记",
                "填写新建配表",
                "创建在线表是高风险，必须预览通过并二次确认。",
                () =>
                {
                    _selectedTab = TablesTab;
                    _showNewTableSection = true;
                    _showSyncSection = false;
                });
            DrawWorkflowGuideCard(
                "合并 PR",
                "1. 点“生成合并预览”\n2. 确认冲突、Schema review 和合并审查\n3. 运行 PR 检查\n4. 通过后合 PR",
                "去合并页",
                "写回 main 是高风险，必须负责人确认。",
                () => _selectedTab = MergeTab);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawWorkflowGuideCard(string title, string body, string buttonLabel, string safety, Action action)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(190));
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(body, EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(56));
            if (DrawJobButton(new GUIContent(buttonLabel, safety), GUILayout.Height(24)))
            {
                if (action != null)
                {
                    action();
                }
            }
            EditorGUILayout.LabelField(safety, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawStartTab()
        {
            DrawTabIntro("状态", "看当前分支能不能同步，安全操作从这里开始。");
            DrawSectionTitle("今日操作");
            EditorGUILayout.HelpBox("不知道下一步时先点“预览同步计划”。预览只读取状态和在线注册中心，不写飞书、不改本地文件。", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("刷新状态", "只重新读取 ProjectSettings、git branch、最近 gate report 和本地 cache 文件状态。"), GUILayout.Height(32)))
            {
                RefreshReadonlyStatus(force: true);
                _lastCommand = "只读刷新状态";
                _resultSummary = "已刷新状态。没有下载、导出或写入任何文件。";
                SetOutputText("");
            }

            if (DrawJobButton(new GUIContent("预览同步计划", "等同于同步页 dry-run；不会写飞书，也不会改本地 cache。"), GUILayout.Height(32)))
            {
                RunSyncCache(apply: false);
            }

            if (DrawJobButton(new GUIContent("运行 PR 检查", "生成最近一次 pr-gate-report，按钮旁和 PR 检查页会显示摘要。"), GUILayout.Height(32)))
            {
                RunPrGateReport();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("“预览同步计划”等同于 sync-cache dry-run，只生成计划，不写飞书、不改本地 cache、不改 ProjectSettings。", EditorStyles.wordWrappedMiniLabel);

            DrawCurrentBranchTables(compact: true);
            DrawSyncCacheModeCard();
            DrawAdvancedDiagnostics();
        }

        private void DrawAdvancedDiagnostics()
        {
            _showAdvancedDiagnostics = EditorGUILayout.Foldout(_showAdvancedDiagnostics, "高级诊断", true);
            if (!_showAdvancedDiagnostics)
            {
                return;
            }

            var projectRoot = FindProjectRoot();
            var configPath = ConfigSheetForgeEditorUtility.GetConfigPath(projectRoot);
            var registryPath = ConfigSheetForgeEditorUtility.GetRegistryPath(projectRoot);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawReadonlyRow("Git 分支", FirstNonEmpty(_currentGitBranch, _projectConfig.GitBranch, "未知"), "当前本地 git branch。");
            DrawReadonlyRow("Feishu Profile", FirstNonEmpty(_projectConfig.BranchProfile, "按当前分支推导中"), "当前 Git 分支对应的 Feishu profile。");
            DrawReadonlyRow("Wiki 节点", FirstNonEmpty(_projectConfig.BranchWikiNodeTitle, "按规则推导中"), "当前 branch/profile 对应的 Wiki branch 节点。");
            DrawReadonlyRow("Wiki 链接", FirstNonEmpty(_projectConfig.BranchWikiNodeUrl, _projectConfig.BranchWikiNodeToken, "待读取 BranchBindings"), "当前 branch/profile 对应的 Wiki 节点链接或 token。");
            DrawReadonlyRow("项目根目录", projectRoot, "CLI 会以此目录作为工作目录。");
            DrawReadonlyRow("项目配置", _projectConfig.Exists ? ToProjectRelativePath(_projectConfig.ProjectConfigPath) : "未发现", "ProjectSettings/*ConfigSheetForge*.json。");
            DrawReadonlyRow("schemaVersion", FirstNonEmpty(_projectConfig.SchemaVersion, "未声明"), "项目 config 中声明的 schema 版本。");
            DrawReadonlyRow("共享表数量", _projectConfig.TableCount > 0 ? _projectConfig.TableCount.ToString() : "未声明", "ProjectSettings 顶层 tables/configSheets 数组中的配表数量。");
            DrawReadonlyRow("lifecycle", FirstNonEmpty(_projectConfig.LifecycleApplyMode, "未声明"), "项目 config 中声明的 lifecycle 写入模式。");
            DrawReadonlyRow("Gate 报告", FirstNonEmpty(_projectConfig.GateReportPath, "Temp/ConfigSheetForge/pr-gate-report.json"), "PR gate report 输出路径。");
            DrawReadonlyRow("Adapter", FirstNonEmpty(_projectConfig.AdapterDescription, "未配置"), "项目 adapter 负责把项目 config 转成 lifecycle contract。");
            _cliPath = EditorGUILayout.TextField(new GUIContent("CLI", "CLI 可执行文件或绝对路径；默认会先看项目配置声明的环境变量。"), _cliPath);
            _cliInvocation = ConfigSheetForgeEditorUtility.ResolveCoreCli(_projectConfig, projectRoot, _cliPath);
            DrawReadonlyRow("CLI 来源", _cliInvocation.CanLaunch ? _cliInvocation.SourceDescription : "未找到：" + _cliInvocation.SourceDescription, "CLI 来自环境变量、源码 checkout、PATH 或窗口 CLI 字段。");
            var larkCli = ConfigSheetForgeEditorUtility.ResolveLarkCliForDisplay(_projectConfig);
            DrawReadonlyRow("lark-cli", larkCli.Found ? larkCli.DisplayPath : "未找到", "用于读取飞书 Base / Sheet。Unity 子进程会补齐 npm global PATH。来源：" + larkCli.Source);
            DrawReadonlyRow("lark-cli 环境变量", FirstNonEmpty(_projectConfig.LarkCliEnvironmentVariable, "CONFIG_SHEET_FORGE_LARK_CLI"), "可设置该变量或 toolkit.larkCliPath 指向 lark-cli。");
            DrawWrappedReadonlyBlock("Unity 子进程 PATH", ConfigSheetForgeEditorUtility.DescribeUnityPathForDiagnostics(), "已经补齐 npm global bin 的 PATH；用于 adapter、apply-contract 和 lark-cli。");
            if (_projectConfig.AllowUserFallback)
            {
                EditorGUILayout.HelpBox(_projectConfig.AllowUserFallbackForHardGate
                    ? "项目允许 user fallback，并声明可用于 hard gate。请确认这是项目治理策略。"
                    : "项目允许 user fallback，但默认不应把 user 身份结果当作 CI hard gate 通过依据。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("飞书身份策略：strict bot，PR hard gate / sync apply / seed apply 不会静默切换到 user。", EditorStyles.wordWrappedMiniLabel);
            }
            DrawReadonlyRow("目标分支默认值", FirstNonEmpty(_projectConfig.DefaultTargetBranch, "main"), "合并页默认 target branch。");
            DrawReadonlyRow("GitHub repo", FirstNonEmpty(_projectConfig.GithubRepository, _githubRepository, "未声明/待从 remote 推导"), "用于 PR 自动识别。");
            DrawReadonlyRow("PR 自动识别", _projectConfig.AllowPrAutoDetect ? "启用" : "关闭", "allowPrAutoDetect。");
            DrawReadonlyRow("本地状态目录", BuildLocalStateText(configPath, registryPath), ".config-sheet-forge 是 gitignored 本地状态/cache；可忽略，可重建，不参与共享项目摘要。");
            if (_projectConfig.Diagnostics.Count > 0)
            {
                foreach (var diagnostic in _projectConfig.Diagnostics)
                {
                    EditorGUILayout.HelpBox(diagnostic, MessageType.Warning);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("doctor --details", "运行 doctor --details，确认 CLI、lark-cli、权限和本地配置状态。"), GUILayout.Height(24)))
            {
                RunCli("doctor", "--details");
            }

            if (GUILayout.Button(new GUIContent("Core Smoke", "通过 Unity 加载的 shared core 计算 semantic hash。"), GUILayout.Height(24)))
            {
                var report = SchemaReviewer.Review(CreateSmokeWorkbook());
                _lastCommand = "Shared core smoke check";
                SetImmediateOutput(
                    "Shared core smoke 已完成。" + Environment.NewLine + "Findings: " + report.Findings.Count,
                    "Semantic hash: " + SemanticHasher.ComputeHash(CreateSmokeWorkbook()));
            }

            if (GUILayout.Button(new GUIContent("打开缓存目录", "在文件浏览器中显示 .config-sheet-forge/cache。"), GUILayout.Height(24)))
            {
                RevealPath(Path.Combine(FindProjectRoot(), ".config-sheet-forge", "cache"));
            }
            EditorGUILayout.EndHorizontal();

            _rootQuery = EditorGUILayout.TextField(new GUIContent("Root 搜索", "飞书/Lark 文档标题的一部分。"), _rootQuery);
            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("查找候选根文档", "只列出候选根文档，不会自动选择。"), GUILayout.Height(24)))
            {
                RunCli("discover-root", "--query", _rootQuery);
            }

            if (GUILayout.Button(new GUIContent("复制 discover-root", "复制 discover-root 命令。"), GUILayout.Height(24)))
            {
                CopyCommand("discover-root", "--query", _rootQuery);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("复制 sync dry-run", "复制同步预览命令。"), GUILayout.Height(24)))
            {
                CopySyncCacheCommand(apply: false);
            }

            if (GUILayout.Button(new GUIContent("复制 sync apply", "复制同步写 cache 命令。"), GUILayout.Height(24)))
            {
                CopySyncCacheCommand(apply: true);
            }

            if (GUILayout.Button(new GUIContent("复制 PR gate", "复制 PR gate report 命令。"), GUILayout.Height(24)))
            {
                CopyPrGateCommand();
            }
            EditorGUILayout.EndHorizontal();

            if (_projectConfig.HasLifecycleAdapter)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("复制 new-table adapter", "复制新建配表 adapter 命令。"), GUILayout.Height(24)))
                {
                    CopyProjectLifecycleAdapterCommand("new-table", dryRun: true);
                }

                if (GUILayout.Button(new GUIContent("复制 seed adapter", "复制 seed dry-run adapter 命令。"), GUILayout.Height(24)))
                {
                    CopyProjectLifecycleAdapterCommand("seed-from-local-xlsx", dryRun: true);
                }

                if (GUILayout.Button(new GUIContent("复制 merge adapter", "复制三方合并 adapter 命令。"), GUILayout.Height(24)))
                {
                    CopyProjectLifecycleAdapterCommand("compare-merge", dryRun: true);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTablesTab()
        {
            DrawTabIntro("配表", "同步已有在线表，或申请新建配表。");
            EditorGUILayout.HelpBox("低风险操作可预览；创建在线表、改 schema、写回 main 等危险动作必须通过项目 contract 和确认流程。", MessageType.None);

            if (_projectConfig.Exists)
            {
                if (ShouldShowCurrentBranchBootstrapCard())
                {
                    DrawCurrentBranchBootstrapCard();
                }

                _showSyncSection = EditorGUILayout.Foldout(_showSyncSection, "同步当前分支 cache", true);
                if (_showSyncSection)
                {
                    DrawCurrentBranchTables(compact: false);
                    DrawSyncCacheModeCard();
                    DrawUnityAssetImportCard();
                }

                _showNewTableSection = EditorGUILayout.Foldout(_showNewTableSection, "新建配表", true);
                if (_showNewTableSection)
                {
                    DrawProjectNewTableInputs();
                    DrawProjectNewTableActions();
                }

                _showSeedSection = EditorGUILayout.Foldout(_showSeedSection, "本地 Excel Seed（高风险迁移，默认不执行）", true);
                if (_showSeedSection)
                {
                    DrawProjectSeedCard();
                }
                else
                {
                    EditorGUILayout.HelpBox("Seed 只在展开后手动预览或确认执行；打开窗口和切换页面都不会自动迁移、下载或写项目文件。", MessageType.Info);
                }

                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _tableId = EditorGUILayout.TextField(new GUIContent("配表ID", "稳定机器 key，例如 SkillsData。"), _tableId);
            _tableName = EditorGUILayout.TextField(new GUIContent("显示名称", "报告和窗口里给策划看的名称。"), _tableName);
            _spreadsheet = EditorGUILayout.TextField(new GUIContent("在线表", "飞书/Lark Sheet URL 或 token。真实私有链接不要提交。"), _spreadsheet);
            _sheetId = EditorGUILayout.TextField(new GUIContent("工作表ID", "provider 工作表 ID。"), _sheetId);
            _range = EditorGUILayout.TextField(new GUIContent("读取范围", "A1 范围，例如 A1:Z500。"), _range);

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("登记本地表", "添加或更新本地 registry.json。项目 Source of Truth 流程请优先使用 contract。"), GUILayout.Height(28)))
            {
                RunCli("new-table", "--id", _tableId, "--name", _tableName, "--spreadsheet", _spreadsheet, "--sheet-id", _sheetId, "--range", _range);
            }

            if (DrawJobButton(new GUIContent("同步单表", "导出/读取此表并计算 semantic hash。"), GUILayout.Height(28)))
            {
                RunCli("sync", "--table", _tableId);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("同步全部", "同步本地 registry 里的全部表。"), GUILayout.Height(24)))
            {
                RunCli("sync");
            }

            if (GUILayout.Button(new GUIContent("复制登记命令", "复制当前表单对应的 new-table 命令。"), GUILayout.Height(24)))
            {
                CopyCommand("new-table", "--id", _tableId, "--name", _tableName, "--spreadsheet", _spreadsheet, "--sheet-id", _sheetId, "--range", _range);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawMergeTab()
        {
            DrawTabIntro("合并", "处理当前分支到 main 的配表合并。");
            EditorGUILayout.HelpBox("合并审查按当前 Git 分支和目标分支自动推导，像 GitHub PR 一样先生成预览；写回 main 必须显式确认。", MessageType.None);

            DrawProjectMergeInputs();
            if (_projectConfig.Exists)
            {
                DrawTargetBranchBootstrapCard();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("执行", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("低风险 merge 只生成预览，不写回 main；adapter 会根据 source/target/merge-base 自动准备 base/ours/theirs semantic 输入。", EditorStyles.wordWrappedLabel);
                var mergeWriteReady = _writeBackToMain && _confirmWriteMain && LastPreviewPassed("compare-merge");
                if (_writeBackToMain && _confirmWriteMain && !LastPreviewPassed("compare-merge"))
                {
                    EditorGUILayout.HelpBox("请先生成合并预览，并确认预览成功后再写回 main。", MessageType.Warning);
                }
                EditorGUILayout.BeginHorizontal();
                if (DrawJobButton(new GUIContent("生成合并预览", "生成 compare-merge dry-run，不写回 main。"), GUILayout.Height(28)))
                {
                    RunProjectLifecycle("compare-merge", dryRun: true);
                }

                var reviewReady = CanSubmitMergeReview();
                if (DrawJobButton(new GUIContent("提交合并审查记录", "写入 Base MergeReviews；不写 main、不写本地 cache、不改 ProjectSettings。"), reviewReady, GUILayout.Height(28)))
                {
                    if (EditorUtility.DisplayDialog(
                        "提交合并审查记录",
                        "这一步会写入 Base 的 MergeReviews，表示负责人已经审过最近一次合并预览。\n\n不会写回 main。\n不会写本地 cache。\n不会改 ProjectSettings。\n不会改 ExcelToSO。\n\n如果合并预览不是刚刚这次输入生成的，CLI 会阻断写入。",
                        "提交审查记录",
                        "取消"))
                    {
                        RunSubmitMergeReview();
                    }
                }

                if (DrawJobButton(new GUIContent("确认写回 main", "危险操作：必须勾选申请写回、确认写回，并且最近一次合并预览成功。"), mergeWriteReady, GUILayout.Height(28)))
                {
                    if (EditorUtility.DisplayDialog("确认写回 main", "将按项目 adapter 的 compare-merge contract 执行写回。请确认预览已经通过。", "确认执行", "取消"))
                    {
                        RunProjectLifecycle("compare-merge", dryRun: false);
                    }
                }
                EditorGUILayout.EndHorizontal();
                DrawMergeReviewSubmitCard(reviewReady);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox("未发现 ProjectSettings/*ConfigSheetForge*.json，无法按分支工作区规则自动定位在线表。请先接入项目 adapter；下面保留本地 CLI 兼容入口给工程诊断使用。", MessageType.Warning);
            DrawPathField("基线", ref _basePath, "共同祖先 semantic workbook JSON。");
            DrawPathField("本分支", ref _oursPath, "本地 semantic workbook JSON。");
            DrawPathField("对方", ref _theirsPath, "待合入 semantic workbook JSON。");
            _mergeReportPath = EditorGUILayout.TextField(new GUIContent("报告", "Markdown 报告路径。"), _mergeReportPath);
            _mergedPath = EditorGUILayout.TextField(new GUIContent("合并结果", "合并后的 semantic workbook JSON 路径。"), _mergedPath);

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("生成合并预览", "生成三方合并报告和合并预览。"), GUILayout.Height(28)))
            {
                RunCli("merge", "--base", _basePath, "--ours", _oursPath, "--theirs", _theirsPath, "--out", _mergeReportPath, "--merged", _mergedPath);
            }

            if (GUILayout.Button(new GUIContent("复制命令", "复制 merge 命令。"), GUILayout.Width(116), GUILayout.Height(28)))
            {
                CopyCommand("merge", "--base", _basePath, "--ours", _oursPath, "--theirs", _theirsPath, "--out", _mergeReportPath, "--merged", _mergedPath);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawGateTab()
        {
            DrawTabIntro("PR 检查", "合 PR 前跑 gate，失败会告诉你找谁处理。");
            EditorGUILayout.HelpBox("Gate 是提交前硬检查。失败原因会尽量用策划能看懂的中文说明下一步。", MessageType.None);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (_projectConfig.Exists && DrawJobButton(new GUIContent("生成 PR gate report", "通过项目 adapter 生成 pr-gate-report contract，再由 core 输出 gate report。"), GUILayout.Height(28)))
            {
                RunPrGateReport();
            }
            else if (!_projectConfig.Exists && DrawJobButton(new GUIContent("运行 Gate", "检查 semantic cache 和同步报告。"), GUILayout.Height(28)))
            {
                RunCli("gate", "--details");
            }

            EditorGUILayout.LabelField(BuildGateStatusText(), EditorStyles.wordWrappedMiniLabel, GUILayout.MinWidth(220));
            EditorGUILayout.EndHorizontal();
            DrawGateReportCards();
            DrawGateActionPanel();
            EditorGUILayout.EndVertical();
        }

        private void DrawGateActionPanel()
        {
            if (_gateReportSummary == null || !_gateReportSummary.HasReport || GateLooksPassed())
            {
                return;
            }

            GUILayout.Space(6);
            EditorGUILayout.LabelField("可执行处理", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("处理 Schema 审查", "写入 Base SchemaReviews，不写 main 或 cache。"), GUILayout.Height(26)))
            {
                _showSchemaReviewEntry = !_showSchemaReviewEntry;
            }

            if (GUILayout.Button(new GUIContent("申请/批准 waiver", "写入 Base Waivers，必须填写原因和过期时间。"), GUILayout.Height(26)))
            {
                _showWaiverEntry = !_showWaiverEntry;
            }
            EditorGUILayout.EndHorizontal();

            if (_showSchemaReviewEntry)
            {
                DrawSchemaReviewEntry();
            }

            if (_showWaiverEntry)
            {
                DrawWaiverEntry();
            }
        }

        private void DrawSchemaReviewEntry()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("提交 Schema 审查结果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("用于 schema 变化已经由负责人确认的情况。只写 Base SchemaReviews，不写 main、不写本地 cache。", EditorStyles.wordWrappedMiniLabel);
            _schemaReviewTableId = EditorGUILayout.TextField(new GUIContent("配表ID", "要批准 schema 变化的配表 ID。"), _schemaReviewTableId);
            _schemaReviewComment = EditorGUILayout.TextField(new GUIContent("审查备注", "可选。说明本次 schema 审查结论。"), _schemaReviewComment);
            var ready = !string.IsNullOrWhiteSpace(_schemaReviewTableId);
            if (!ready)
            {
                EditorGUILayout.HelpBox("请先填写配表ID。", MessageType.Info);
            }

            if (DrawJobButton(new GUIContent("批准 Schema 审查", "写入 SchemaReviews 状态 approved。"), ready, GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog("批准 Schema 审查", "将写入 Base SchemaReviews，状态为 approved。\n\n不会写 main。\n不会写本地 cache。\n不会改 ProjectSettings 或 ExcelToSO。", "确认写入", "取消"))
                {
                    RunSimpleReviewLifecycle("approve-schema-review");
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawWaiverEntry()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("申请/批准临时放行", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("waiver 必须有原因和过期时间，只能由配置负责人批准；默认 strict bot，不会用 user 身份绕过 gate。", EditorStyles.wordWrappedMiniLabel);
            _waiverTableId = EditorGUILayout.TextField(new GUIContent("适用 TableId", "项目级放行用 __project_pr_gate__，单表放行填具体 TableId。"), FirstNonEmpty(_waiverTableId, "__project_pr_gate__"));
            _waiverReason = EditorGUILayout.TextField(new GUIContent("原因", "必填。说明为什么需要临时放行。"), _waiverReason);
            _waiverExpiresAt = EditorGUILayout.TextField(new GUIContent("过期时间", "必填。建议使用 ISO 时间，例如 2026-05-27T18:00:00+08:00。"), _waiverExpiresAt);
            var ready = !string.IsNullOrWhiteSpace(_waiverReason) && !string.IsNullOrWhiteSpace(_waiverExpiresAt);
            if (!ready)
            {
                EditorGUILayout.HelpBox("请填写原因和过期时间。", MessageType.Info);
            }

            if (DrawJobButton(new GUIContent("配置负责人批准 waiver", "写入 Waivers；过期后 gate 会重新阻断。"), ready, GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog("批准 waiver", "将写入 Base Waivers，批准角色为 configOwner。\n\n不会写 main。\n不会写本地 cache。\n不会改 ProjectSettings 或 ExcelToSO。", "确认写入", "取消"))
                {
                    RunSimpleReviewLifecycle("approve-waiver");
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawGateReportCards()
        {
            if (_gateReportSummary == null || !_gateReportSummary.HasReport)
            {
                EditorGUILayout.HelpBox("还没有生成 PR 检查报告。点击“生成 PR gate report”后，这里会显示是否可以合并。", MessageType.Info);
                return;
            }

            if (_gateReportSummary.Waived && _gateReportSummary.Failures.Count == 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("已由配置负责人 waiver 临时放行", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_gateReportSummary.WaiverSummary, EditorStyles.wordWrappedLabel);
                if (_gateReportSummary.WaivedFailures.Count > 0)
                {
                    EditorGUILayout.LabelField("本次临时放行覆盖的问题：" + string.Join("；", _gateReportSummary.WaivedFailures), EditorStyles.wordWrappedMiniLabel);
                }
                EditorGUILayout.EndVertical();
                return;
            }

            if (GateLooksPassed())
            {
                EditorGUILayout.HelpBox("PR 检查通过：当前 cache、合并审查和权限检查看起来可以合并。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("PR 还不能合并", EditorStyles.boldLabel);
            var failures = _gateReportSummary.Failures;
            if (failures.Count == 0)
            {
                EditorGUILayout.LabelField("原因：报告没有给出明确失败原因。", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("下一步：重新运行 PR 检查；如果仍失败，请展开输出页查看详细日志。", EditorStyles.wordWrappedLabel);
            }
            else
            {
                for (var i = 0; i < failures.Count; i++)
                {
                    DrawGateFailureCard(failures[i]);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawGateFailureCard(GateFailureView failure)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("原因：" + failure.Reason, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("下一步：" + failure.NextStep, EditorStyles.wordWrappedLabel);
            if (failure.IsMergeReviewMissing)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("去合并页提交审查记录", "先确认最近一次合并预览，再写入 Base MergeReviews。"), GUILayout.Height(26)))
                {
                    _selectedTab = MergeTab;
                    _highlightMergeReview = true;
                }

                if (GUILayout.Button(new GUIContent("生成合并预览", "只读预览，不写 main、不写 cache。"), GUILayout.Width(116), GUILayout.Height(26)))
                {
                    _selectedTab = MergeTab;
                    RunProjectLifecycle("compare-merge", dryRun: true);
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (failure.IsSchemaReviewMissing)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("处理 Schema 审查", "查看/提交 SchemaReviews 审查结果。"), GUILayout.Height(26)))
                {
                    _selectedTab = GateTab;
                    _showSchemaReviewEntry = true;
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (failure.IsWaiverProblem)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("申请/更新 waiver", "waiver 必须填写原因和过期时间，并由配置负责人批准。"), GUILayout.Height(26)))
                {
                    _selectedTab = GateTab;
                    _showWaiverEntry = true;
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (failure.IsRegistryMigrationNeeded)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("复制状态选项窄迁移命令", "只检查/补齐 MergeReviews、SchemaReviews、Waivers 的状态选项；确认后再把 --dry-run 改为 --yes。"), GUILayout.Height(26)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildRegistryMigrateDryRunCommand();
                    SetImmediateOutput("已复制 review-status-options 窄迁移 dry-run 命令。", "这条命令只检查/补齐 MergeReviews、SchemaReviews、Waivers 的状态选项；确认后把 --dry-run 改成 --yes。");
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawCurrentBranchTables(bool compact)
        {
            DrawSectionTitle(compact ? "当前分支表" : "当前 Branch 表列表");
            var tables = _projectConfig.CurrentBranchTables;
            if (tables == null || tables.Count == 0)
            {
                EditorGUILayout.HelpBox(BuildNoTablesReason(), MessageType.Warning);
                return;
            }

            var limit = compact ? Math.Min(5, tables.Count) : tables.Count;
            for (var i = 0; i < limit; i++)
            {
                DrawCurrentBranchTableRow(tables[i], compact);
            }

            if (compact && tables.Count > limit)
            {
                EditorGUILayout.LabelField("还有 " + (tables.Count - limit).ToString() + " 张表，可到“配表”页查看完整列表。", EditorStyles.miniLabel);
            }
        }

        private void DrawCurrentBranchTableRow(ProjectConfigTableSummary table, bool compact)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(FirstNonEmpty(table.TableId, "未命名表"), EditorStyles.boldLabel, GUILayout.Width(160));
            EditorGUILayout.LabelField(FirstNonEmpty(table.DisplayName, "未命名"), EditorStyles.miniLabel, GUILayout.MinWidth(120));
            if (!string.IsNullOrWhiteSpace(table.OnlineSheetUrl) && GUILayout.Button(new GUIContent("打开 Sheet", "打开在线 Sheet。"), GUILayout.Width(88)))
            {
                Application.OpenURL(table.OnlineSheetUrl);
            }
            EditorGUILayout.EndHorizontal();

            DrawReadonlyRow("cache", BuildTableCacheStatus(table), "本地 cache 文件状态。");
            DrawReadonlyRow("semantic", FirstNonEmpty(ShortHash(table.SemanticHash), "未记录"), "在线注册中心或 ProjectSettings 中记录的 semantic hash。");
            if (!compact)
            {
                DrawReadonlyRow("更新时间", FirstNonEmpty(table.UpdatedAt, "未记录"), "在线注册中心或 ProjectSettings 中记录的更新时间。");
                DrawReadonlyRow("Schema", FirstNonEmpty(table.SchemaStatus, "未知"), "schema 是否变化或是否需要审查。");
                DrawReadonlyRow("负责人", FirstNonEmpty(table.OwnerRole, "未声明"), "在线表负责人角色。");
                DrawReadonlyRow("Sheet 定位", BuildSheetLocationText(table), "在线 Sheet token、sheet id、wiki node。");
                if (!string.IsNullOrWhiteSpace(table.BlockingReason))
                {
                    EditorGUILayout.HelpBox(table.BlockingReason, MessageType.Warning);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSyncCacheModeCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("同步当前分支 cache", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("预览同步计划会从在线注册中心定位当前分支 16 张表，读取/导出到 Temp 并做三方检查；不会写飞书、不改正式 cache、不改 ProjectSettings。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("写入本地 cache 只有在预览通过、勾选确认并通过弹窗后才会执行；它会读取在线 Sheet、导出 xlsx、完成三方一致性检查和 hash gate 后更新本地 cache。", EditorStyles.wordWrappedLabel);
            DrawReadonlyRow("最近同步结论", BuildSyncCacheStatusText(), "来自最近一次 sync-cache dry-run/apply result。");
            _confirmSyncApply = EditorGUILayout.Toggle(new GUIContent("确认写入本地 cache", "允许更新 .config-sheet-forge/cache 和 excel-cache；必须先预览通过。"), _confirmSyncApply);
            var syncApplyReady = _confirmSyncApply && LastPreviewPassed("sync-cache") && SyncPreviewRequiresCacheWrite();
            if (string.Equals(_projectConfig.SyncCacheStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                EditorGUILayout.HelpBox("同步预检未通过，先修复在线读取/三方一致性问题；本次不会允许写入本地 cache。阻断表：" + string.Join(", ", _projectConfig.SyncCacheBlockedTables), MessageType.Error);
            }
            else if (_confirmSyncApply && !LastPreviewPassed("sync-cache"))
            {
                EditorGUILayout.HelpBox("请先预览同步计划，并确认预览成功后再写入本地 cache。", MessageType.Warning);
            }
            else if (_confirmSyncApply && LastPreviewPassed("sync-cache") && !SyncPreviewRequiresCacheWrite())
            {
                EditorGUILayout.HelpBox("最近一次同步预览显示 cache 已是最新，无需写入本地 cache。", MessageType.Info);
            }
            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("预览同步计划", "读取在线注册中心并生成同步计划，不改本地 cache。"), GUILayout.Height(28)))
            {
                RunSyncCache(apply: false);
            }

            if (DrawJobButton(new GUIContent("写入本地 cache", "危险操作：会更新本地 cache；需要确认且最近一次同步预览成功。"), syncApplyReady, GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("确认同步 cache", "将读取当前 branch/profile 的在线 Sheet，导出 xlsx，三方一致后更新本地 cache。无变化时会显示“无变化，未重写 cache”。", "确认执行", "取消"))
                {
                    RunSyncCache(apply: true);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private bool ShouldShowCurrentBranchBootstrapCard()
        {
            return string.Equals(BuildNextStepText(FindProjectRoot()), "初始化当前分支在线表", StringComparison.OrdinalIgnoreCase) ||
                   LastPreviewPassed("bootstrap-current-branch-from-target") ||
                   LastPreviewPassed("branch-workspace-bootstrap-from-target");
        }

        private void DrawCurrentBranchBootstrapCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("从目标分支派生当前分支在线表", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("用于新 git 分支第一次建立飞书工作区：从 main/PR base 复制在线 Source of Truth，创建当前分支 Wiki 节点、在线 Sheet、BranchBindings、ConfigSheets 和 SchemaReviews。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox("默认不会写本地 cache，不改 ProjectSettings，不改 ExcelToSO，也不会碰历史 OneDrive Excel。先 dry-run，apply 必须同输入预览通过并勾选三项确认。", MessageType.Info);
            _confirmCurrentBranchCreateOnlineSheets = EditorGUILayout.Toggle(new GUIContent("确认创建/复用当前分支在线 Sheet", "会在当前分支工作区下创建或复用在线表副本。"), _confirmCurrentBranchCreateOnlineSheets);
            _confirmCurrentBranchRegistryUpsert = EditorGUILayout.Toggle(new GUIContent("确认写 BranchBindings / ConfigSheets", "会写入 Feishu Base 注册中心，用于后续同步和 PR gate。"), _confirmCurrentBranchRegistryUpsert);
            _confirmCurrentBranchSchemaReviews = EditorGUILayout.Toggle(new GUIContent("确认登记 SchemaReviews baseline", "会写入 SchemaReviews baseline，便于 PR gate 识别后续 schema 变化。"), _confirmCurrentBranchSchemaReviews);
            var applyReady = LastPreviewPassed("bootstrap-current-branch-from-target") &&
                             _confirmCurrentBranchCreateOnlineSheets &&
                             _confirmCurrentBranchRegistryUpsert &&
                             _confirmCurrentBranchSchemaReviews &&
                             !string.IsNullOrWhiteSpace(_lastResultPath) &&
                             File.Exists(_lastResultPath);
            if (!LastPreviewPassed("bootstrap-current-branch-from-target"))
            {
                EditorGUILayout.HelpBox("请先生成“从目标分支派生当前分支”的 dry-run 预览。", MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("预览派生当前分支", "只生成从 main/目标分支派生当前分支在线表的计划，不写飞书、不改本地。"), GUILayout.Height(28)))
            {
                RunCurrentBranchBootstrapPreview();
            }

            if (DrawJobButton(new GUIContent("执行派生当前分支", "会写飞书当前分支工作区、在线 Sheet 和注册中心；不写本地 cache/ProjectSettings/ExcelToSO。"), applyReady, GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("确认派生当前分支", "将从目标分支复制在线 Source of Truth，写入当前分支飞书工作区、在线 Sheet、BranchBindings、ConfigSheets 和 SchemaReviews。\n\n不会写本地 cache，不改 ProjectSettings，不改 ExcelToSO，不碰历史 Excel。", "确认执行", "取消"))
                {
                    RunCurrentBranchBootstrapApply();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawProjectNewTableInputs()
        {
            EnsureNewTableDefaults();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("新表输入", EditorStyles.boldLabel);
            _tableId = EditorGUILayout.TextField(new GUIContent("配表ID", "代码和文件使用的英文 ID，例如 SkillExtraData。只能用英文、数字、下划线，不能有空格。"), _tableId);
            if (string.IsNullOrWhiteSpace(_tableId))
            {
                EditorGUILayout.LabelField("例：SkillExtraData、MonsterData。不要使用中文名或空格。", EditorStyles.wordWrappedMiniLabel);
            }

            _tableName = EditorGUILayout.TextField(new GUIContent("显示名称", "给策划看的中文名，例如 技能扩展数据。"), _tableName);
            if (string.IsNullOrWhiteSpace(_tableName))
            {
                EditorGUILayout.LabelField("例：技能扩展数据、怪物表。", EditorStyles.wordWrappedMiniLabel);
            }

            DrawOwnerRolePicker();
            DrawApprovalRulesCard();

            _schemaChangeSummary = EditorGUILayout.TextField(new GUIContent("Schema 变更说明", "说明为什么要新增或修改字段。只新增新表时可以写：新增 XXX 配表。"), _schemaChangeSummary);
            if (string.IsNullOrWhiteSpace(_schemaChangeSummary))
            {
                EditorGUILayout.LabelField("例：新增技能扩展数据配表。", EditorStyles.wordWrappedMiniLabel);
            }

            _sheetName = EditorGUILayout.TextField(new GUIContent("工作表名", "普通用户可以不管。留空时默认使用显示名称，显示名称为空时使用配表ID。"), _sheetName);
            DrawReadonlyRow("本地 Excel cache", EffectiveNewTableExcelPath(), "普通新表自动生成到本地 cache。旧 Excel 迁移请使用“本地 Excel Seed”。");
            if (_riskModeUnlocked)
            {
                _excelPath = EditorGUILayout.TextField(new GUIContent("高级：覆盖本地 Excel cache 路径", "普通用户不需要改。仅在项目 cache 路径规则异常时覆盖。"), _excelPath);
            }
            else if (_programView)
            {
                EditorGUILayout.LabelField("高级未开启：本地 Excel cache 路径按项目规则自动推导。", EditorStyles.wordWrappedMiniLabel);
            }

            DrawStructuredFieldEditor();
            if (_riskModeUnlocked)
            {
                _showFieldTemplateEditor = EditorGUILayout.Foldout(_showFieldTemplateEditor, "高级：编辑原始模板文本", true);
                if (_showFieldTemplateEditor)
                {
                    EditorGUILayout.HelpBox("风险配置：raw 模板文本容易写错字段类型或覆盖结构化编辑结果。普通用户请继续使用上面的字段表格。", MessageType.Warning);
                    EditorGUILayout.LabelField("每行一个字段：字段 key | 显示名 | 类型 | 说明。枚举类型写 enum:a,b,c。修改后会解析回上面的结构化字段。", EditorStyles.wordWrappedMiniLabel);
                    var previous = _fieldsText;
                    _fieldsText = EditorGUILayout.TextArea(_fieldsText, GUILayout.MinHeight(72));
                    if (!string.Equals(previous, _fieldsText, StringComparison.Ordinal))
                    {
                        ReplaceFieldRows(ParseFieldsText(_fieldsText));
                    }
                }
            }
            else if (_programView)
            {
                EditorGUILayout.LabelField("raw 模板文本在“高级”开启后才显示；日常新建配表请使用结构化字段表格。", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawOwnerRolePicker()
        {
            if (_projectConfig.Roles.Count == 0)
            {
                EditorGUILayout.HelpBox("项目未配置角色列表，请联系主程补 roles 配置。这里暂时保留文本输入。", MessageType.Warning);
                _ownerRole = EditorGUILayout.TextField(new GUIContent("这张表由谁负责", "项目未声明 roles 时，暂时输入内部角色标识。"), _ownerRole);
                return;
            }

            EnsureOwnerRoleDefault();
            var selected = 0;
            var labels = new string[_projectConfig.Roles.Count];
            for (var i = 0; i < _projectConfig.Roles.Count; i++)
            {
                var role = _projectConfig.Roles[i];
                labels[i] = FirstNonEmpty(role.DisplayName, role.Key) + (_programView ? " (" + role.Key + ")" : "");
                if (string.Equals(role.Key, _ownerRole, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                }
            }

            var next = EditorGUILayout.Popup(new GUIContent("这张表由谁负责", "选择这张表的日常维护负责人。审批规则由项目配置决定。"), selected, labels);
            if (next >= 0 && next < _projectConfig.Roles.Count)
            {
                _ownerRole = _projectConfig.Roles[next].Key;
            }
        }

        private void DrawApprovalRulesCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("审批规则（只读）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("这张表的日常负责人可以选择；Schema 审查、临时放行和写回 main 仍由项目规则决定。", EditorStyles.wordWrappedMiniLabel);
            DrawReadonlyRow("Schema 审查", BuildRoleListText("schema"), "负责字段结构变化审查。");
            DrawReadonlyRow("临时放行", BuildRoleListText("waiver"), "可批准临时放行。");
            DrawReadonlyRow("写回 main", BuildRoleListText("main"), "可批准写回主线工作区。");
            EditorGUILayout.EndVertical();
        }

        private string BuildRoleListText(string kind)
        {
            var values = new List<string>();
            for (var i = 0; i < _projectConfig.Roles.Count; i++)
            {
                var role = _projectConfig.Roles[i];
                var include = string.Equals(kind, "schema", StringComparison.OrdinalIgnoreCase) && role.CanApproveSchemaReview ||
                              string.Equals(kind, "waiver", StringComparison.OrdinalIgnoreCase) && role.CanApproveWaiver ||
                              string.Equals(kind, "main", StringComparison.OrdinalIgnoreCase) && role.CanApproveMainWriteBack;
                if (include)
                {
                    values.Add(FirstNonEmpty(role.DisplayName, role.Key) + (_programView ? " (" + role.Key + ")" : ""));
                }
            }

            return values.Count == 0 ? "由项目 adapter 默认处理" : string.Join("、", values.ToArray());
        }

        private void DrawStructuredFieldEditor()
        {
            EditorGUILayout.LabelField("字段", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_programView ? "程序视图：显示 canonical 类型；提交时仍写入 lifecycle inputs fields。" : "策划视图：选择字段类型即可，不需要手写模板。", EditorStyles.wordWrappedMiniLabel);
            var validation = ValidateNewTableInputs();
            for (var i = 0; i < _fieldRows.Count; i++)
            {
                DrawFieldRow(i);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("添加字段", "新增一个空字段行。"), GUILayout.Height(24)))
            {
                _fieldRows.Add(new ProjectFieldInput { Key = "field" + (_fieldRows.Count + 1).ToString(), DisplayName = "", ValueKind = "string", Description = "" });
                SyncFieldTemplateTextFromRows();
            }

            if (GUILayout.Button(new GUIContent("添加常用字段", "添加描述、图标、排序、备注。"), GUILayout.Height(24)))
            {
                AddCommonField("description", "描述", "string", "给策划看的补充说明");
                AddCommonField("icon", "图标", "string", "图标资源ID或路径");
                AddCommonField("sortOrder", "排序", "integer", "排序用数字");
                AddCommonField("note", "备注", "string", "仅用于备注说明");
                SyncFieldTemplateTextFromRows();
            }

            if (GUILayout.Button(new GUIContent("重置默认字段", "恢复 id/name 默认字段。"), GUILayout.Height(24)))
            {
                _fieldRows.Clear();
                AddDefaultFieldRows();
                SyncFieldTemplateTextFromRows();
            }
            EditorGUILayout.EndHorizontal();

            if (!validation.Valid)
            {
                EditorGUILayout.HelpBox(validation.Message, MessageType.Warning);
            }

            EditorGUILayout.LabelField("只读模板预览", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(FieldRowsToTemplateText(), EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(48));
        }

        private void DrawFieldRow(int index)
        {
            var field = _fieldRows[index];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            field.Key = EditorGUILayout.TextField(new GUIContent("key", "英文、数字、下划线，不能以数字开头。"), field.Key, GUILayout.MinWidth(110));
            field.DisplayName = EditorGUILayout.TextField(new GUIContent("中文名", "策划看到的字段名。"), field.DisplayName, GUILayout.MinWidth(110));
            field.ValueKind = DrawFieldTypePopup(field.ValueKind, GUILayout.Width(_programView ? 120 : 96));
            if (index == 0)
            {
                field.IsPrimary = true;
                EditorGUILayout.LabelField(new GUIContent("唯一 ID", "第一行默认 ID 字段不可删除，key 可改但仍作为稳定 ID。"), GUILayout.Width(74));
            }
            else
            {
                field.IsPrimary = EditorGUILayout.ToggleLeft(new GUIContent("唯一 ID", "至少需要一个稳定 ID 字段。"), field.IsPrimary, GUILayout.Width(74));
            }

            GUI.enabled = index != 0;
            if (GUILayout.Button(new GUIContent("删除", "第一行默认 ID 字段不可删除。"), GUILayout.Width(48)))
            {
                _fieldRows.RemoveAt(index);
                SyncFieldTemplateTextFromRows();
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            GUI.enabled = true;

            GUI.enabled = index > 1;
            if (GUILayout.Button("↑", GUILayout.Width(28)))
            {
                var previous = _fieldRows[index - 1];
                _fieldRows[index - 1] = field;
                _fieldRows[index] = previous;
            }
            GUI.enabled = index > 0 && index + 1 < _fieldRows.Count;
            if (GUILayout.Button("↓", GUILayout.Width(28)))
            {
                var next = _fieldRows[index + 1];
                _fieldRows[index + 1] = field;
                _fieldRows[index] = next;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            field.Description = EditorGUILayout.TextField(new GUIContent("说明", "必填。说明这个字段是做什么的。"), field.Description);
            if (CanonicalFieldKind(field.ValueKind) == "enum")
            {
                DrawEnumValuesEditor(field);
            }

            if (_programView)
            {
                EditorGUILayout.LabelField("内部类型：" + ValueKindForContract(field), EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
            SyncFieldTemplateTextFromRows();
        }

        private string DrawFieldTypePopup(string valueKind, params GUILayoutOption[] options)
        {
            var specs = GetSupportedFieldTypes();
            var canonical = CanonicalFieldKind(valueKind);
            var selected = 0;
            var labels = new string[specs.Count];
            for (var i = 0; i < specs.Count; i++)
            {
                labels[i] = _programView ? specs[i].Canonical : specs[i].Label;
                if (string.Equals(specs[i].Canonical, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                }
            }

            var next = EditorGUILayout.Popup(selected, labels, options);
            return specs[Mathf.Clamp(next, 0, specs.Count - 1)].Canonical;
        }

        private void DrawEnumValuesEditor(ProjectFieldInput field)
        {
            EditorGUILayout.LabelField("枚举值", EditorStyles.boldLabel);
            for (var i = 0; i < field.EnumValues.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                field.EnumValues[i] = EditorGUILayout.TextField(field.EnumValues[i]);
                if (GUILayout.Button("删除", GUILayout.Width(48)))
                {
                    field.EnumValues.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(new GUIContent("添加枚举值", "枚举至少需要一个选项，例如 common / rare。"), GUILayout.Height(22)))
            {
                field.EnumValues.Add("");
            }

            if (field.EnumValues.Count == 0)
            {
                EditorGUILayout.HelpBox("枚举类型必须至少填写一个枚举值。", MessageType.Warning);
            }
        }

        private void DrawProjectNewTableActions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("新建配表操作", EditorStyles.boldLabel);
            var validation = ValidateNewTableInputs();
            if (!validation.Valid)
            {
                EditorGUILayout.HelpBox(validation.Message, MessageType.Warning);
            }

            _confirmNewTableApply = EditorGUILayout.Toggle(new GUIContent("确认创建在线表并登记", "允许创建/复用在线 Sheet，并登记到 Base。必须先预览通过。"), _confirmNewTableApply);
            var applyReady = validation.Valid && _confirmNewTableApply && LastPreviewPassed("new-table");
            if (_confirmNewTableApply && !LastPreviewPassed("new-table"))
            {
                EditorGUILayout.HelpBox("请先预览新建配表，并确认预览成功后再创建在线表并登记。", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("预览新建配表", "生成 new-table dry-run，预览创建 Sheet、登记 Base 和 SchemaReviews，不写飞书。"), validation.Valid, GUILayout.Height(28)))
            {
                RunProjectLifecycle("new-table", dryRun: true);
            }

            if (DrawJobButton(new GUIContent("创建在线表并登记", "危险操作：创建/复用在线 Sheet，并登记 Base；需要确认且最近一次预览成功。"), applyReady, GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("确认新建配表", "将创建或复用在线 Sheet，并登记在线注册中心和 schema 审查记录。请确认新建配表预览已经通过。", "确认执行", "取消"))
                {
                    RunProjectLifecycle("new-table", dryRun: false);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private bool HasNewTableRequiredFields()
        {
            return ValidateNewTableInputs().Valid;
        }

        private void EnsureNewTableDefaults()
        {
            EnsureOwnerRoleDefault();
            if (!_fieldRowsInitialized)
            {
                AddDefaultFieldRows();
                SyncFieldTemplateTextFromRows();
                _fieldRowsInitialized = true;
            }
        }

        private void EnsureOwnerRoleDefault()
        {
            if (!string.IsNullOrWhiteSpace(_ownerRole))
            {
                return;
            }

            var configured = FirstNonEmpty(_projectConfig.NewTableDefaultOwnerRole, FindRoleKey("tableOwner"));
            if (string.IsNullOrWhiteSpace(configured) && _projectConfig.Roles.Count > 0)
            {
                configured = _projectConfig.Roles[0].Key;
            }

            _ownerRole = configured;
        }

        private string FindRoleKey(string key)
        {
            for (var i = 0; i < _projectConfig.Roles.Count; i++)
            {
                if (string.Equals(_projectConfig.Roles[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return _projectConfig.Roles[i].Key;
                }
            }

            return "";
        }

        private void AddDefaultFieldRows()
        {
            if (_projectConfig.NewTableDefaultFields.Count > 0)
            {
                for (var i = 0; i < _projectConfig.NewTableDefaultFields.Count; i++)
                {
                    var source = _projectConfig.NewTableDefaultFields[i];
                    _fieldRows.Add(new ProjectFieldInput
                    {
                        Key = source.Key,
                        DisplayName = FirstNonEmpty(source.DisplayName, source.Key),
                        ValueKind = CanonicalFieldKind(source.ValueKind),
                        Description = source.Description,
                        IsPrimary = source.IsPrimary || i == 0,
                        EnumValues = ExtractEnumValues(source.ValueKind)
                    });
                }

                if (_fieldRows.Count > 0)
                {
                    _fieldRows[0].IsPrimary = true;
                }

                return;
            }

            _fieldRows.Add(new ProjectFieldInput { Key = "id", DisplayName = "ID", ValueKind = "string", Description = "唯一ID", IsPrimary = true });
            _fieldRows.Add(new ProjectFieldInput { Key = "name", DisplayName = "名称", ValueKind = "string", Description = "显示名称" });
        }

        private void ReplaceFieldRows(List<ProjectFieldInput> fields)
        {
            _fieldRows.Clear();
            if (fields != null)
            {
                _fieldRows.AddRange(fields);
            }

            if (_fieldRows.Count == 0)
            {
                AddDefaultFieldRows();
            }

            _fieldRows[0].IsPrimary = true;
        }

        private void AddCommonField(string key, string displayName, string valueKind, string description)
        {
            for (var i = 0; i < _fieldRows.Count; i++)
            {
                if (string.Equals(_fieldRows[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            _fieldRows.Add(new ProjectFieldInput
            {
                Key = key,
                DisplayName = displayName,
                ValueKind = valueKind,
                Description = description
            });
        }

        private string EffectiveNewTableExcelPath()
        {
            if (!string.IsNullOrWhiteSpace(_excelPath))
            {
                return _excelPath;
            }

            return ".config-sheet-forge/excel-cache/" + FirstNonEmpty(_tableId, "<配表ID>") + ".xlsx";
        }

        private string EffectiveNewTableSheetName()
        {
            return FirstNonEmpty(_sheetName, _tableName, _tableId);
        }

        private NewTableValidationResult ValidateNewTableInputs()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(_tableId) || string.IsNullOrWhiteSpace(_tableName))
            {
                errors.Add("请先填写配表ID和显示名称。");
            }

            if (!string.IsNullOrWhiteSpace(_tableId) && !IsPortableKey(_tableId))
            {
                errors.Add("配表ID 只能用英文、数字、下划线，且不能以数字开头。");
            }

            if (string.IsNullOrWhiteSpace(_ownerRole))
            {
                errors.Add("请选择这张表由谁负责。");
            }

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasPrimary = false;
            for (var i = 0; i < _fieldRows.Count; i++)
            {
                var field = _fieldRows[i];
                var prefix = "字段 " + (i + 1).ToString() + "：";
                if (string.IsNullOrWhiteSpace(field.Key))
                {
                    errors.Add(prefix + "key 不能为空。");
                }
                else if (!IsPortableKey(field.Key))
                {
                    errors.Add(prefix + "key 只能用英文、数字、下划线，且不能以数字开头。");
                }
                else if (!keys.Add(field.Key))
                {
                    errors.Add(prefix + "key 重复。");
                }

                if (string.IsNullOrWhiteSpace(field.DisplayName))
                {
                    errors.Add(prefix + "中文名不能为空。");
                }

                if (string.IsNullOrWhiteSpace(field.Description))
                {
                    errors.Add(prefix + "说明不能为空。");
                }

                if (!IsSupportedFieldKind(field.ValueKind))
                {
                    errors.Add(prefix + "类型不在当前项目支持列表中。");
                }

                if (CanonicalFieldKind(field.ValueKind) == "enum" && !HasNonEmptyEnumValue(field))
                {
                    errors.Add(prefix + "枚举类型至少需要一个枚举值。");
                }

                hasPrimary = hasPrimary || field.IsPrimary;
            }

            if (_fieldRows.Count == 0)
            {
                errors.Add("至少需要一个字段。");
            }

            if (!hasPrimary)
            {
                errors.Add("至少需要一个唯一 ID 字段。");
            }

            return new NewTableValidationResult
            {
                Valid = errors.Count == 0,
                Message = string.Join("\n", errors.ToArray())
            };
        }

        private static bool IsPortableKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var first = value[0];
            if (!(first == '_' || first >= 'A' && first <= 'Z' || first >= 'a' && first <= 'z'))
            {
                return false;
            }

            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (!(c == '_' || c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9'))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSupportedFieldKind(string valueKind)
        {
            var canonical = CanonicalFieldKind(valueKind);
            var specs = GetSupportedFieldTypes();
            for (var i = 0; i < specs.Count; i++)
            {
                if (string.Equals(specs[i].Canonical, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private List<FieldTypeSpec> GetSupportedFieldTypes()
        {
            var specs = new List<FieldTypeSpec>();
            if (_projectConfig.NewTableSupportedFieldTypes.Count > 0)
            {
                for (var i = 0; i < _projectConfig.NewTableSupportedFieldTypes.Count; i++)
                {
                    AddFieldTypeSpec(specs, _projectConfig.NewTableSupportedFieldTypes[i]);
                }
            }

            if (specs.Count == 0)
            {
                AddFieldTypeSpec(specs, "string");
                AddFieldTypeSpec(specs, "integer");
                AddFieldTypeSpec(specs, "number");
                AddFieldTypeSpec(specs, "bool");
                AddFieldTypeSpec(specs, "date");
                AddFieldTypeSpec(specs, "datetime");
                AddFieldTypeSpec(specs, "enum");
                AddFieldTypeSpec(specs, "json");
            }

            return specs;
        }

        private static void AddFieldTypeSpec(List<FieldTypeSpec> specs, string valueKind)
        {
            var canonical = CanonicalFieldKind(valueKind);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                return;
            }

            for (var i = 0; i < specs.Count; i++)
            {
                if (string.Equals(specs[i].Canonical, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            specs.Add(new FieldTypeSpec
            {
                Canonical = canonical,
                Label = PlannerTypeLabel(canonical)
            });
        }

        private static string CanonicalFieldKind(string valueKind)
        {
            var value = (valueKind ?? "").Trim();
            var lower = value.ToLowerInvariant();
            if (lower.StartsWith("enum:", StringComparison.Ordinal) ||
                lower.StartsWith("enum(", StringComparison.Ordinal) ||
                lower.StartsWith("enum{", StringComparison.Ordinal))
            {
                return "enum";
            }

            switch (lower)
            {
                case "":
                case "string":
                case "str":
                case "text":
                    return "string";
                case "integer":
                case "int":
                case "int32":
                case "long":
                case "int64":
                    return "integer";
                case "number":
                case "float":
                case "double":
                case "decimal":
                    return "number";
                case "bool":
                case "boolean":
                    return "bool";
                case "date":
                    return "date";
                case "datetime":
                case "date_time":
                case "timestamp":
                    return "datetime";
                case "enum":
                    return "enum";
                case "json":
                    return "json";
                default:
                    return "";
            }
        }

        private static string PlannerTypeLabel(string canonical)
        {
            switch (canonical)
            {
                case "integer":
                    return "整数";
                case "number":
                    return "小数";
                case "bool":
                    return "是/否";
                case "date":
                    return "日期";
                case "datetime":
                    return "日期时间";
                case "enum":
                    return "枚举";
                case "json":
                    return "JSON";
                default:
                    return "文本";
            }
        }

        private static bool HasNonEmptyEnumValue(ProjectFieldInput field)
        {
            if (field == null || field.EnumValues == null)
            {
                return false;
            }

            for (var i = 0; i < field.EnumValues.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(field.EnumValues[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ValueKindForContract(ProjectFieldInput field)
        {
            var canonical = CanonicalFieldKind(field == null ? "" : field.ValueKind);
            if (canonical == "enum")
            {
                var values = new List<string>();
                if (field != null && field.EnumValues != null)
                {
                    for (var i = 0; i < field.EnumValues.Count; i++)
                    {
                        var value = (field.EnumValues[i] ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
                        {
                            values.Add(value);
                        }
                    }
                }

                return values.Count == 0 ? "enum" : "enum:" + string.Join(",", values.ToArray());
            }

            return FirstNonEmpty(canonical, "string");
        }

        private void SyncFieldTemplateTextFromRows()
        {
            _fieldsText = FieldRowsToTemplateText();
        }

        private string FieldRowsToTemplateText()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < _fieldRows.Count; i++)
            {
                var field = _fieldRows[i];
                builder.Append(field.Key ?? "").Append(" | ")
                    .Append(field.DisplayName ?? "").Append(" | ")
                    .Append(ValueKindForContract(field)).Append(" | ")
                    .Append(field.Description ?? "");
                if (i + 1 < _fieldRows.Count)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private static List<ProjectFieldInput> ParseFieldsText(string text)
        {
            var fields = new List<ProjectFieldInput>();
            foreach (var rawLine in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawLine.Split('|');
                var key = parts.Length > 0 ? parts[0].Trim() : "";
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var field = new ProjectFieldInput
                {
                    Key = key,
                    DisplayName = parts.Length > 1 ? parts[1].Trim() : key,
                    ValueKind = parts.Length > 2 ? CanonicalFieldKind(parts[2].Trim()) : "string",
                    Description = parts.Length > 3 ? parts[3].Trim() : "",
                    IsPrimary = fields.Count == 0 || string.Equals(key, "id", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "key", StringComparison.OrdinalIgnoreCase)
                };
                field.EnumValues.AddRange(ExtractEnumValues(parts.Length > 2 ? parts[2].Trim() : ""));
                fields.Add(field);
            }

            return fields;
        }

        private static List<string> ExtractEnumValues(string valueKind)
        {
            var values = new List<string>();
            var text = (valueKind ?? "").Trim();
            var lower = text.ToLowerInvariant();
            if (lower.StartsWith("enum:", StringComparison.Ordinal))
            {
                AddEnumValues(values, text.Substring("enum:".Length));
            }
            else if (lower.StartsWith("enum(", StringComparison.Ordinal) && text.EndsWith(")", StringComparison.Ordinal))
            {
                AddEnumValues(values, text.Substring(5, text.Length - 6));
            }
            else if (lower.StartsWith("enum{", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal))
            {
                AddEnumValues(values, text.Substring(5, text.Length - 6));
            }

            return values;
        }

        private static void AddEnumValues(List<string> values, string text)
        {
            foreach (var raw in (text ?? "").Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = raw.Trim();
                if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
                {
                    values.Add(value);
                }
            }
        }

        private void DrawProjectMergeInputs()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("PR 合并上下文", EditorStyles.boldLabel);
            DrawReadonlyRow("当前分支", FirstNonEmpty(_mergeSourceBranch, _currentGitBranch, "未知"), "source/head branch。");
            DrawGitHubPreflightCard();
            DrawTargetBranchField();
            DrawReadonlyRow("GitHub PR", BuildPrText(), "如果 gh 可用且 allowPrAutoDetect=true，会尝试自动识别当前分支的 PR。");
            DrawReadonlyRow("当前状态", BuildMergeStatusText(), "合并预览/写回的当前状态。");
            DrawReadonlyRow("下一步", BuildMergeNextStepText(), "按当前状态推荐下一步操作。");
            DrawReadonlyRow("比较范围", BuildMergeTablesText(), "默认比较当前分支所有在线表；单表模式在高级选项中。");
            if (!string.IsNullOrWhiteSpace(_mergeContextStatus))
            {
                EditorGUILayout.LabelField(_mergeContextStatus, EditorStyles.wordWrappedMiniLabel);
            }
            if (_manualTargetBranchOverride)
            {
                EditorGUILayout.HelpBox("当前正在使用手动覆盖的目标分支。想重新跟随 GitHub PR，请恢复 PR 自动识别。", MessageType.Info);
                if (GUILayout.Button(new GUIContent("恢复 PR 自动识别", "清除手动覆盖，重新尝试读取当前 GitHub PR 的 base branch。"), GUILayout.Height(22)))
                {
                    _manualTargetBranchOverride = false;
                    _mergeContextTask = null;
                    RefreshMergeContext();
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("刷新合并上下文", "重新读取当前分支、远端分支、PR、merge-base。"), GUILayout.Height(24)))
            {
                RefreshMergeContext();
            }

            if (GUILayout.Button(new GUIContent("复制 PR 链接", "复制当前识别到的 PR URL。"), GUILayout.Height(24)))
            {
                EditorGUIUtility.systemCopyBuffer = _prUrl ?? "";
            }
            EditorGUILayout.EndHorizontal();

            _showMergeAdvancedOptions = EditorGUILayout.Foldout(_showMergeAdvancedOptions, "高级选项", true);
            if (_showMergeAdvancedOptions)
            {
                DrawReadonlyRow("共同祖先", FirstNonEmpty(_mergeBase, "待推导"), "source/head 与 target/base 的 merge-base。");
                DrawReadonlyRow("目标 Feishu", FirstNonEmpty(_targetFeishuProfile, "按目标分支推导中"), "目标分支对应的 Feishu profile。");
                DrawReadonlyRow("目标 Wiki", BuildTargetWikiText(), "目标分支对应的 Wiki branch 节点。");
                if (_riskModeUnlocked)
                {
                    if (!string.IsNullOrWhiteSpace(_prNumber))
                    {
                        EditorGUILayout.HelpBox("风险配置：一般不需要改目标分支。只有 PR 目标分支异常时，才在这里手动覆盖。", MessageType.Warning);
                        DrawTargetBranchPicker("手动覆盖目标分支", "高级选项：覆盖 GitHub PR base branch。", manualOverride: true);
                    }

                    _mergeTableId = EditorGUILayout.TextField(new GUIContent("只比较单表", "可选；留空时 adapter 可比较当前分支所有在线表。"), _mergeTableId);
                    _mergeReportPath = EditorGUILayout.TextField(new GUIContent("报告路径", "写入 inputs.mergeReportPath。"), _mergeReportPath);
                    _mergedPath = EditorGUILayout.TextField(new GUIContent("合并结果路径", "写入 inputs.mergedPath。"), _mergedPath);
                }
                else if (_programView)
                {
                    EditorGUILayout.LabelField("手动覆盖目标分支、单表比较和输出路径属于风险配置；开启顶部“高级”后才显示。", EditorStyles.wordWrappedMiniLabel);
                }
            }
            _writeBackToMain = EditorGUILayout.Toggle(new GUIContent("申请写回 main", "默认关闭；关闭时只生成 merge.preview。"), _writeBackToMain);
            if (_writeBackToMain)
            {
                _confirmWriteMain = EditorGUILayout.Toggle(new GUIContent("确认写回 main", "只有显式确认后，inputs.confirmWriteMain 才会为 true。"), _confirmWriteMain);
            }
            else
            {
                _confirmWriteMain = false;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTargetBranchBootstrapCard()
        {
            if (!_programView || !TargetBranchLooksMissing())
            {
                return;
            }

            var targetBranch = FirstNonEmpty(_targetBranch, _defaultTargetBranch, "main");
            var targetProfile = FirstNonEmpty(_targetFeishuProfile, targetBranch);
            var targetTitle = FirstNonEmpty(_targetBranchWikiNodeTitle, targetProfile);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标分支还没初始化", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("合并预览需要目标分支 " + targetBranch + " 的在线工作区和表定位。先生成初始化 dry-run，确认会创建/复用哪些在线 Sheet、会写哪些 Base 记录。", EditorStyles.wordWrappedLabel);
            DrawReadonlyRow("目标分支", targetBranch, "通常来自 GitHub PR base branch。");
            DrawReadonlyRow("目标 Feishu", targetProfile, "初始化后 compare-merge 会用它定位 main/base 在线表。");
            DrawReadonlyRow("目标节点", targetTitle, "默认 main；会显示为项目配置表/main。");
            DrawReadonlyRow("表范围", BuildTargetBootstrapTablesText(), "默认使用项目配置中的全部表。");

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("初始化目标分支 " + targetBranch + "（先 dry-run）", "只生成初始化计划，不写飞书、不改本地文件。"), GUILayout.Height(28)))
            {
                RunProjectLifecycle("bootstrap-target-branch-from-local-xlsx", dryRun: true);
            }

            if (GUILayout.Button(new GUIContent("复制 dry-run 命令", "复制通过 adapter 生成初始化计划的命令。"), GUILayout.Height(28), GUILayout.Width(116)))
            {
                CopyProjectLifecycleAdapterCommand("bootstrap-target-branch-from-local-xlsx", dryRun: true);
            }
            EditorGUILayout.EndHorizontal();

            if (_riskModeUnlocked)
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField("执行初始化（分项确认）", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("apply 会严格使用 bot 身份；不会静默 fallback 到 user。执行前会校验最近一次同输入 dry-run result。cache、ProjectSettings、ExcelToSO settings 都是单独确认，默认不写。", MessageType.Warning);
                _confirmTargetCreateOnlineSheets = EditorGUILayout.Toggle(new GUIContent("确认创建/复用在线 Sheet 和目标节点", "允许创建/复用 项目配置表/" + targetTitle + " 以及各表在线 Sheet。"), _confirmTargetCreateOnlineSheets);
                _confirmTargetRegistryUpsert = EditorGUILayout.Toggle(new GUIContent("确认登记 BranchBindings / ConfigSheets", "允许 upsert Base 注册中心中的分支绑定和配表定位。"), _confirmTargetRegistryUpsert);
                _confirmTargetSchemaReviews = EditorGUILayout.Toggle(new GUIContent("确认登记 SchemaReviews baseline", "允许为目标分支初始化 schema 审查记录。"), _confirmTargetSchemaReviews);
                _confirmTargetWriteLocalCache = EditorGUILayout.Toggle(new GUIContent("确认写本地 cache", "可选；允许写 .config-sheet-forge/cache 和 excel-cache。"), _confirmTargetWriteLocalCache);
                _confirmTargetWriteProjectConfig = EditorGUILayout.Toggle(new GUIContent("确认回填 ProjectSettings", "可选；默认不改 ProjectSettings/*ConfigSheetForge*.json。"), _confirmTargetWriteProjectConfig);
                _confirmTargetExcelToSo = EditorGUILayout.Toggle(new GUIContent("确认更新 ExcelToSO settings", "可选；默认不改旧 Excel 源路径和 ExcelToSO settings。"), _confirmTargetExcelToSo);
                var applyReady = _confirmTargetCreateOnlineSheets && _confirmTargetRegistryUpsert && _confirmTargetSchemaReviews && LastPreviewPassed("bootstrap-target-branch-from-local-xlsx");
                if ((_confirmTargetCreateOnlineSheets || _confirmTargetRegistryUpsert || _confirmTargetSchemaReviews) && !LastPreviewPassed("bootstrap-target-branch-from-local-xlsx"))
                {
                    EditorGUILayout.HelpBox("请先生成“初始化目标分支”的 dry-run，并确认预览成功后再 apply。", MessageType.Warning);
                }

                if (DrawJobButton(new GUIContent("执行目标分支初始化", "危险操作：创建/复用在线 Sheet，并按确认项写 Base/cache/ProjectSettings/ExcelToSO。"), applyReady, GUILayout.Height(28)))
                {
                    var message = "将写飞书 " + targetBranch + "：创建/复用在线 Sheet，并写入 BranchBindings / ConfigSheets / SchemaReviews。默认不会改本地 Excel、ProjectSettings 或 ExcelToSO；只有勾选对应项才会写本地。执行前会校验最近一次同输入 dry-run。";
                    if (EditorUtility.DisplayDialog("确认初始化目标分支", message, "确认执行", "取消"))
                    {
                        RunProjectLifecycle("bootstrap-target-branch-from-local-xlsx", dryRun: false);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("执行初始化属于高级写入；需要顶部开启“高级”后才显示确认项。", EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private bool TargetBranchLooksMissing()
        {
            if (!_projectConfig.Exists)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_targetBranchWikiNodeToken) && string.IsNullOrWhiteSpace(_targetBranchWikiNodeUrl))
            {
                return true;
            }

            var summary = (_resultSummary ?? "") + "\n" + (_output ?? "");
            return summary.IndexOf("目标分支", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (summary.IndexOf("缺少", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    summary.IndexOf("找不到", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    summary.IndexOf("missingTarget", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string BuildTargetBootstrapTablesText()
        {
            if (_projectConfig.TableCount > 0)
            {
                return _projectConfig.TableCount.ToString() + " 张共享配置表";
            }

            if (_projectConfig.CurrentBranchTableCount > 0)
            {
                return _projectConfig.CurrentBranchTableCount.ToString() + " 张当前分支表";
            }

            return "项目配置 tables 中的全部表";
        }

        private string BuildTargetBootstrapTableIdsCsv()
        {
            var ids = new List<string>();
            var source = _projectConfig.Tables != null && _projectConfig.Tables.Count > 0
                ? _projectConfig.Tables
                : _projectConfig.CurrentBranchTables;
            if (source != null)
            {
                foreach (var table in source)
                {
                    if (!string.IsNullOrWhiteSpace(table.TableId) && !ids.Contains(table.TableId))
                    {
                        ids.Add(table.TableId);
                    }
                }
            }

            return string.Join(",", ids);
        }

        private string BuildCompareMergeReviewTableIdsCsv()
        {
            if (!string.IsNullOrWhiteSpace(_mergeTableId))
            {
                return _mergeTableId.Trim();
            }

            var ids = new List<string>();
            var source = _projectConfig.CurrentBranchTables != null && _projectConfig.CurrentBranchTables.Count > 0
                ? _projectConfig.CurrentBranchTables
                : _projectConfig.Tables;
            if (source != null)
            {
                foreach (var table in source)
                {
                    if (!string.IsNullOrWhiteSpace(table.TableId) && !ids.Contains(table.TableId))
                    {
                        ids.Add(table.TableId);
                    }
                }
            }

            return string.Join(",", ids);
        }

        private void DrawProjectLifecycleCard(string title, string body, string buttonLabel, string operation, bool dryRun, bool includeNewTableSteps)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(body, EditorStyles.wordWrappedLabel);
            if (!_projectConfig.HasLifecycleAdapter)
            {
                EditorGUILayout.HelpBox("项目配置已找到，但没有 adapterScript 或 contractCommand。请让项目 adapter 在 config 中声明 contract 生成入口。", MessageType.Warning);
            }
            else
            {
                DrawReadonlyRow("Adapter", _projectConfig.AdapterDescription, "项目 adapter 负责把项目 config 转成 lifecycle contract。");
                DrawReadonlyRow("输出请求", "Temp/ConfigSheetForge/unity-lifecycle", "Unity 窗口生成的临时 contract/result 目录。");
                if (includeNewTableSteps)
                {
                    EditorGUILayout.LabelField("dry-run 预览会展示：创建 Sheet、写模板三行、登记 Base、更新 ExcelToSO、创建 SchemaReviews。", EditorStyles.wordWrappedMiniLabel);
                }

                if (DrawJobButton(new GUIContent(ProjectButtonLabel(buttonLabel, operation), "先生成 contract，再运行 apply-contract。dry-run 不写飞书、不改本地文件。"), GUILayout.Height(28)))
                {
                    RunProjectLifecycle(operation, EffectiveDryRun(operation, dryRun));
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawProjectSeedCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("本地 Excel 一次性 Seed", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("把已有 ExcelToSO xlsx 迁移成飞书在线 Sheet Source of Truth。dry-run 只做本地预检和计划展示；apply 会创建/复用在线 Sheet，并在三方一致后回填 cache、项目配置、Base 和 ExcelToSO settings。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox("高风险迁移入口：打开窗口、刷新状态、切换页面都不会自动 seed、下载或改文件。只有手动预览/确认后才会执行。", MessageType.Warning);
            _confirmSeedApply = EditorGUILayout.Toggle(new GUIContent("确认迁移到在线表并回填", "允许创建/复用在线 Sheet，并回填本地/Base 状态。必须先预览通过。"), _confirmSeedApply);
            _confirmSeedExcelToSo = EditorGUILayout.Toggle(new GUIContent("确认更新 ExcelToSO settings", "允许只追加/更新目标表的 ExcelToSO JSON/YAML settings。"), _confirmSeedExcelToSo);
            var seedApplyReady = _confirmSeedApply && _confirmSeedExcelToSo && LastPreviewPassed("seed-from-local-xlsx");
            if ((_confirmSeedApply || _confirmSeedExcelToSo) && !LastPreviewPassed("seed-from-local-xlsx"))
            {
                EditorGUILayout.HelpBox("请先预览本地 Excel Seed，并确认预览成功后再执行迁移。", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("预览本地 Excel Seed", "生成 seed-from-local-xlsx dry-run，不写飞书、不改本地文件。"), GUILayout.Height(28)))
            {
                RunProjectLifecycle("seed-from-local-xlsx", dryRun: true);
            }

            if (DrawJobButton(new GUIContent("迁移到在线表并回填", "危险操作：创建/复用在线 Sheet，并回填本地 cache、项目配置、Base 和 ExcelToSO settings。"), seedApplyReady, GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("确认 seed apply", "将把本地 xlsx 迁移到在线 Sheet，并在三方一致后回填 cache、项目配置、Base 和 ExcelToSO settings。请确认 dry-run 已通过。", "确认执行", "取消"))
                {
                    RunProjectLifecycle("seed-from-local-xlsx", dryRun: false);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawCollapsedOutputStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(CollapsedOutputBarHeight));
            EditorGUILayout.LabelField(BuildCollapsedResultText(), EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            if (IsJobRunning && GUILayout.Button(new GUIContent("取消", "终止当前后台任务。"), GUILayout.Width(58), GUILayout.Height(22)))
            {
                CancelActiveJob();
            }

            if (GUILayout.Button(new GUIContent("复制输出", "复制摘要、命令和详细日志。"), GUILayout.Width(76), GUILayout.Height(22)))
            {
                EditorGUIUtility.systemCopyBuffer = BuildCopyOutput();
            }

            GUI.enabled = File.Exists(_lastResultPath);
            if (GUILayout.Button(new GUIContent("打开 result", "在文件浏览器中显示最近一次 lifecycle result。"), GUILayout.Width(82), GUILayout.Height(22)))
            {
                EditorUtility.RevealInFinder(_lastResultPath);
            }
            GUI.enabled = true;

            GUI.enabled = Directory.Exists(_lastLifecycleDir);
            if (GUILayout.Button(new GUIContent("打开目录", "打开 Temp/ConfigSheetForge/unity-lifecycle。"), GUILayout.Width(76), GUILayout.Height(22)))
            {
                EditorUtility.RevealInFinder(_lastLifecycleDir);
            }
            GUI.enabled = true;

            if (GUILayout.Button(new GUIContent("展开", "展开底部结果抽屉。"), GUILayout.Width(58), GUILayout.Height(22)))
            {
                SetBottomOutputExpanded(true);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOutputTab(bool fullPage, float preferredHeight)
        {
            DrawSectionTitle(fullPage ? "输出" : "最近结果");
            if (fullPage)
            {
                EditorGUILayout.LabelField("查看命令和详细日志，平时不用看。", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(preferredHeight), GUILayout.ExpandHeight(fullPage));
            EditorGUILayout.BeginHorizontal();
            if (!fullPage && GUILayout.Button(new GUIContent("收起", "收起后底部只保留一行最近结果。"), GUILayout.Width(64)))
            {
                SetBottomOutputExpanded(false);
            }

            if (GUILayout.Button(new GUIContent("复制完整命令", "复制最近一次完整命令。"), GUILayout.Width(116)))
            {
                EditorGUIUtility.systemCopyBuffer = _lastCommand ?? "";
            }

            if (GUILayout.Button(new GUIContent("复制输出", "复制命令输出。"), GUILayout.Width(104)))
            {
                EditorGUIUtility.systemCopyBuffer = BuildCopyOutput();
            }

            GUI.enabled = File.Exists(_lastResultPath);
            if (GUILayout.Button(new GUIContent("打开 result 文件", "在文件浏览器中显示最近一次 lifecycle result。"), GUILayout.Width(116)))
            {
                EditorUtility.RevealInFinder(_lastResultPath);
            }
            GUI.enabled = true;

            GUI.enabled = Directory.Exists(_lastLifecycleDir);
            if (GUILayout.Button(new GUIContent("打开 lifecycle 目录", "打开 Temp/ConfigSheetForge/unity-lifecycle。"), GUILayout.Width(136)))
            {
                EditorUtility.RevealInFinder(_lastLifecycleDir);
            }
            GUI.enabled = true;

            if (GUILayout.Button(new GUIContent("清空", "清空输出面板。"), GUILayout.Width(72)))
            {
                SetOutputText("");
                _resultSummary = "";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("摘要", EditorStyles.boldLabel);
            var summaryStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            var summary = BuildVisibleSummary();
            var summaryHeight = Math.Min(fullPage ? 180f : 132f, Math.Max(72f, summaryStyle.CalcHeight(new GUIContent(summary), Math.Max(240f, EditorGUIUtility.currentViewWidth - 56f)) + 10f));
            EditorGUILayout.SelectableLabel(summary, summaryStyle, GUILayout.Height(summaryHeight), GUILayout.ExpandWidth(true));

            _showRecentCommand = EditorGUILayout.Foldout(_showRecentCommand, fullPage ? "命令详情" : "查看完整命令", true);
            if (_showRecentCommand)
            {
                DrawWrappedReadonlyBlock("", string.IsNullOrWhiteSpace(_lastCommand) ? "（暂无）" : _lastCommand, "此窗口最近启动的命令。");
            }

            var showLogs = fullPage;
            if (!fullPage)
            {
                _showDetailedLogs = EditorGUILayout.Foldout(_showDetailedLogs, "详细日志 / result JSON", true);
                showLogs = _showDetailedLogs;
            }
            else
            {
                EditorGUILayout.LabelField("详细日志 / result JSON", EditorStyles.boldLabel);
            }

            if (showLogs)
            {
                var outputStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                var logHeight = Math.Max(80f, preferredHeight - summaryHeight - (fullPage ? 190f : 210f));
                _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll, false, true, GUILayout.Height(logHeight), GUILayout.ExpandHeight(true));
                EditorGUILayout.TextArea(string.IsNullOrWhiteSpace(_output) ? "暂无详细日志。" : _output, outputStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private string BuildCollapsedResultText()
        {
            if (IsJobRunning && _activeJob != null)
            {
                return "最近结果：正在运行：" + _activeJob.Operation + " " + (_activeJob.DryRun ? "dry-run" : "apply") +
                       "，" + FirstNonEmpty(_activeJob.Status, "处理中") + "。可取消或切到“输出”查看日志。";
            }

            var summary = BuildVisibleSummary();
            var state = FirstLine(summary);
            var operation = ExtractSummaryValue(summary, "操作");
            var nextStep = BuildCollapsedNextStep(state);
            return "最近结果：" + FirstNonEmpty(state, "暂无结果") +
                   (string.IsNullOrWhiteSpace(operation) ? "" : "，操作：" + operation) +
                   "。下一步：" + nextStep;
        }

        private static string BuildCollapsedNextStep(string state)
        {
            state = state ?? "";
            if (state.StartsWith("成功", StringComparison.OrdinalIgnoreCase))
            {
                return "继续下一步，或运行 PR 检查。";
            }

            if (state.StartsWith("失败", StringComparison.OrdinalIgnoreCase))
            {
                return "展开结果查看原因，处理后重试。";
            }

            if (state.StartsWith("已取消", StringComparison.OrdinalIgnoreCase))
            {
                return "按需重新预览。";
            }

            return "选择上方操作开始预览。";
        }

        private static string ExtractSummaryValue(string summary, string key)
        {
            if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            var normalized = summary.Replace("\r\n", "\n");
            var prefix = key + ":";
            foreach (var rawLine in normalized.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(prefix.Length).Trim();
                }
            }

            return "";
        }

        private string BuildVisibleSummary()
        {
            if (IsJobRunning && _activeJob != null)
            {
                return _activeJob.BuildLiveSummary();
            }

            return string.IsNullOrWhiteSpace(_resultSummary) ? "暂无结果摘要。" : _resultSummary;
        }

        private string BuildCopyOutput()
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_resultSummary))
            {
                builder.AppendLine("摘要:");
                builder.AppendLine(_resultSummary.TrimEnd());
                builder.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(_lastCommand))
            {
                builder.AppendLine("最近命令:");
                builder.AppendLine(_lastCommand.TrimEnd());
                builder.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(_output))
            {
                builder.AppendLine("详细日志:");
                builder.AppendLine(_output.TrimEnd());
            }

            return builder.ToString().TrimEnd();
        }

        private static string FirstLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "暂无结果摘要。";
            }

            var normalized = value.Replace("\r\n", "\n");
            var index = normalized.IndexOf('\n');
            return index >= 0 ? normalized.Substring(0, index) : normalized;
        }

        private void SetImmediateOutput(string summary, string details)
        {
            _resultSummary = summary ?? "";
            SetOutputText(details ?? "");
            SetBottomOutputExpanded(false, persist: false);
            _showDetailedLogs = false;
            Repaint();
        }

        private bool IsJobRunning
        {
            get { return _activeJob != null && !_activeJob.IsFinished; }
        }

        private bool IsExcelToSoImportRunning
        {
            get { return _excelToSoImportSession != null && !_excelToSoImportSession.IsFinished; }
        }

        private bool IsAnyTaskRunning
        {
            get { return IsJobRunning || IsExcelToSoImportRunning; }
        }

        private float CalculateInlineOutputHeight()
        {
            if (!_showBottomOutput)
            {
                return CollapsedOutputBarHeight;
            }

            var maxByWindow = Math.Max(MinBottomDrawerHeight, position.height - 260f);
            var maxByFraction = Math.Max(MinBottomDrawerHeight, position.height * 0.45f);
            var max = Math.Min(maxByWindow, maxByFraction);
            if (_bottomOutputHeight <= 0f || float.IsNaN(_bottomOutputHeight))
            {
                _bottomOutputHeight = Mathf.Clamp(position.height * 0.32f, MinBottomDrawerHeight, max);
            }

            _bottomOutputHeight = Mathf.Clamp(_bottomOutputHeight, MinBottomDrawerHeight, max);
            return _bottomOutputHeight;
        }

        private void SetBottomOutputExpanded(bool expanded)
        {
            SetBottomOutputExpanded(expanded, persist: true);
        }

        private void SetBottomOutputExpanded(bool expanded, bool persist)
        {
            _showBottomOutput = expanded;
            if (persist)
            {
                EditorPrefs.SetBool(BottomOutputExpandedPrefKey, expanded);
            }
        }

        private void DrawOutputResizeHandle()
        {
            if (!_showBottomOutput || _selectedTab == 4)
            {
                return;
            }

            var rect = GUILayoutUtility.GetRect(1f, 7f, GUILayout.ExpandWidth(true));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 3f, rect.width, 1f), new Color(0.35f, 0.35f, 0.35f, 0.75f));
            }

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _isResizingOutputPanel = true;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDrag && _isResizingOutputPanel)
            {
                var maxByWindow = Math.Max(MinBottomDrawerHeight, position.height - 260f);
                var maxByFraction = Math.Max(MinBottomDrawerHeight, position.height * 0.55f);
                _bottomOutputHeight = Mathf.Clamp(_bottomOutputHeight - Event.current.delta.y, MinBottomDrawerHeight, Math.Min(maxByWindow, maxByFraction));
                Repaint();
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp && _isResizingOutputPanel)
            {
                _isResizingOutputPanel = false;
                EditorPrefs.SetFloat(BottomOutputHeightPrefKey, _bottomOutputHeight);
                Event.current.Use();
            }
        }

        private void OnEditorUpdate()
        {
            var mergeChanged = ApplyMergeContextProbeIfDone();
            var importChanged = TickExcelToSoImportSession();
            if (_activeJob == null)
            {
                if (mergeChanged || importChanged)
                {
                    Repaint();
                }

                return;
            }

            var changed = DrainActiveJobOutput();
            _activeJobStatus = _activeJob.Status;
            if (!_activeJob.IsFinished)
            {
                _resultSummary = _activeJob.BuildLiveSummary();
            }
            if (_activeJob.IsFinished)
            {
                changed = DrainActiveJobOutput() || changed;
                var refresh = _activeJob.RefreshReadonlyStatusOnComplete;
                _resultSummary = _activeJob.FinalSummary;
                if (!IsReadonlyStatusOperation(_activeJob.Operation))
                {
                    _lastCompletedOperation = _activeJob.Operation;
                    _lastCompletedDryRun = _activeJob.DryRun;
                    _lastCompletedSuccess = _activeJob.Success;
                    _lastCompletedInputFingerprint = _activeJob.InputFingerprint;
                }
                CaptureCompletedLifecycleResult(_activeJob);
                SetBottomOutputExpanded(false, persist: false);
                _showDetailedLogs = false;
                _activeJob = null;
                if (refresh)
                {
                    RefreshReadonlyStatus(force: true);
                }
                changed = true;
            }

            if (changed || mergeChanged || importChanged)
            {
                Repaint();
            }
        }

        private bool TickExcelToSoImportSession()
        {
            if (_excelToSoImportSession == null)
            {
                return false;
            }

            var changed = _excelToSoImportSession.Tick();
            foreach (var line in _excelToSoImportSession.DrainLines())
            {
                AppendOutputLine(line);
                changed = true;
            }

            _resultSummary = _excelToSoImportSession.BuildSummary();
            if (_excelToSoImportSession.IsFinished)
            {
                _lastCompletedOperation = "import-unity-assets";
                _lastCompletedDryRun = false;
                _lastCompletedSuccess = _excelToSoImportSession.Success;
                _lastCompletedInputFingerprint = BuildOperationFingerprint("import-unity-assets");
                _resultSummary = _excelToSoImportSession.BuildFinalSummary();
                _excelToSoImportSession = null;
                SetBottomOutputExpanded(false, persist: false);
                _showDetailedLogs = false;
                changed = true;
            }

            return changed;
        }

        private bool DrainActiveJobOutput()
        {
            if (_activeJob == null)
            {
                return false;
            }

            var changed = false;
            foreach (var line in _activeJob.DrainLines())
            {
                AppendOutputLine(line);
                changed = true;
            }

            if (changed && IsJobRunning)
            {
                _outputScroll.y = float.MaxValue;
            }

            return changed;
        }

        private void AppendOutputLine(string line)
        {
            if (_outputBuilder.Length > 0 && !BuilderEndsWithNewLine(_outputBuilder))
            {
                _outputBuilder.AppendLine();
            }

            _outputBuilder.Append(line ?? "");
            if (!BuilderEndsWithNewLine(_outputBuilder))
            {
                _outputBuilder.AppendLine();
            }

            TrimOutputBuffer();
            _output = _outputBuilder.ToString();
        }

        private void SetOutputText(string value)
        {
            _outputBuilder.Length = 0;
            if (!string.IsNullOrEmpty(value))
            {
                _outputBuilder.Append(value);
            }

            TrimOutputBuffer();
            _output = _outputBuilder.ToString();
        }

        private static bool BuilderEndsWithNewLine(StringBuilder builder)
        {
            return builder != null && builder.Length > 0 && (builder[builder.Length - 1] == '\n' || builder[builder.Length - 1] == '\r');
        }

        private void TrimOutputBuffer()
        {
            if (_outputBuilder.Length <= MaxOutputCharacters)
            {
                return;
            }

            var removeCount = _outputBuilder.Length - MaxOutputCharacters;
            var text = _outputBuilder.ToString();
            var newline = text.IndexOf('\n', removeCount);
            if (newline > removeCount && newline < removeCount + 4096)
            {
                removeCount = newline + 1;
            }

            _outputBuilder.Remove(0, Math.Min(removeCount, _outputBuilder.Length));
        }

        private void StartBackgroundJob(ConfigSheetForgeBackgroundJob job)
        {
            if (job == null)
            {
                return;
            }

            if (IsAnyTaskRunning)
            {
                AppendOutputLine("已有后台任务正在运行，请等待完成或先取消。");
                Repaint();
                return;
            }

            _activeJob = job;
            _activeJobStatus = job.Status;
            _lastCommand = job.CommandLine;
            _lastResultPath = job.ResultPath;
            _lastLifecycleDir = job.LifecycleDirectory;
            job.InputFingerprint = BuildOperationFingerprint(job.Operation);
            _outputScroll = Vector2.zero;
            _resultSummary = job.BuildLiveSummary();
            SetOutputText(job.StartOutput);
            SetBottomOutputExpanded(false, persist: false);
            _showDetailedLogs = false;
            Repaint();
            job.Start();
        }

        private void CancelActiveJob()
        {
            if (_activeJob == null)
            {
                return;
            }

            _activeJob.Cancel("已取消，未写本地 cache。");
            AppendOutputLine("正在取消后台任务，会终止当前进程树...");
            Repaint();
        }

        private bool DrawJobButton(GUIContent content, params GUILayoutOption[] options)
        {
            return DrawJobButton(content, true, options);
        }

        private bool DrawJobButton(GUIContent content, bool enabled, params GUILayoutOption[] options)
        {
            var oldEnabled = GUI.enabled;
            var originalTooltip = content == null ? "" : content.tooltip;
            if (content != null && IsAnyTaskRunning)
            {
                content = new GUIContent(content.text, FirstNonEmpty(originalTooltip, "") + "\n后台任务运行中，完成后自动恢复。");
            }

            GUI.enabled = oldEnabled && enabled && !IsAnyTaskRunning;
            var clicked = GUILayout.Button(content, options);
            GUI.enabled = oldEnabled;
            return clicked;
        }

        private bool LastPreviewPassed(string operation)
        {
            return _lastCompletedSuccess &&
                   _lastCompletedDryRun &&
                   string.Equals(_lastCompletedOperation, operation, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(_lastCompletedInputFingerprint, BuildOperationFingerprint(operation), StringComparison.Ordinal);
        }

        private static bool IsReadonlyStatusOperation(string operation)
        {
            return string.Equals(operation, "registry-status", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(operation, "branch-status", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(operation, "sync-status", StringComparison.OrdinalIgnoreCase);
        }

        private void CaptureCompletedLifecycleResult(ConfigSheetForgeBackgroundJob job)
        {
            if (job == null || string.IsNullOrWhiteSpace(job.ResultPath) || !File.Exists(job.ResultPath))
            {
                return;
            }

            if (string.Equals(job.Operation, "compare-merge", StringComparison.OrdinalIgnoreCase) && job.DryRun && job.Success)
            {
                var json = File.ReadAllText(job.ResultPath);
                var fingerprint = ExtractJsonString(json, "requestFingerprint");
                if (!string.IsNullOrWhiteSpace(fingerprint))
                {
                    _lastCompareMergeResultPath = job.ResultPath;
                    _lastCompareMergeRequestFingerprint = fingerprint;
                }
            }

            if (string.Equals(job.Operation, "submit-merge-review", StringComparison.OrdinalIgnoreCase) && job.Success)
            {
                _highlightMergeReview = false;
            }
        }

        private string BuildOperationFingerprint(string operation)
        {
            var builder = new StringBuilder();
            builder.Append(operation ?? "").Append('|');
            builder.Append("branch=").Append(FirstNonEmpty(_currentGitBranch, _projectConfig.GitBranch)).Append('|');

            if (string.Equals(operation, "new-table", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append("table=").Append(_tableId).Append('|');
                builder.Append("name=").Append(_tableName).Append('|');
                builder.Append("owner=").Append(_ownerRole).Append('|');
                builder.Append("schema=").Append(_schemaChangeSummary).Append('|');
                builder.Append("excel=").Append(EffectiveNewTableExcelPath()).Append('|');
                builder.Append("sheet=").Append(EffectiveNewTableSheetName()).Append('|');
                builder.Append("fields=").Append(FieldRowsToTemplateText());
            }
            else if (string.Equals(operation, "seed-from-local-xlsx", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(operation, "bootstrap-from-local-xlsx", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append("table=").Append(_tableId).Append('|');
                builder.Append("name=").Append(_tableName).Append('|');
                builder.Append("excel=").Append(_excelPath).Append('|');
                builder.Append("sheet=").Append(_sheetName);
            }
            else if (string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append("source=").Append(FirstNonEmpty(_mergeSourceBranch, _currentGitBranch)).Append('|');
                builder.Append("target=").Append(FirstNonEmpty(_targetBranch, _defaultTargetBranch)).Append('|');
                builder.Append("table=").Append(_mergeTableId).Append('|');
                builder.Append("report=").Append(_mergeReportPath).Append('|');
                builder.Append("merged=").Append(_mergedPath);
            }
            else if (string.Equals(operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append("target=").Append(FirstNonEmpty(_targetBranch, _defaultTargetBranch, "main")).Append('|');
                builder.Append("profile=").Append(FirstNonEmpty(_targetFeishuProfile, _targetBranch, "main")).Append('|');
                builder.Append("node=").Append(FirstNonEmpty(_targetBranchWikiNodeTitle, _targetFeishuProfile, "main")).Append('|');
                builder.Append("tables=").Append(BuildTargetBootstrapTableIdsCsv());
            }

            return builder.ToString();
        }

        private bool EffectiveDryRun(string operation, bool defaultDryRun)
        {
            if (string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase))
            {
                return !(_writeBackToMain && _confirmWriteMain);
            }

            return defaultDryRun;
        }

        private bool ConfirmApplyForOperation(string operation)
        {
            if (string.Equals(operation, "sync-cache", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "sync-from-online-sheet", StringComparison.OrdinalIgnoreCase))
            {
                return _confirmSyncApply;
            }

            if (string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase))
            {
                return _writeBackToMain && _confirmWriteMain;
            }

            if (string.Equals(operation, "new-table", StringComparison.OrdinalIgnoreCase))
            {
                return _confirmNewTableApply;
            }

            if (string.Equals(operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return _confirmSeedApply;
        }

        private string TableIdForOperation(string operation)
        {
            return string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase)
                ? _mergeTableId
                : _tableId;
        }

        private string ProjectButtonLabel(string fallback, string operation)
        {
            if (string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase) && _writeBackToMain && _confirmWriteMain)
            {
                return "确认写回 main";
            }

            return fallback;
        }

        private void DrawStep(string number, string title, string body, string buttonLabel, string tooltip, Action action)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(number + ". " + title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(body, EditorStyles.wordWrappedLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(buttonLabel, tooltip), GUILayout.Height(28)))
            {
                action();
            }

            if (GUILayout.Button(new GUIContent("复制命令", "复制命令，不直接运行。"), GUILayout.Width(116), GUILayout.Height(28)))
            {
                if (buttonLabel.IndexOf("检查", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CopyCommand("doctor", "--details");
                }
                else if (buttonLabel.IndexOf("初始化", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CopyCommand("init");
                }
                else
                {
                    CopyCommand("gate");
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static void DrawSectionTitle(string title)
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void DrawTabIntro(string title, string oneLine)
        {
            DrawSectionTitle(title);
            EditorGUILayout.LabelField(oneLine, EditorStyles.wordWrappedMiniLabel);
        }

        private List<WorkflowStatusCard> BuildWorkflowStatusCards(string projectRoot)
        {
            var cards = new List<WorkflowStatusCard>();
            var branchBound = BranchLooksBound();
            var onlineReadable = OnlineTablesReadable();
            var cacheFresh = CacheLooksFresh(projectRoot);
            cards.Add(new WorkflowStatusCard(
                "分支工作区",
                branchBound ? "已绑定" : "未绑定",
                branchBound ? "已从注册中心或分支规则找到当前分支在线工作区。" : "当前分支还没有在线工作区；下一步从 main/目标分支初始化。",
                branchBound ? WorkflowStatusKind.Ok : WorkflowStatusKind.Warning));
            cards.Add(new WorkflowStatusCard(
                "在线表",
                BuildOnlineTableStatus(),
                BuildOnlineTableStatusDetail(),
                onlineReadable ? WorkflowStatusKind.Ok : WorkflowStatusKind.Warning));
            cards.Add(new WorkflowStatusCard(
                "本地 cache",
                cacheFresh ? "无需同步" : BuildCacheOverviewText(projectRoot),
                cacheFresh ? "最近同步预览显示无变化，或当前分支表已有本地 semantic/hash cache。" : "先预览同步计划；apply 只在确认后写本地 cache。",
                cacheFresh ? WorkflowStatusKind.Ok : WorkflowStatusKind.Pending));
            cards.Add(new WorkflowStatusCard(
                "PR gate",
                BuildGateStatusText(),
                BuildGateStatusDetail(),
                GateLooksPassed() ? WorkflowStatusKind.Ok : (_gateReportSummary.HasReport ? WorkflowStatusKind.Error : WorkflowStatusKind.Pending)));
            cards.Add(new WorkflowStatusCard(
                "下一步",
                BuildNextStepText(projectRoot),
                "只读刷新不会下载、导出或写入任何项目文件。",
                WorkflowStatusKind.Neutral));
            return cards;
        }

        private static void DrawStatusCardGrid(List<WorkflowStatusCard> cards)
        {
            var viewWidth = Math.Max(480f, EditorGUIUtility.currentViewWidth - 36f);
            var columns = Mathf.Clamp(Mathf.FloorToInt(viewWidth / 210f), 2, 5);
            if (cards.Count == 5 && columns == 4)
            {
                columns = 3;
            }

            var cardWidth = Math.Max(160f, (viewWidth - (columns - 1) * 6f) / columns);
            for (var i = 0; i < cards.Count; i += columns)
            {
                EditorGUILayout.BeginHorizontal();
                for (var column = 0; column < columns; column++)
                {
                    var index = i + column;
                    if (index < cards.Count)
                    {
                        DrawStatusCard(cards[index], cardWidth);
                    }
                    else
                    {
                        GUILayout.Space(cardWidth);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawStatusCard(WorkflowStatusCard card, float width)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width), GUILayout.Height(86));
            var bar = GUILayoutUtility.GetRect(1f, 4f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar, StatusColor(card.Kind));
            EditorGUILayout.LabelField(card.Label, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(StatusKindText(card.Kind) + "：" + card.Status, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(card.Detail, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
            EditorGUILayout.EndVertical();
        }

        private static Color StatusColor(WorkflowStatusKind kind)
        {
            switch (kind)
            {
                case WorkflowStatusKind.Ok:
                    return new Color(0.24f, 0.66f, 0.34f);
                case WorkflowStatusKind.Warning:
                    return new Color(0.86f, 0.56f, 0.16f);
                case WorkflowStatusKind.Error:
                    return new Color(0.78f, 0.25f, 0.22f);
                case WorkflowStatusKind.Pending:
                    return new Color(0.24f, 0.47f, 0.78f);
                default:
                    return new Color(0.45f, 0.45f, 0.45f);
            }
        }

        private static string StatusKindText(WorkflowStatusKind kind)
        {
            switch (kind)
            {
                case WorkflowStatusKind.Ok:
                    return "正常";
                case WorkflowStatusKind.Warning:
                    return "警告";
                case WorkflowStatusKind.Error:
                    return "失败";
                case WorkflowStatusKind.Pending:
                    return "待处理";
                default:
                    return "状态";
            }
        }

        private static void DrawReadonlyRow(string label, string value, string tooltip)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), GUILayout.Width(110));
            EditorGUILayout.SelectableLabel(value, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawWrappedReadonlyBlock(string label, string value, string tooltip)
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                EditorGUILayout.LabelField(new GUIContent(label, tooltip), EditorStyles.boldLabel);
            }

            var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            var width = Math.Max(240f, EditorGUIUtility.currentViewWidth - 48f);
            var height = Math.Max(EditorGUIUtility.singleLineHeight * 2f, style.CalcHeight(new GUIContent(value), width) + 8f);
            EditorGUILayout.SelectableLabel(value, style, GUILayout.Height(height), GUILayout.ExpandWidth(true));
        }

        private static void DrawStatusRow(string label, bool ok, string okText, string missingText)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110));
            var oldColor = GUI.color;
            GUI.color = ok ? new Color(0.45f, 0.85f, 0.45f) : new Color(1f, 0.72f, 0.35f);
            EditorGUILayout.LabelField(ok ? okText : missingText, EditorStyles.miniLabel);
            GUI.color = oldColor;
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawPathField(string label, ref string value, string tooltip)
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.TextField(new GUIContent(label, tooltip), value);
            if (GUILayout.Button(new GUIContent("...", "Choose a semantic workbook JSON file."), GUILayout.Width(32)))
            {
                var selected = EditorUtility.OpenFilePanel("Choose " + label, FindProjectRoot(), "json");
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    value = ToProjectRelativePath(selected);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void RunCli(params string[] args)
        {
            RunCliInternal(false, args);
        }

        private void StartRegistryStatusProbeIfNeeded(bool force)
        {
            if (IsJobRunning || _projectConfig == null || !_projectConfig.Exists || string.IsNullOrWhiteSpace(_projectConfig.RegistryBaseToken))
            {
                return;
            }

            var projectRoot = FindProjectRoot();
            var key = _projectConfig.ProjectConfigPath + "|" + FirstNonEmpty(_currentGitBranch, _projectConfig.GitBranch);
            if (!force &&
                string.Equals(_lastRegistryStatusProbeKey, key, StringComparison.Ordinal) &&
                (DateTime.UtcNow - _lastRegistryStatusProbeUtc).TotalSeconds < RegistryStatusProbeCacheSeconds)
            {
                return;
            }

            var resultPath = GetUnityLifecyclePath(projectRoot, "registry-status.result.json");
            var cli = ResolveCoreCli(projectRoot);
            var args = new[]
            {
                "registry-status",
                "--manifest",
                _projectConfig.ProjectConfigPath,
                "--out",
                resultPath,
                "--details"
            };
            _lastCommand = cli.ToCommandLine(args);
            if (!cli.CanLaunch)
            {
                return;
            }

            _lastRegistryStatusProbeKey = key;
            _lastRegistryStatusProbeUtc = DateTime.UtcNow;
            StartBackgroundJob(ConfigSheetForgeBackgroundJob.CreateSingleProcess(
                "registry-status",
                dryRun: true,
                commandLine: _lastCommand,
                executable: cli.Executable,
                arguments: cli.BuildArguments(args),
                workingDirectory: projectRoot,
                resultPath: resultPath,
                lifecycleDirectory: GetUnityLifecycleDirectory(projectRoot),
                refreshReadonlyStatusOnComplete: true,
                projectConfig: _projectConfig));
        }

        private void RunCliInternal(bool refreshReadonlyStatusOnComplete, params string[] args)
        {
            var cleanArgs = CleanArgs(args);
            var projectRoot = FindProjectRoot();
            var cli = ResolveCoreCli(projectRoot);
            _lastCommand = cli.ToCommandLine(cleanArgs);
            if (!cli.CanLaunch)
            {
                SetImmediateOutput(ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, cli.FailureReason, _projectConfig), "");
                return;
            }

            var commandName = cleanArgs.Length > 0 ? cleanArgs[0] : "CLI";
            StartBackgroundJob(ConfigSheetForgeBackgroundJob.CreateSingleProcess(
                "CLI: " + commandName,
                dryRun: Array.IndexOf(cleanArgs, "--dry-run") >= 0,
                commandLine: _lastCommand,
                executable: cli.Executable,
                arguments: cli.BuildArguments(cleanArgs),
                workingDirectory: projectRoot,
                resultPath: "",
                lifecycleDirectory: GetUnityLifecycleDirectory(projectRoot),
                refreshReadonlyStatusOnComplete: refreshReadonlyStatusOnComplete,
                projectConfig: _projectConfig));
        }

        private void RunSyncCache(bool apply)
        {
            RefreshReadonlyStatus();
            if (_projectConfig.Exists && _projectConfig.HasLifecycleAdapter)
            {
                RunProjectLifecycle("sync-cache", dryRun: !apply);
                return;
            }

            RunSyncCacheCli(apply);
        }

        private void RunCurrentBranchBootstrapPreview()
        {
            RefreshReadonlyStatus();
            if (!_projectConfig.Exists)
            {
                SetImmediateOutput("未发现项目配置。请确认 ProjectSettings 下存在 *ConfigSheetForge*.json。", "");
                return;
            }

            var projectRoot = FindProjectRoot();
            var resultPath = GetUnityLifecyclePath(projectRoot, "bootstrap-current-branch-from-target.result.json");
            var args = new[]
            {
                "bootstrap-current-branch-from-target",
                "--manifest",
                _projectConfig.ProjectConfigPath,
                "--target-branch",
                FirstNonEmpty(_targetBranch, _projectConfig.DefaultTargetBranch, "main"),
                "--out",
                resultPath,
                "--details",
                "--dry-run"
            };
            var cli = ResolveCoreCli(projectRoot);
            _lastCommand = cli.ToCommandLine(args);
            if (!cli.CanLaunch)
            {
                SetImmediateOutput(ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, cli.FailureReason, _projectConfig), "");
                return;
            }

            StartBackgroundJob(ConfigSheetForgeBackgroundJob.CreateSingleProcess(
                "bootstrap-current-branch-from-target",
                dryRun: true,
                commandLine: _lastCommand,
                executable: cli.Executable,
                arguments: cli.BuildArguments(args),
                workingDirectory: projectRoot,
                resultPath: resultPath,
                lifecycleDirectory: GetUnityLifecycleDirectory(projectRoot),
                refreshReadonlyStatusOnComplete: true,
                projectConfig: _projectConfig));
        }

        private void RunCurrentBranchBootstrapApply()
        {
            RefreshReadonlyStatus();
            if (!_projectConfig.Exists)
            {
                SetImmediateOutput("未发现项目配置。请确认 ProjectSettings 下存在 *ConfigSheetForge*.json。", "");
                return;
            }

            if (string.IsNullOrWhiteSpace(_lastResultPath) || !File.Exists(_lastResultPath))
            {
                SetImmediateOutput("找不到最近一次派生当前分支 dry-run result。", "请先点“预览派生当前分支”。");
                return;
            }

            var projectRoot = FindProjectRoot();
            var resultPath = GetUnityLifecyclePath(projectRoot, "bootstrap-current-branch-from-target.apply.result.json");
            var args = new List<string>
            {
                "bootstrap-current-branch-from-target",
                "--manifest",
                _projectConfig.ProjectConfigPath,
                "--target-branch",
                FirstNonEmpty(_targetBranch, _projectConfig.DefaultTargetBranch, "main"),
                "--preview-result",
                _lastResultPath,
                "--out",
                resultPath,
                "--details",
                "--apply",
                "--confirm-create-online-sheets",
                "--confirm-registry-upsert",
                "--confirm-schema-reviews"
            };
            var cli = ResolveCoreCli(projectRoot);
            _lastCommand = cli.ToCommandLine(args.ToArray());
            if (!cli.CanLaunch)
            {
                SetImmediateOutput(ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, cli.FailureReason, _projectConfig), "");
                return;
            }

            StartBackgroundJob(ConfigSheetForgeBackgroundJob.CreateSingleProcess(
                "bootstrap-current-branch-from-target",
                dryRun: false,
                commandLine: _lastCommand,
                executable: cli.Executable,
                arguments: cli.BuildArguments(args.ToArray()),
                workingDirectory: projectRoot,
                resultPath: resultPath,
                lifecycleDirectory: GetUnityLifecycleDirectory(projectRoot),
                refreshReadonlyStatusOnComplete: true,
                projectConfig: _projectConfig));
        }

        private void RunSyncCacheCli(bool apply)
        {
            RefreshReadonlyStatus();
            if (!_projectConfig.Exists)
            {
                SetImmediateOutput("未发现项目配置。请确认 ProjectSettings 下存在 *ConfigSheetForge*.json。", "");
                return;
            }

            var projectRoot = FindProjectRoot();
            var resultPath = GetUnityLifecyclePath(projectRoot, "sync-cache.result.json");
            var args = BuildSyncCacheArgs(apply, resultPath);
            var cli = ResolveCoreCli(projectRoot);
            _lastCommand = cli.ToCommandLine(args);
            if (!cli.CanLaunch)
            {
                SetImmediateOutput(ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, cli.FailureReason, _projectConfig), "");
                return;
            }

            StartBackgroundJob(ConfigSheetForgeBackgroundJob.CreateSingleProcess(
                "sync-cache",
                dryRun: !apply,
                commandLine: _lastCommand,
                executable: cli.Executable,
                arguments: cli.BuildArguments(args),
                workingDirectory: projectRoot,
                resultPath: resultPath,
                lifecycleDirectory: GetUnityLifecycleDirectory(projectRoot),
                refreshReadonlyStatusOnComplete: true,
                projectConfig: _projectConfig));
        }

        private void RunPrGateReport()
        {
            if (_projectConfig.Exists && _projectConfig.HasLifecycleAdapter)
            {
                RunProjectLifecycle("pr-gate-report", dryRun: false);
            }
            else
            {
                RunCliInternal(true, "gate", "--details", "--report", ResolveGateReportPath(FindProjectRoot()));
            }
        }

        private bool CanSubmitMergeReview()
        {
            return _projectConfig.Exists &&
                   LastPreviewPassed("compare-merge") &&
                   !string.IsNullOrWhiteSpace(_lastCompareMergeResultPath) &&
                   File.Exists(_lastCompareMergeResultPath) &&
                   !string.IsNullOrWhiteSpace(_lastCompareMergeRequestFingerprint);
        }

        private void DrawMergeReviewSubmitCard(bool reviewReady)
        {
            var previousColor = GUI.color;
            if (_highlightMergeReview)
            {
                GUI.color = new Color(1f, 0.92f, 0.65f);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.color = previousColor;
            EditorGUILayout.LabelField("合并审查记录", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("“生成合并预览”只是看差异；“提交合并审查记录”会写 Base MergeReviews，让 PR gate 能知道这次预览已经审过。", EditorStyles.wordWrappedLabel);
            if (reviewReady)
            {
                EditorGUILayout.LabelField("最近一次合并预览有效，可以提交审查记录。", EditorStyles.wordWrappedMiniLabel);
                DrawReadonlyRow("当前分支", FirstNonEmpty(_projectConfig.GitBranch, _mergeSourceBranch, "未读取"), "写入 MergeReviews 的 Git分支。");
                DrawReadonlyRow("目标分支", FirstNonEmpty(_targetBranch, _projectConfig.DefaultTargetBranch, "main"), "这次合并预览对应的目标分支。");
                DrawReadonlyRow("表范围", FirstNonEmpty(BuildCompareMergeReviewTableIdsCsv(), "__project_pr_gate__"), "项目级审查会覆盖当前合并预览里的表范围。");
                DrawReadonlyRow("请求指纹", _lastCompareMergeRequestFingerprint, "提交时 CLI 会校验它和最近一次合并预览一致。");
                DrawReadonlyRow("写入对象", "Base MergeReviews", "不会写 main、本地 cache、ProjectSettings 或 ExcelToSO。");
                if (_programView)
                {
                    DrawReadonlyRow("预览 result", _lastCompareMergeResultPath, "提交时会校验这个 result 的 requestFingerprint。");
                }
            }
            else
            {
                EditorGUILayout.HelpBox("请先点击“生成合并预览”，预览成功后才可以提交合并审查记录。", MessageType.Info);
            }

            _mergeReviewComment = EditorGUILayout.TextField(new GUIContent("审查备注", "可选。写给 MergeReviews 的说明。"), _mergeReviewComment);
            EditorGUILayout.EndVertical();
        }

        private void RunSubmitMergeReview()
        {
            if (!CanSubmitMergeReview())
            {
                SetImmediateOutput("还不能提交合并审查记录。", "请先生成合并预览，并确认预览成功后再提交审查记录。");
                return;
            }

            var projectRoot = FindProjectRoot();
            var workDir = GetUnityLifecycleDirectory(projectRoot);
            Directory.CreateDirectory(workDir);
            var requestPath = Path.Combine(workDir, "submit-merge-review.contract.json");
            var resultPath = Path.Combine(workDir, "submit-merge-review.result.json");
            File.WriteAllText(requestPath, BuildSubmitMergeReviewRequestJson(), Utf8NoBom);

            var cli = ResolveCoreCli(projectRoot);
            var args = new List<string>
            {
                "apply-contract",
                "--request",
                requestPath,
                "--out",
                resultPath,
                "--preview-result",
                _lastCompareMergeResultPath,
                "--confirm"
            };
            _lastCommand = cli.ToCommandLine(args);
            if (!cli.CanLaunch)
            {
                SetImmediateOutput(ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, cli.FailureReason, _projectConfig), "");
                return;
            }

            StartBackgroundJob(ConfigSheetForgeBackgroundJob.CreateSingleProcess(
                "submit-merge-review",
                dryRun: false,
                commandLine: _lastCommand,
                executable: cli.Executable,
                arguments: cli.BuildArguments(args.ToArray()),
                workingDirectory: projectRoot,
                resultPath: resultPath,
                lifecycleDirectory: workDir,
                refreshReadonlyStatusOnComplete: true,
                projectConfig: _projectConfig));
        }

        private string BuildSubmitMergeReviewRequestJson()
        {
            var tableIds = BuildCompareMergeReviewTableIdsCsv();
            var sourceBranch = FirstNonEmpty(_mergeSourceBranch, _currentGitBranch, _projectConfig.GitBranch);
            var targetBranch = FirstNonEmpty(_targetBranch, _defaultTargetBranch, "main");
            var builder = new StringBuilder();
            builder.AppendLine("{");
            AppendJsonProperty(builder, "operation", "submit-merge-review", comma: true);
            AppendJsonProperty(builder, "locale", "zh-Hans", comma: true);
            AppendJsonProperty(builder, "dryRun", false, comma: true);
            builder.AppendLine("  \"registry\": {");
            AppendNestedJsonProperty(builder, "baseToken", _projectConfig.RegistryBaseToken, comma: true);
            AppendNestedJsonProperty(builder, "baseUrl", _projectConfig.RegistryBaseUrl, comma: false);
            builder.AppendLine("  },");
            builder.AppendLine("  \"git\": {");
            AppendNestedJsonProperty(builder, "branch", sourceBranch, comma: true);
            AppendNestedJsonProperty(builder, "head", "", comma: false);
            builder.AppendLine("  },");
            builder.AppendLine("  \"mergeInputs\": {");
            AppendNestedJsonProperty(builder, "sourceBranch", sourceBranch, comma: true);
            AppendNestedJsonProperty(builder, "targetBranch", targetBranch, comma: true);
            AppendNestedJsonProperty(builder, "prNumber", _prNumber, comma: true);
            AppendNestedJsonProperty(builder, "prUrl", _prUrl, comma: true);
            AppendNestedJsonProperty(builder, "mergeReportPath", _mergeReportPath, comma: true);
            AppendNestedJsonProperty(builder, "mergedPath", _mergedPath, comma: false);
            builder.AppendLine("  },");
            builder.AppendLine("  \"mergeReview\": {");
            AppendNestedJsonProperty(builder, "sourceBranch", sourceBranch, comma: true);
            AppendNestedJsonProperty(builder, "targetBranch", targetBranch, comma: true);
            AppendNestedJsonArrayProperty(builder, "tableIds", tableIds, comma: true);
            AppendNestedJsonProperty(builder, "tableId", "__project_pr_gate__", comma: true);
            AppendNestedJsonProperty(builder, "prNumber", _prNumber, comma: true);
            AppendNestedJsonProperty(builder, "prUrl", _prUrl, comma: true);
            AppendNestedJsonProperty(builder, "mergeReportPath", _mergeReportPath, comma: true);
            AppendNestedJsonProperty(builder, "mergedPath", _mergedPath, comma: true);
            AppendNestedJsonProperty(builder, "requestFingerprint", _lastCompareMergeRequestFingerprint, comma: true);
            AppendNestedJsonProperty(builder, "requiredPreviewFingerprint", _lastCompareMergeRequestFingerprint, comma: true);
            AppendNestedJsonProperty(builder, "previewResultPath", _lastCompareMergeResultPath, comma: true);
            AppendNestedJsonProperty(builder, "approverRole", "configOwner", comma: true);
            AppendNestedJsonProperty(builder, "reviewComment", _mergeReviewComment, comma: true);
            AppendNestedJsonProperty(builder, "status", "approved", comma: true);
            AppendNestedJsonProperty(builder, "confirmSubmit", true, comma: false);
            builder.AppendLine("  }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private void RunSimpleReviewLifecycle(string operation)
        {
            var projectRoot = FindProjectRoot();
            var workDir = GetUnityLifecycleDirectory(projectRoot);
            Directory.CreateDirectory(workDir);
            var requestPath = Path.Combine(workDir, operation + ".contract.json");
            var resultPath = Path.Combine(workDir, operation + ".result.json");
            File.WriteAllText(requestPath, BuildSimpleReviewRequestJson(operation), Utf8NoBom);

            var cli = ResolveCoreCli(projectRoot);
            var args = new List<string>
            {
                "apply-contract",
                "--request",
                requestPath,
                "--out",
                resultPath,
                "--confirm"
            };
            _lastCommand = cli.ToCommandLine(args);
            if (!cli.CanLaunch)
            {
                SetImmediateOutput(ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, cli.FailureReason, _projectConfig), "");
                return;
            }

            StartBackgroundJob(ConfigSheetForgeBackgroundJob.CreateSingleProcess(
                operation,
                dryRun: false,
                commandLine: _lastCommand,
                executable: cli.Executable,
                arguments: cli.BuildArguments(args.ToArray()),
                workingDirectory: projectRoot,
                resultPath: resultPath,
                lifecycleDirectory: workDir,
                refreshReadonlyStatusOnComplete: true,
                projectConfig: _projectConfig));
        }

        private string BuildSimpleReviewRequestJson(string operation)
        {
            var branch = FirstNonEmpty(_currentGitBranch, _projectConfig.GitBranch);
            var builder = new StringBuilder();
            builder.AppendLine("{");
            AppendJsonProperty(builder, "operation", operation, comma: true);
            AppendJsonProperty(builder, "locale", "zh-Hans", comma: true);
            AppendJsonProperty(builder, "dryRun", false, comma: true);
            builder.AppendLine("  \"registry\": {");
            AppendNestedJsonProperty(builder, "baseToken", _projectConfig.RegistryBaseToken, comma: true);
            AppendNestedJsonProperty(builder, "baseUrl", _projectConfig.RegistryBaseUrl, comma: false);
            builder.AppendLine("  },");
            builder.AppendLine("  \"git\": {");
            AppendNestedJsonProperty(builder, "branch", branch, comma: false);
            builder.AppendLine("  },");
            if (string.Equals(operation, "approve-schema-review", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine("  \"schemaReviewApproval\": {");
                AppendNestedJsonProperty(builder, "tableId", _schemaReviewTableId, comma: true);
                AppendNestedJsonProperty(builder, "branch", branch, comma: true);
                AppendNestedJsonProperty(builder, "profile", _projectConfig.BranchProfile, comma: true);
                AppendNestedJsonProperty(builder, "status", "approved", comma: true);
                AppendNestedJsonProperty(builder, "approverRole", "schemaReviewer", comma: true);
                AppendNestedJsonProperty(builder, "reviewComment", _schemaReviewComment, comma: true);
                AppendNestedJsonProperty(builder, "confirmSubmit", true, comma: false);
                builder.AppendLine("  }");
            }
            else
            {
                builder.AppendLine("  \"waiverApproval\": {");
                AppendNestedJsonProperty(builder, "tableId", FirstNonEmpty(_waiverTableId, "__project_pr_gate__"), comma: true);
                AppendNestedJsonProperty(builder, "branch", branch, comma: true);
                AppendNestedJsonProperty(builder, "reason", _waiverReason, comma: true);
                AppendNestedJsonProperty(builder, "expiresAt", _waiverExpiresAt, comma: true);
                AppendNestedJsonProperty(builder, "approvedByRole", "configOwner", comma: true);
                AppendNestedJsonProperty(builder, "confirmApprove", true, comma: false);
                builder.AppendLine("  }");
            }

            builder.AppendLine("}");
            return builder.ToString();
        }

        private void RunProjectLifecycle(string operation, bool dryRun)
        {
            RefreshReadonlyStatus();
            if (!_projectConfig.Exists)
            {
                SetImmediateOutput("未发现项目配置。请确认 ProjectSettings 下存在 *ConfigSheetForge*.json。", "");
                return;
            }

            if (!_projectConfig.HasLifecycleAdapter)
            {
                SetImmediateOutput("项目配置缺少 adapterScript 或 contractCommand，无法生成 lifecycle contract。", "");
                return;
            }

            var projectRoot = FindProjectRoot();
            var workDir = GetUnityLifecycleDirectory(projectRoot);
            Directory.CreateDirectory(workDir);
            var defaultRequestPath = Path.Combine(workDir, operation + ".contract.json");
            var inputsPath = Path.Combine(workDir, operation + ".inputs.json");
            var requestPath = string.IsNullOrWhiteSpace(_projectConfig.ContractRequestPath)
                ? defaultRequestPath
                : ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, ConfigSheetForgeEditorUtility.ExpandToken(_projectConfig.ContractRequestPath, projectRoot, _projectConfig, operation, defaultRequestPath, inputsPath, dryRun));
            var resultPath = Path.Combine(workDir, operation + ".result.json");
            var finalGateReportPath = ResolveGateReportPath(projectRoot);
            File.WriteAllText(inputsPath, BuildLifecycleInputsJson(operation, dryRun, finalGateReportPath), Utf8NoBom);

            var adapter = ConfigSheetForgeEditorUtility.CreateProjectLifecycleCommand(_projectConfig, projectRoot, operation, requestPath, inputsPath, dryRun);
            var cli = ResolveCoreCli(projectRoot);
            var applyArgs = BuildApplyContractArgs(operation, requestPath, resultPath, finalGateReportPath, dryRun);
            _lastCommand = adapter.ToCommandLine() + Environment.NewLine +
                           cli.ToCommandLine(applyArgs);
            if (!cli.CanLaunch)
            {
                SetImmediateOutput(ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, cli.FailureReason, _projectConfig), "");
                return;
            }

            StartBackgroundJob(ConfigSheetForgeBackgroundJob.CreateLifecycle(
                operation,
                dryRun,
                projectRoot,
                requestPath,
                inputsPath,
                resultPath,
                finalGateReportPath,
                ConfigSheetForgeEditorUtility.ResolveExecutable(adapter.Executable),
                adapter.Arguments.ToArray(),
                adapter.ToCommandLine(),
                cli.Executable,
                cli.BuildArguments(applyArgs),
                cli.ToCommandLine(applyArgs),
                _lastCommand,
                _projectConfig));
        }

        private static string[] CleanArgs(IEnumerable<string> args)
        {
            var cleaned = new List<string>();
            foreach (var arg in args)
            {
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    cleaned.Add(arg);
                }
            }

            return cleaned.ToArray();
        }

        private string[] BuildSyncCacheArgs(bool apply, string resultPath)
        {
            var args = new List<string>
            {
                "sync-cache",
                "--manifest",
                _projectConfig.ProjectConfigPath,
                "--out",
                resultPath,
                "--details"
            };
            args.Add(apply ? "--yes" : "--dry-run");
            return CleanArgs(args);
        }

        private string[] BuildPrGateArgs()
        {
            var projectRoot = FindProjectRoot();
            return new[] { "gate", "--details", "--report", ResolveGateReportPath(projectRoot) };
        }

        private void CopyCommand(params string[] args)
        {
            var cleanArgs = CleanArgs(args);
            var cli = ResolveCoreCli(FindProjectRoot());
            EditorGUIUtility.systemCopyBuffer = cli.ToCommandLine(cleanArgs);
        }

        private void CopySyncCacheCommand(bool apply)
        {
            RefreshReadonlyStatus();
            var projectRoot = FindProjectRoot();
            if (_projectConfig.Exists && _projectConfig.HasLifecycleAdapter)
            {
                CopyProjectLifecycleAdapterCommand("sync-cache", dryRun: !apply);
                return;
            }

            var resultPath = GetUnityLifecyclePath(projectRoot, "sync-cache.result.json");
            var cli = ResolveCoreCli(projectRoot);
            EditorGUIUtility.systemCopyBuffer = cli.ToCommandLine(BuildSyncCacheArgs(apply, resultPath));
        }

        private void CopyPrGateCommand()
        {
            RefreshReadonlyStatus();
            if (_projectConfig.Exists && _projectConfig.HasLifecycleAdapter)
            {
                CopyProjectLifecycleAdapterCommand("pr-gate-report", dryRun: false);
                return;
            }

            var cli = ResolveCoreCli(FindProjectRoot());
            EditorGUIUtility.systemCopyBuffer = cli.ToCommandLine(BuildPrGateArgs());
        }

        private string BuildRegistryMigrateDryRunCommand()
        {
            var projectRoot = FindProjectRoot();
            var cli = ResolveCoreCli(projectRoot);
            var args = new List<string>
            {
                "registry-migrate",
                "--base",
                FirstNonEmpty(_projectConfig.RegistryBaseToken, "<registry-base-token>"),
                "--only",
                "review-status-options",
                "--locale",
                "zh-Hans",
                "--dry-run",
                "--out",
                GetUnityLifecyclePath(projectRoot, "registry-migrate.result.json")
            };
            return cli.ToCommandLine(CleanArgs(args));
        }

        private void CopyProjectLifecycleAdapterCommand(string operation, bool dryRun)
        {
            RefreshReadonlyStatus();
            var projectRoot = FindProjectRoot();
            var requestPath = Path.Combine(projectRoot, "Temp", "ConfigSheetForge", "unity-lifecycle", operation + ".contract.json");
            var inputsPath = Path.Combine(projectRoot, "Temp", "ConfigSheetForge", "unity-lifecycle", operation + ".inputs.json");
            var adapter = ConfigSheetForgeEditorUtility.CreateProjectLifecycleCommand(_projectConfig, projectRoot, operation, requestPath, inputsPath, dryRun);
            EditorGUIUtility.systemCopyBuffer = adapter.ToCommandLine();
        }

        private void RefreshReadonlyStatus(bool force = false)
        {
            if (!force && (DateTime.UtcNow - _lastReadonlyRefreshUtc).TotalSeconds < ReadonlyRefreshThrottleSeconds)
            {
                return;
            }

            _lastReadonlyRefreshUtc = DateTime.UtcNow;
            var projectRoot = FindProjectRoot();
            _currentGitBranch = TryReadGitBranch(projectRoot);
            _projectConfig = ConfigSheetForgeEditorUtility.LoadProjectConfigSummary(projectRoot, _currentGitBranch);
            EnsureNewTableDefaults();
            _cliInvocation = ConfigSheetForgeEditorUtility.ResolveCoreCli(_projectConfig, projectRoot, _cliPath);
            MergeLifecycleSummary(projectRoot, "registry-status.result.json");
            MergeLifecycleSummary(projectRoot, "sync-cache.result.json");
            MergeLifecycleSummary(projectRoot, "pr-gate-report.result.json");
            _gateReportSummary = LoadGateReportSummary(projectRoot);
            RefreshMergeContext();
        }

        private ConfigSheetForgeCliInvocation ResolveCoreCli(string projectRoot)
        {
            _cliInvocation = ConfigSheetForgeEditorUtility.ResolveCoreCli(_projectConfig, projectRoot, _cliPath);
            return _cliInvocation;
        }

        private void RefreshMergeContext()
        {
            var projectRoot = FindProjectRoot();
            _mergeSourceBranch = FirstNonEmpty(_currentGitBranch, _projectConfig.GitBranch, TryReadGitBranch(projectRoot));
            _defaultTargetBranch = FirstNonEmpty(_projectConfig.DefaultTargetBranch, "main");
            if (string.IsNullOrWhiteSpace(_targetBranch) || string.Equals(_targetBranch, _mergeSourceBranch, StringComparison.OrdinalIgnoreCase))
            {
                _targetBranch = _defaultTargetBranch;
            }

            UpdateMergeInputPaths();
            UpdateMergeWorkspaceContext();

            _githubRepository = FirstNonEmpty(_projectConfig.GithubRepository, TryReadGithubRepository(projectRoot));
            _allowPrAutoDetect = _projectConfig.AllowPrAutoDetect && !_manualTargetBranchOverride;
            if (_manualTargetBranchOverride)
            {
                _githubPreflight.Status = "GitHub PR 识别：已手动覆盖目标分支";
                _githubPreflight.NextStep = "当前使用手动目标分支；如需跟随 PR base，请点“恢复 PR 自动识别”。";
            }
            else if (_cachedMergeContextProbe == null)
            {
                _githubPreflight = GitHubPreflightSummary.Pending();
            }

            if (_targetBranchOptions.Count == 0)
            {
                _targetBranchOptions = BuildRemoteBranchOptionsFromRefs(projectRoot, !string.IsNullOrWhiteSpace(_prNumber) ? _targetBranch : "", _defaultTargetBranch, _targetBranch);
            }

            _mergeBase = "正在计算";
            _mergeContextStatus = "已按当前分支和目标分支推导合并上下文；不需要手动选择 base/ours/theirs 文件。";
            var probeKey = BuildMergeContextProbeKey(projectRoot, _mergeSourceBranch, _targetBranch, _allowPrAutoDetect, _githubRepository, _manualTargetBranchOverride);
            if (TryApplyCachedMergeContextProbe(probeKey))
            {
                return;
            }

            if (_mergeContextTask == null || !string.Equals(_mergeContextProbeKey, probeKey, StringComparison.Ordinal))
            {
                var source = _mergeSourceBranch;
                var target = _targetBranch;
                var allowPr = _allowPrAutoDetect;
                var repository = _githubRepository;
                var defaultTarget = _defaultTargetBranch;
                var installHelpUrl = _projectConfig.GithubInstallHelpUrl;
                _mergeContextProbeKey = probeKey;
                _mergeContextTask = Task.Run(() => ProbeMergeContext(projectRoot, source, target, allowPr, repository, defaultTarget, installHelpUrl));
                _mergeContextStatus = allowPr
                    ? "已开始后台识别 GitHub PR、目标分支列表和 merge-base；gh 不可用时会使用目标分支 fallback。"
                    : "已开始后台读取目标分支列表并计算 merge-base。";
            }
        }

        private bool ApplyMergeContextProbeIfDone()
        {
            if (_mergeContextTask == null || !_mergeContextTask.IsCompleted)
            {
                return false;
            }

            try
            {
                var result = _mergeContextTask.Result;
                _cachedMergeContextProbe = result;
                _cachedMergeContextProbeKey = _mergeContextProbeKey;
                _cachedMergeContextProbeUtc = DateTime.UtcNow;
                ApplyMergeContextProbeResult(result);
            }
            catch (Exception ex)
            {
                _mergeBase = "未计算到共同祖先";
                _mergeContextStatus = "PR 自动识别失败：" + ex.Message + "；使用目标分支 fallback。";
            }
            finally
            {
                _mergeContextTask = null;
            }

            return true;
        }

        private bool TryApplyCachedMergeContextProbe(string probeKey)
        {
            if (_cachedMergeContextProbe == null ||
                string.IsNullOrWhiteSpace(probeKey) ||
                !string.Equals(_cachedMergeContextProbeKey, probeKey, StringComparison.Ordinal) ||
                (DateTime.UtcNow - _cachedMergeContextProbeUtc).TotalSeconds > MergeProbeCacheSeconds)
            {
                return false;
            }

            ApplyMergeContextProbeResult(_cachedMergeContextProbe);
            return true;
        }

        private void ApplyMergeContextProbeResult(MergeContextProbeResult result)
        {
            if (result == null)
            {
                return;
            }

            if (result.Preflight != null)
            {
                _githubPreflight = result.Preflight;
            }

            if (result.BranchOptions != null && result.BranchOptions.Count > 0)
            {
                _targetBranchOptions = result.BranchOptions;
            }

            if (result.Found)
            {
                _manualTargetBranchOverride = false;
                _prNumber = result.Number;
                _prUrl = result.Url;
                _targetBranch = FirstNonEmpty(result.BaseBranch, _targetBranch, _defaultTargetBranch);
                _mergeBase = FirstNonEmpty(result.MergeBase, "未计算到共同祖先");
                UpdateMergeInputPaths();
                UpdateMergeWorkspaceContext();
                _mergeContextStatus = "已识别 GitHub PR #" + _prNumber + "，目标分支来自 PR：" + _targetBranch + "。";
            }
            else if (!string.IsNullOrWhiteSpace(result.Message))
            {
                _mergeBase = FirstNonEmpty(result.MergeBase, "未计算到共同祖先");
                _mergeContextStatus = result.Message + "；使用目标分支 fallback。";
            }
        }

        private static string BuildMergeContextProbeKey(string projectRoot, string sourceBranch, string targetBranch, bool allowPrAutoDetect, string repository, bool manualOverride)
        {
            return string.Join("\n", new[]
            {
                projectRoot ?? "",
                sourceBranch ?? "",
                targetBranch ?? "",
                allowPrAutoDetect ? "pr" : "no-pr",
                repository ?? "",
                manualOverride ? "manual" : "auto"
            });
        }

        private void UpdateMergeInputPaths()
        {
            var sourceSlug = SlugifyPathToken(FirstNonEmpty(_mergeSourceBranch, _currentGitBranch, "source"));
            var targetSlug = SlugifyPathToken(FirstNonEmpty(_targetBranch, _defaultTargetBranch, "target"));
            _basePath = "Temp/ConfigSheetForge/merge-inputs/" + targetSlug + "_base.semantic.json";
            _oursPath = "Temp/ConfigSheetForge/merge-inputs/" + sourceSlug + "_ours.semantic.json";
            _theirsPath = "Temp/ConfigSheetForge/merge-inputs/" + targetSlug + "_theirs.semantic.json";
        }

        private void UpdateMergeWorkspaceContext()
        {
            var target = ResolveWorkspaceForBranch(FirstNonEmpty(_targetBranch, _defaultTargetBranch, "main"));
            _targetFeishuProfile = FirstNonEmpty(target.Profile, target.FeishuBranch);
            _targetBranchWikiNodeTitle = target.NodeTitle;
            _targetBranchWikiNodeUrl = target.WikiNodeUrl;
            _targetBranchWikiNodeToken = target.WikiNodeToken;
        }

        private BranchWorkspaceResolution ResolveWorkspaceForBranch(string gitBranch)
        {
            var request = new LifecycleContractRequest
            {
                Git = new ContractGitSpec { Branch = gitBranch },
                BranchWorkspace = new BranchWorkspaceContract
                {
                    GitBranch = gitBranch,
                    RootWikiToken = _projectConfig.BranchWorkspaceRootWikiToken,
                    RootWikiUrl = _projectConfig.BranchWorkspaceRootWikiUrl,
                    ProfileNameTemplate = FirstNonEmpty(_projectConfig.ProfileNameTemplate, "{gitBranch}"),
                    BranchNodeTitleTemplate = FirstNonEmpty(_projectConfig.BranchNodeTitleTemplate, "branch-{slug}"),
                    MainGitBranch = FirstNonEmpty(_projectConfig.MainGitBranch, "main"),
                    MainFeishuBranch = FirstNonEmpty(_projectConfig.MainFeishuBranch, "main")
                }
            };

            if (_projectConfig.BranchBindings != null)
            {
                request.BranchBindings.AddRange(_projectConfig.BranchBindings);
            }

            return BranchWorkspaceResolver.Resolve(request);
        }

        private void MergeLifecycleSummary(string projectRoot, string fileName)
        {
            var path = GetUnityLifecyclePath(projectRoot, fileName);
            if (!File.Exists(path) || _projectConfig == null)
            {
                return;
            }

            var live = ProjectConfigProbe.ProbeFile(path, _currentGitBranch);
            if (!live.Exists)
            {
                return;
            }

            if (live.CurrentBranchTables.Count > 0)
            {
                _projectConfig.CurrentBranchTables.Clear();
                _projectConfig.CurrentBranchTables.AddRange(live.CurrentBranchTables);
                _projectConfig.CurrentBranchTableCount = live.CurrentBranchTableCount;
                _projectConfig.CurrentBranchTableSource = "最近一次 " + fileName;
            }

            _projectConfig.BranchWikiNodeTitle = FirstNonEmpty(_projectConfig.BranchWikiNodeTitle, live.BranchWikiNodeTitle);
            _projectConfig.BranchWikiNodeUrl = FirstNonEmpty(_projectConfig.BranchWikiNodeUrl, live.BranchWikiNodeUrl);
            _projectConfig.BranchWikiNodeToken = FirstNonEmpty(_projectConfig.BranchWikiNodeToken, live.BranchWikiNodeToken);
            _projectConfig.Profile = FirstNonEmpty(_projectConfig.Profile, live.Profile);
            _projectConfig.FeishuBranch = FirstNonEmpty(_projectConfig.FeishuBranch, live.FeishuBranch);
            _projectConfig.LiveBranchBindingStatus = FirstNonEmpty(live.LiveBranchBindingStatus, _projectConfig.LiveBranchBindingStatus);
            _projectConfig.LiveNextRecommendedAction = FirstNonEmpty(live.LiveNextRecommendedAction, _projectConfig.LiveNextRecommendedAction);
            _projectConfig.LiveExpectedTableCount = live.LiveExpectedTableCount > 0 ? live.LiveExpectedTableCount : _projectConfig.LiveExpectedTableCount;
            _projectConfig.LiveRegisteredTableCount = live.LiveRegisteredTableCount > 0 ? live.LiveRegisteredTableCount : _projectConfig.LiveRegisteredTableCount;
            if (live.LiveMissingTables.Count > 0)
            {
                _projectConfig.LiveMissingTables.Clear();
                _projectConfig.LiveMissingTables.AddRange(live.LiveMissingTables);
            }

            if (live.LiveMissingLocators.Count > 0)
            {
                _projectConfig.LiveMissingLocators.Clear();
                _projectConfig.LiveMissingLocators.AddRange(live.LiveMissingLocators);
            }

            if (live.LiveDuplicateConfigSheets.Count > 0)
            {
                _projectConfig.LiveDuplicateConfigSheets.Clear();
                _projectConfig.LiveDuplicateConfigSheets.AddRange(live.LiveDuplicateConfigSheets);
            }

            if (!string.IsNullOrWhiteSpace(live.SyncCacheStatus))
            {
                _projectConfig.SyncCacheStatus = live.SyncCacheStatus;
                _projectConfig.SyncCacheChangedTables.Clear();
                _projectConfig.SyncCacheMissingCacheTables.Clear();
                _projectConfig.SyncCacheUpToDateTables.Clear();
                _projectConfig.SyncCacheBlockedTables.Clear();
                _projectConfig.SyncCacheChangedTables.AddRange(live.SyncCacheChangedTables);
                _projectConfig.SyncCacheMissingCacheTables.AddRange(live.SyncCacheMissingCacheTables);
                _projectConfig.SyncCacheUpToDateTables.AddRange(live.SyncCacheUpToDateTables);
                _projectConfig.SyncCacheBlockedTables.AddRange(live.SyncCacheBlockedTables);
            }
        }

        private GateReportSummaryView LoadGateReportSummary(string projectRoot)
        {
            var path = ResolveGateReportPath(projectRoot);
            if (!File.Exists(path))
            {
                return GateReportSummaryView.NotFound(path);
            }

            try
            {
                return GateReportSummaryView.FromJson(path, File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                return new GateReportSummaryView
                {
                    Path = path,
                    ShortText = "Gate report 无法读取",
                    DetailText = "最近一次报告读取失败：" + ex.Message
                };
            }
        }

        private string[] BuildApplyContractArgs(string operation, string requestPath, string resultPath, string finalGateReportPath, bool dryRun)
        {
            var args = new List<string> { "apply-contract", "--request", requestPath, "--out", resultPath };
            if (dryRun)
            {
                args.Add("--dry-run");
            }

            if (string.Equals(operation, "pr-gate-report", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--report");
                args.Add(finalGateReportPath);
            }
            else if (string.Equals(operation, "sync-cache", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(operation, "sync-from-online-sheet", StringComparison.OrdinalIgnoreCase))
            {
                if (!dryRun && _confirmSyncApply)
                {
                    args.Add("--yes");
                }
            }
            else if (string.Equals(operation, "seed-from-local-xlsx", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(operation, "bootstrap-from-local-xlsx", StringComparison.OrdinalIgnoreCase))
            {
                if (_confirmSeedApply)
                {
                    args.Add("--yes");
                }

                if (_confirmSeedExcelToSo)
                {
                    args.Add("--confirm-excel-to-so");
                }
            }
            else if (string.Equals(operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase))
            {
                if (!dryRun)
                {
                    if (!string.IsNullOrWhiteSpace(_lastResultPath))
                    {
                        args.Add("--preview-result");
                        args.Add(_lastResultPath);
                    }

                    if (_confirmTargetCreateOnlineSheets)
                    {
                        args.Add("--confirm-create-online-sheets");
                    }

                    if (_confirmTargetRegistryUpsert)
                    {
                        args.Add("--confirm-registry-upsert");
                    }

                    if (_confirmTargetSchemaReviews)
                    {
                        args.Add("--confirm-schema-reviews");
                    }

                    if (_confirmTargetWriteLocalCache)
                    {
                        args.Add("--confirm-write-local-cache");
                    }

                    if (_confirmTargetWriteProjectConfig)
                    {
                        args.Add("--confirm-write-project-config");
                    }

                    if (_confirmTargetExcelToSo)
                    {
                        args.Add("--confirm-excel-to-so");
                    }
                }
            }
            else if (string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase))
            {
                if (!dryRun && _writeBackToMain && _confirmWriteMain)
                {
                    args.Add("--yes");
                }
            }
            else if (string.Equals(operation, "new-table", StringComparison.OrdinalIgnoreCase))
            {
                if (!dryRun && _confirmNewTableApply)
                {
                    args.Add("--yes");
                }
            }

            return args.ToArray();
        }

        private string ResolveGateReportPath(string projectRoot)
        {
            var path = FirstNonEmpty(_projectConfig.GateReportPath, Path.Combine("Temp", "ConfigSheetForge", "pr-gate-report.json"));
            return ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, path);
        }

        private static string GetUnityLifecyclePath(string projectRoot, string fileName)
        {
            return Path.Combine(GetUnityLifecycleDirectory(projectRoot), fileName);
        }

        private static string GetUnityLifecycleDirectory(string projectRoot)
        {
            return Path.Combine(projectRoot, "Temp", "ConfigSheetForge", "unity-lifecycle");
        }

        private string BuildCurrentBranchTableCountText()
        {
            if (_projectConfig.CurrentBranchTableCount > 0)
            {
                var source = FirstNonEmpty(_projectConfig.CurrentBranchTableSource, "project-config");
                return _projectConfig.CurrentBranchTableCount.ToString() + " 张（" + source + "）";
            }

            if (_projectConfig.TableCount > 0)
            {
                return _projectConfig.TableCount.ToString() + " 张（共享配置，待确认 branch/profile）";
            }

            return "未找到表记录";
        }

        private string BuildOnlineTableStatus()
        {
            if (!_projectConfig.Exists)
            {
                return "未找到项目配置";
            }

            if (string.Equals(_projectConfig.LiveBranchBindingStatus, "ok", StringComparison.OrdinalIgnoreCase) &&
                _projectConfig.LiveRegisteredTableCount > 0 &&
                _projectConfig.LiveExpectedTableCount > 0 &&
                _projectConfig.LiveRegisteredTableCount >= _projectConfig.LiveExpectedTableCount &&
                _projectConfig.LiveMissingLocators.Count == 0)
            {
                return _projectConfig.LiveRegisteredTableCount.ToString() + "/" + _projectConfig.LiveExpectedTableCount.ToString() + " 在线表已登记";
            }

            if (!BranchLooksBound())
            {
                return "未绑定分支";
            }

            if (OnlineTablesReadable())
            {
                return "在线表可读";
            }

            if (_projectConfig.CurrentBranchTables.Count == 0)
            {
                return "未读取到在线表";
            }

            return "部分不可读";
        }

        private string BuildOnlineTableStatusDetail()
        {
            if (!_projectConfig.Exists)
            {
                return "未找到项目共享配置。";
            }

            if (string.Equals(_projectConfig.LiveBranchBindingStatus, "ok", StringComparison.OrdinalIgnoreCase) &&
                _projectConfig.LiveRegisteredTableCount > 0)
            {
                if (_projectConfig.LiveMissingTables.Count == 0 && _projectConfig.LiveMissingLocators.Count == 0)
                {
                    return "已从飞书 Base 注册中心读取当前分支在线表定位；ProjectSettings 不需要保存 Sheet token。";
                }

                return "注册中心已读取，但缺少：" +
                       (_projectConfig.LiveMissingTables.Count > 0 ? "表记录 " + string.Join(", ", _projectConfig.LiveMissingTables) + " " : "") +
                       (_projectConfig.LiveMissingLocators.Count > 0 ? "Sheet 定位 " + string.Join(", ", _projectConfig.LiveMissingLocators) : "");
            }

            if (!BranchLooksBound())
            {
                return "当前分支还没有绑定在线工作区。";
            }

            if (OnlineTablesReadable())
            {
                return "当前分支可读取 " + _projectConfig.CurrentBranchTables.Count.ToString() + " 张在线表。";
            }

            if (_projectConfig.CurrentBranchTables.Count == 0)
            {
                return "还没找到当前分支的在线表记录。";
            }

            return "有表缺少在线链接、工作表 ID，或被权限阻断。";
        }

        private bool BranchLooksBound()
        {
            if (string.Equals(_projectConfig.LiveBranchBindingStatus, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(_projectConfig.BranchWikiNodeToken) ||
                   !string.IsNullOrWhiteSpace(_projectConfig.BranchWikiNodeUrl) ||
                   !string.IsNullOrWhiteSpace(_projectConfig.BranchWikiNodeTitle);
        }

        private bool OnlineTablesReadable()
        {
            if (string.Equals(_projectConfig.LiveBranchBindingStatus, "ok", StringComparison.OrdinalIgnoreCase) &&
                _projectConfig.LiveRegisteredTableCount > 0 &&
                _projectConfig.LiveMissingTables.Count == 0 &&
                _projectConfig.LiveMissingLocators.Count == 0 &&
                _projectConfig.LiveDuplicateConfigSheets.Count == 0)
            {
                return true;
            }

            var tables = _projectConfig.CurrentBranchTables;
            if (tables == null || tables.Count == 0)
            {
                return false;
            }

            foreach (var table in tables)
            {
                if (!string.IsNullOrWhiteSpace(table.BlockingReason) ||
                    string.IsNullOrWhiteSpace(table.SpreadsheetToken) ||
                    string.IsNullOrWhiteSpace(table.SheetId))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CacheLooksFresh(string projectRoot)
        {
            if (string.Equals(_projectConfig.SyncCacheStatus, "upToDate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(_projectConfig.SyncCacheStatus, "needsUpdate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_projectConfig.SyncCacheStatus, "missingCache", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_projectConfig.SyncCacheStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var tables = _projectConfig.CurrentBranchTables;
            if (tables == null || tables.Count == 0)
            {
                return false;
            }

            foreach (var table in tables)
            {
                if (!HasCacheFiles(projectRoot, table))
                {
                    return false;
                }
            }

            return true;
        }

        private bool SyncPreviewRequiresCacheWrite()
        {
            return string.Equals(_projectConfig.SyncCacheStatus, "needsUpdate", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(_projectConfig.SyncCacheStatus, "missingCache", StringComparison.OrdinalIgnoreCase) ||
                   _projectConfig.SyncCacheChangedTables.Count > 0 ||
                   _projectConfig.SyncCacheMissingCacheTables.Count > 0;
        }

        private string BuildSyncCacheStatusText()
        {
            if (string.Equals(_projectConfig.SyncCacheStatus, "upToDate", StringComparison.OrdinalIgnoreCase))
            {
                return "无变化，cache 已是最新";
            }

            if (string.Equals(_projectConfig.SyncCacheStatus, "needsUpdate", StringComparison.OrdinalIgnoreCase))
            {
                return "需要更新：" + string.Join(", ", _projectConfig.SyncCacheChangedTables);
            }

            if (string.Equals(_projectConfig.SyncCacheStatus, "missingCache", StringComparison.OrdinalIgnoreCase))
            {
                return "缺少 cache：" + string.Join(", ", _projectConfig.SyncCacheMissingCacheTables);
            }

            if (string.Equals(_projectConfig.SyncCacheStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                return "被阻断：" + string.Join(", ", _projectConfig.SyncCacheBlockedTables);
            }

            return "还没有同步预览结论";
        }

        private bool GateLooksPassed()
        {
            return _gateReportSummary != null &&
                   _gateReportSummary.Passed.HasValue &&
                   _gateReportSummary.Passed.Value &&
                   _gateReportSummary.Failures.Count == 0;
        }

        private string BuildGateStatusText()
        {
            if (_gateReportSummary == null || !_gateReportSummary.HasReport)
            {
                return "未生成";
            }

            if (_gateReportSummary.Waived)
            {
                return "临时放行";
            }

            return GateLooksPassed() ? "通过" : _gateReportSummary.ShortText;
        }

        private string BuildGateStatusDetail()
        {
            if (_gateReportSummary == null || !_gateReportSummary.HasReport)
            {
                return "还没有最近一次 PR 检查报告。";
            }

            if (GateLooksPassed())
            {
                if (_gateReportSummary.Waived)
                {
                    return "已由配置负责人 waiver 临时放行；请注意过期时间。";
                }

                return "最近一次 PR 检查已通过。";
            }

            if (_gateReportSummary.Failures.Count > 0)
            {
                return _gateReportSummary.Failures[0].NextStep;
            }

            return "请重新运行 PR 检查查看阻断原因。";
        }

        private string BuildNextStepText(string projectRoot)
        {
            if (!_projectConfig.Exists)
            {
                return "先配置项目";
            }

            if (GateLooksPassed())
            {
                return "可以提交 PR";
            }

            if (!BranchLooksBound() ||
                string.Equals(_projectConfig.LiveNextRecommendedAction, "bootstrap-current-branch-from-target", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(_projectConfig.LiveBranchBindingStatus, "missing", StringComparison.OrdinalIgnoreCase) && _projectConfig.LiveRegisteredTableCount == 0))
            {
                return "初始化当前分支在线表";
            }

            if (!OnlineTablesReadable())
            {
                return _projectConfig.LiveMissingTables.Count > 0 ? "初始化当前分支在线表" : "修复在线表定位";
            }

            if (string.Equals(_projectConfig.SyncCacheStatus, "upToDate", StringComparison.OrdinalIgnoreCase))
            {
                return "运行 PR 检查";
            }

            if (string.Equals(_projectConfig.SyncCacheStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                return "修复同步预检问题";
            }

            if (string.Equals(_projectConfig.SyncCacheStatus, "needsUpdate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_projectConfig.SyncCacheStatus, "missingCache", StringComparison.OrdinalIgnoreCase))
            {
                return "写入本地 cache";
            }

            if (!CacheLooksFresh(projectRoot))
            {
                return "预览同步计划";
            }

            if (!GateLooksPassed())
            {
                return "运行 PR 检查";
            }

            return "可以提交 PR";
        }

        private string BuildNextStepButtonText(string projectRoot)
        {
            var next = BuildNextStepText(projectRoot);
            if (string.Equals(next, "初始化当前分支在线表", StringComparison.OrdinalIgnoreCase))
            {
                return "初始化当前分支在线表";
            }

            if (string.Equals(next, "写入本地 cache", StringComparison.OrdinalIgnoreCase))
            {
                return "写入本地 cache";
            }

            if (string.Equals(next, "运行 PR 检查", StringComparison.OrdinalIgnoreCase))
            {
                return "运行 PR 检查";
            }

            if (string.Equals(next, "修复同步预检问题", StringComparison.OrdinalIgnoreCase))
            {
                return "查看同步问题";
            }

            if (string.Equals(next, "可以提交 PR", StringComparison.OrdinalIgnoreCase))
            {
                return "刷新状态";
            }

            return "预览同步计划";
        }

        private void RunNextStep(string projectRoot)
        {
            var next = BuildNextStepText(projectRoot);
            if (string.Equals(next, "运行 PR 检查", StringComparison.OrdinalIgnoreCase))
            {
                RunPrGateReport();
                return;
            }

            if (string.Equals(next, "可以提交 PR", StringComparison.OrdinalIgnoreCase))
            {
                RefreshReadonlyStatus(force: true);
                SetImmediateOutput("已刷新状态。当前看起来可以提交 PR。", "");
                return;
            }

            if (string.Equals(next, "初始化当前分支在线表", StringComparison.OrdinalIgnoreCase))
            {
                RunCurrentBranchBootstrapPreview();
                return;
            }

            if (string.Equals(next, "写入本地 cache", StringComparison.OrdinalIgnoreCase))
            {
                _selectedTab = TablesTab;
                _showSyncSection = true;
                SetImmediateOutput("请在“配表”页确认写入本地 cache。", "写入前需要最近一次同步预览通过，并勾选确认。");
                return;
            }

            if (string.Equals(next, "修复同步预检问题", StringComparison.OrdinalIgnoreCase))
            {
                _selectedTab = TablesTab;
                _showSyncSection = true;
                SetBottomOutputExpanded(true, persist: true);
                SetImmediateOutput("同步预检未通过，先修复在线读取/三方一致性问题。", BuildSyncCacheStatusText());
                return;
            }

            RunSyncCache(apply: false);
        }

        private string BuildCacheOverviewText(string projectRoot)
        {
            if (!string.IsNullOrWhiteSpace(_projectConfig.SyncCacheStatus))
            {
                return BuildSyncCacheStatusText();
            }

            var tables = _projectConfig.CurrentBranchTables;
            if (tables == null || tables.Count == 0)
            {
                return "待同步";
            }

            var ready = 0;
            foreach (var table in tables)
            {
                if (HasCacheFiles(projectRoot, table))
                {
                    ready++;
                }
            }

            if (ready == tables.Count)
            {
                return "已有 cache（" + ready.ToString() + "/" + tables.Count.ToString() + "）";
            }

            if (ready == 0)
            {
                return "待同步（0/" + tables.Count.ToString() + "）";
            }

            return "部分待同步（" + ready.ToString() + "/" + tables.Count.ToString() + "）";
        }

        private string BuildTableCacheStatus(ProjectConfigTableSummary table)
        {
            if (_projectConfig.SyncCacheUpToDateTables.Any(t => string.Equals(t, table.TableId, StringComparison.OrdinalIgnoreCase)))
            {
                return "无变化";
            }

            if (_projectConfig.SyncCacheChangedTables.Any(t => string.Equals(t, table.TableId, StringComparison.OrdinalIgnoreCase)))
            {
                return "需要更新";
            }

            if (_projectConfig.SyncCacheMissingCacheTables.Any(t => string.Equals(t, table.TableId, StringComparison.OrdinalIgnoreCase)))
            {
                return "缺少 cache";
            }

            if (_projectConfig.SyncCacheBlockedTables.Any(t => string.Equals(t, table.TableId, StringComparison.OrdinalIgnoreCase)))
            {
                return "同步阻断";
            }

            var projectRoot = FindProjectRoot();
            if (HasCacheFiles(projectRoot, table))
            {
                return "已有 cache";
            }

            if (!string.IsNullOrWhiteSpace(table.BlockingReason))
            {
                return "不能同步：" + table.BlockingReason;
            }

            return "待同步";
        }

        private bool HasCacheFiles(string projectRoot, ProjectConfigTableSummary table)
        {
            var semantic = ResolveCachePath(projectRoot, table.SemanticCachePath, table.TableId, ".semantic.json");
            var hash = ResolveCachePath(projectRoot, table.HashCachePath, table.TableId, ".sha256");
            return File.Exists(semantic) && File.Exists(hash);
        }

        private static string ResolveCachePath(string projectRoot, string configuredPath, string tableId, string suffix)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                return ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, configuredPath);
            }

            return Path.Combine(projectRoot, ".config-sheet-forge", "cache", (tableId ?? "") + suffix);
        }

        private string BuildNoTablesReason()
        {
            if (!_projectConfig.Exists)
            {
                return "未找到 ProjectSettings/*ConfigSheetForge*.json，无法确认当前项目的共享配表配置。";
            }

            if (string.IsNullOrWhiteSpace(_projectConfig.BranchWikiNodeToken) && string.IsNullOrWhiteSpace(_projectConfig.BranchWikiNodeUrl))
            {
                return "暂时没有读到当前分支的在线工作区。下一步：从 main/目标分支初始化当前分支在线表（先预览），而不是做历史 Excel Seed。";
            }

            return "当前分支没有完整在线表记录，或记录缺少表 ID / 在线表链接。下一步：从 main/目标分支初始化当前分支在线表；本地 Excel Seed 只用于历史迁移。";
        }

        private static string BuildSheetLocationText(ProjectConfigTableSummary table)
        {
            var token = FirstNonEmpty(table.SpreadsheetToken, "缺 Sheet token");
            var sheetId = FirstNonEmpty(table.SheetId, "缺工作表 ID");
            var wiki = FirstNonEmpty(table.WikiNodeUrl, table.WikiNodeToken, "未记录 Wiki 节点");
            return token + " / " + sheetId + " / " + wiki;
        }

        private static string BuildLocalStateText(string configPath, string registryPath)
        {
            var hasConfig = File.Exists(configPath);
            var hasRegistry = File.Exists(registryPath);
            if (hasConfig || hasRegistry)
            {
                return "发现本地状态/cache，可忽略、可重建；旧 registry 不参与项目摘要";
            }

            return "未发现本地状态目录；需要时可由同步流程重建";
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return "";
            }

            return hash.Length > 12 ? hash.Substring(0, 12) : hash;
        }

        private string BuildLifecycleInputsJson(string operation, bool dryRun, string finalGateReportPath)
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            AppendJsonProperty(builder, "operation", operation, comma: true);
            AppendJsonProperty(builder, "dryRun", dryRun, comma: true);
            AppendJsonProperty(builder, "gitBranch", FirstNonEmpty(_currentGitBranch, _projectConfig.GitBranch), comma: true);
            AppendJsonProperty(builder, "feishuProfile", _projectConfig.BranchProfile, comma: true);
            AppendJsonProperty(builder, "branchWikiNodeTitle", _projectConfig.BranchWikiNodeTitle, comma: true);
            AppendJsonProperty(builder, "branchWikiNodeUrl", _projectConfig.BranchWikiNodeUrl, comma: true);
            AppendJsonProperty(builder, "branchWikiNodeToken", _projectConfig.BranchWikiNodeToken, comma: true);
            AppendJsonProperty(builder, "tableId", TableIdForOperation(operation), comma: true);
            AppendJsonProperty(builder, "title", _tableName, comma: true);
            AppendJsonProperty(builder, "displayName", _tableName, comma: true);
            AppendJsonProperty(builder, "ownerRole", _ownerRole, comma: true);
            AppendJsonProperty(builder, "schemaChangeSummary", _schemaChangeSummary, comma: true);
            var isNewTable = string.Equals(operation, "new-table", StringComparison.OrdinalIgnoreCase);
            AppendJsonProperty(builder, "excelPath", isNewTable ? EffectiveNewTableExcelPath() : _excelPath, comma: true);
            AppendJsonProperty(builder, "sheetName", isNewTable ? EffectiveNewTableSheetName() : _sheetName, comma: true);
            AppendJsonProperty(builder, "basePath", _basePath, comma: true);
            AppendJsonProperty(builder, "oursPath", _oursPath, comma: true);
            AppendJsonProperty(builder, "theirsPath", _theirsPath, comma: true);
            AppendJsonProperty(builder, "sourceBranch", FirstNonEmpty(_mergeSourceBranch, _currentGitBranch), comma: true);
            AppendJsonProperty(builder, "targetBranch", FirstNonEmpty(_targetBranch, _defaultTargetBranch), comma: true);
            AppendJsonProperty(builder, "targetFeishuProfile", _targetFeishuProfile, comma: true);
            AppendJsonProperty(builder, "targetBranchWikiNodeTitle", _targetBranchWikiNodeTitle, comma: true);
            AppendJsonProperty(builder, "targetBranchWikiNodeUrl", _targetBranchWikiNodeUrl, comma: true);
            AppendJsonProperty(builder, "targetBranchWikiNodeToken", _targetBranchWikiNodeToken, comma: true);
            AppendJsonProperty(builder, "mergeBase", _mergeBase, comma: true);
            AppendJsonProperty(builder, "githubRepository", FirstNonEmpty(_githubRepository, _projectConfig.GithubRepository), comma: true);
            AppendJsonProperty(builder, "prNumber", _prNumber, comma: true);
            AppendJsonProperty(builder, "prUrl", _prUrl, comma: true);
            AppendJsonProperty(builder, "allowPrAutoDetect", _allowPrAutoDetect, comma: true);
            AppendJsonProperty(builder, "mergeReportPath", _mergeReportPath, comma: true);
            AppendJsonProperty(builder, "mergedPath", _mergedPath, comma: true);
            AppendJsonProperty(builder, "writeBackToMain", _writeBackToMain, comma: true);
            AppendJsonProperty(builder, "confirmWriteMain", _writeBackToMain && _confirmWriteMain, comma: true);
            AppendJsonProperty(builder, "confirmApply", ConfirmApplyForOperation(operation), comma: true);
            AppendJsonProperty(builder, "confirmExcelToSoSettingsUpdate", _confirmSeedExcelToSo, comma: true);
            AppendJsonProperty(builder, "targetGitBranch", FirstNonEmpty(_targetBranch, _defaultTargetBranch, "main"), comma: true);
            AppendJsonProperty(builder, "targetProfile", FirstNonEmpty(_targetFeishuProfile, _targetBranch, "main"), comma: true);
            AppendJsonProperty(builder, "sourceMode", string.Equals(operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase) ? "local-xlsx" : "", comma: true);
            AppendJsonProperty(builder, "previewResultPath", string.Equals(operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase) && !dryRun ? _lastResultPath : "", comma: true);
            AppendJsonProperty(builder, "confirmCreateOnlineSheets", _confirmTargetCreateOnlineSheets, comma: true);
            AppendJsonProperty(builder, "confirmRegistryUpsert", _confirmTargetRegistryUpsert, comma: true);
            AppendJsonProperty(builder, "confirmSchemaReviews", _confirmTargetSchemaReviews, comma: true);
            AppendJsonProperty(builder, "confirmWriteLocalCache", _confirmTargetWriteLocalCache, comma: true);
            AppendJsonProperty(builder, "confirmWriteProjectConfig", _confirmTargetWriteProjectConfig, comma: true);
            AppendJsonProperty(builder, "confirmExcelToSoSettings", _confirmTargetExcelToSo, comma: true);
            AppendJsonProperty(builder, "gateReportPath", finalGateReportPath, comma: true);
            AppendJsonArrayProperty(builder, "tableIds", BuildTargetBootstrapTableIdsCsv(), comma: true);
            builder.AppendLine("  \"fields\": [");
            var fields = GetNewTableFieldsForContract();
            for (var i = 0; i < fields.Count; i++)
            {
                builder.Append("    { ");
                builder.Append("\"key\": \"").Append(EscapeJson(fields[i].Key)).Append("\", ");
                builder.Append("\"displayName\": \"").Append(EscapeJson(fields[i].DisplayName)).Append("\", ");
                builder.Append("\"valueKind\": \"").Append(EscapeJson(fields[i].ValueKind)).Append("\", ");
                builder.Append("\"description\": \"").Append(EscapeJson(fields[i].Description)).Append("\" }");
                if (i + 1 < fields.Count)
                {
                    builder.Append(',');
                }

                builder.AppendLine();
            }

            builder.AppendLine("  ]");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private List<ProjectFieldInput> ParseFieldsText()
        {
            return ParseFieldsText(_fieldsText);
        }

        private List<ProjectFieldInput> GetNewTableFieldsForContract()
        {
            EnsureNewTableDefaults();
            var fields = new List<ProjectFieldInput>();
            for (var i = 0; i < _fieldRows.Count; i++)
            {
                var source = _fieldRows[i];
                fields.Add(new ProjectFieldInput
                {
                    Key = source.Key,
                    DisplayName = source.DisplayName,
                    ValueKind = ValueKindForContract(source),
                    Description = source.Description,
                    IsPrimary = source.IsPrimary
                });
            }

            return fields;
        }

        private static void AppendJsonProperty(StringBuilder builder, string key, string value, bool comma)
        {
            builder.Append("  \"").Append(EscapeJson(key)).Append("\": \"").Append(EscapeJson(value)).Append("\"");
            if (comma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static void AppendJsonProperty(StringBuilder builder, string key, bool value, bool comma)
        {
            builder.Append("  \"").Append(EscapeJson(key)).Append("\": ").Append(value ? "true" : "false");
            if (comma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static void AppendNestedJsonProperty(StringBuilder builder, string key, string value, bool comma)
        {
            builder.Append("    \"").Append(EscapeJson(key)).Append("\": \"").Append(EscapeJson(value)).Append("\"");
            if (comma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static void AppendNestedJsonProperty(StringBuilder builder, string key, bool value, bool comma)
        {
            builder.Append("    \"").Append(EscapeJson(key)).Append("\": ").Append(value ? "true" : "false");
            if (comma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static void AppendNestedJsonArrayProperty(StringBuilder builder, string key, string csv, bool comma)
        {
            builder.Append("    \"").Append(EscapeJson(key)).Append("\": [");
            var values = (csv ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("\"").Append(EscapeJson(values[i].Trim())).Append("\"");
            }

            builder.Append("]");
            if (comma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static void AppendJsonArrayProperty(StringBuilder builder, string key, string csv, bool comma)
        {
            builder.Append("  \"").Append(EscapeJson(key)).Append("\": [");
            var values = (csv ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("\"").Append(EscapeJson(values[i].Trim())).Append("\"");
            }

            builder.Append("]");
            if (comma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static string BuildGateReportSummary(string json)
        {
            return GateReportSummaryView.FromJson("", json).DetailText;
        }

        private static string FindProjectRoot()
        {
            var dataPath = Application.dataPath;
            return string.IsNullOrWhiteSpace(dataPath) ? Directory.GetCurrentDirectory() : Directory.GetParent(dataPath).FullName;
        }

        private static string TryRunReadOnlyGit(params string[] args)
        {
            if (args.Length == 2 &&
                string.Equals(args[0], "branch", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[1], "--show-current", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadGitBranch(FindProjectRoot());
            }

            return "";
        }

        private static string TryReadGitBranch(string projectRoot)
        {
            try
            {
                var gitPath = ResolveGitDir(projectRoot);
                var headPath = Path.Combine(gitPath, "HEAD");
                if (!File.Exists(headPath))
                {
                    return "";
                }

                var head = File.ReadAllText(headPath).Trim();
                const string refPrefix = "ref: refs/heads/";
                return head.StartsWith(refPrefix, StringComparison.OrdinalIgnoreCase)
                    ? head.Substring(refPrefix.Length)
                    : "";
            }
            catch
            {
                return "";
            }
        }

        private static string ResolveGitDir(string projectRoot)
        {
            var gitPath = Path.Combine(projectRoot, ".git");
            if (!File.Exists(gitPath))
            {
                return gitPath;
            }

            var text = File.ReadAllText(gitPath).Trim();
            const string gitDirPrefix = "gitdir:";
            if (!text.StartsWith(gitDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return gitPath;
            }

            var gitDir = text.Substring(gitDirPrefix.Length).Trim();
            return Path.IsPathRooted(gitDir)
                ? gitDir
                : Path.GetFullPath(Path.Combine(projectRoot, gitDir.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string TryReadGithubRepository(string projectRoot)
        {
            try
            {
                var configPath = Path.Combine(ResolveGitDir(projectRoot), "config");
                if (!File.Exists(configPath))
                {
                    return "";
                }

                var inOrigin = false;
                foreach (var raw in File.ReadAllLines(configPath))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("[remote ", StringComparison.OrdinalIgnoreCase))
                    {
                        inOrigin = line.IndexOf("\"origin\"", StringComparison.OrdinalIgnoreCase) >= 0;
                        continue;
                    }

                    if (inOrigin && line.StartsWith("url", StringComparison.OrdinalIgnoreCase))
                    {
                        var equals = line.IndexOf('=');
                        return equals >= 0 ? NormalizeGithubRepository(line.Substring(equals + 1).Trim()) : "";
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private static string NormalizeGithubRepository(string remoteUrl)
        {
            var value = (remoteUrl ?? "").Trim();
            const string httpsPrefix = "https://github.com/";
            const string sshPrefix = "git@github.com:";
            if (value.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(httpsPrefix.Length);
            }
            else if (value.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(sshPrefix.Length);
            }

            return value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - 4)
                : value;
        }

        private static List<TargetBranchOption> ReadRemoteBranchOptions(string projectRoot, string prBaseBranch, string defaultTargetBranch, string selectedBranch)
        {
            var branches = new List<TargetBranchOption>();
            var forEachRef = TryRunTool(projectRoot, "git", new[] { "for-each-ref", "--sort=-committerdate", "--format=%(refname:short)|%(committerdate:iso8601-strict)", "refs/remotes/origin" }, 4500);
            if (forEachRef.ExitCode == 0 && !string.IsNullOrWhiteSpace(forEachRef.Stdout))
            {
                foreach (var raw in forEachRef.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var split = raw.IndexOf('|');
                    var name = split >= 0 ? raw.Substring(0, split).Trim() : raw.Trim();
                    var date = split >= 0 ? raw.Substring(split + 1).Trim() : "";
                    if (name.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring("origin/".Length);
                    }

                    if (!string.Equals(name, "HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        AddOrUpdateBranchOption(branches, name, date, "git for-each-ref");
                    }
                }
            }

            if (branches.Count == 0)
            {
                foreach (var name in ReadRemoteBranchNamesFromRefs(projectRoot))
                {
                    AddOrUpdateBranchOption(branches, name, "", "refs");
                }
            }

            AddOrUpdateBranchOption(branches, prBaseBranch, "", "GitHub PR");
            AddOrUpdateBranchOption(branches, defaultTargetBranch, "", "project default");
            AddOrUpdateBranchOption(branches, selectedBranch, "", "current selection");
            MarkAndSortBranchOptions(branches, prBaseBranch, defaultTargetBranch);
            return branches;
        }

        private static List<TargetBranchOption> BuildRemoteBranchOptionsFromRefs(string projectRoot, string prBaseBranch, string defaultTargetBranch, string selectedBranch)
        {
            var branches = new List<TargetBranchOption>();
            foreach (var name in ReadRemoteBranchNamesFromRefs(projectRoot))
            {
                AddOrUpdateBranchOption(branches, name, "", "refs");
            }

            AddOrUpdateBranchOption(branches, prBaseBranch, "", "GitHub PR");
            AddOrUpdateBranchOption(branches, defaultTargetBranch, "", "project default");
            AddOrUpdateBranchOption(branches, selectedBranch, "", "current selection");
            MarkAndSortBranchOptions(branches, prBaseBranch, defaultTargetBranch);
            return branches;
        }

        private static void MarkAndSortBranchOptions(List<TargetBranchOption> branches, string prBaseBranch, string defaultTargetBranch)
        {
            for (var i = 0; i < branches.Count; i++)
            {
                branches[i].IsPrBase = !string.IsNullOrWhiteSpace(prBaseBranch) && string.Equals(branches[i].Name, prBaseBranch, StringComparison.OrdinalIgnoreCase);
                branches[i].IsDefault = !string.IsNullOrWhiteSpace(defaultTargetBranch) && string.Equals(branches[i].Name, defaultTargetBranch, StringComparison.OrdinalIgnoreCase);
            }

            branches.Sort(CompareTargetBranchOptions);
        }

        private static List<string> ReadRemoteBranchNamesFromRefs(string projectRoot)
        {
            var branches = new List<string>();
            try
            {
                var gitDir = ResolveGitDir(projectRoot);
                var originDir = Path.Combine(gitDir, "refs", "remotes", "origin");
                if (Directory.Exists(originDir))
                {
                    foreach (var file in Directory.GetFiles(originDir, "*", SearchOption.AllDirectories))
                    {
                        var rel = file.Substring(originDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                        if (!string.Equals(rel, "HEAD", StringComparison.OrdinalIgnoreCase))
                        {
                            AddUnique(branches, rel);
                        }
                    }
                }

                var packedRefs = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packedRefs))
                {
                    foreach (var raw in File.ReadAllLines(packedRefs))
                    {
                        if (raw.StartsWith("#", StringComparison.Ordinal) || raw.StartsWith("^", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var parts = raw.Split(' ');
                        if (parts.Length >= 2 && parts[1].StartsWith("refs/remotes/origin/", StringComparison.Ordinal))
                        {
                            var name = parts[1].Substring("refs/remotes/origin/".Length);
                            if (!string.Equals(name, "HEAD", StringComparison.OrdinalIgnoreCase))
                            {
                                AddUnique(branches, name);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            branches.Sort(StringComparer.OrdinalIgnoreCase);
            return branches;
        }

        private static void AddOrUpdateBranchOption(List<TargetBranchOption> branches, string name, string lastCommitText, string source)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            for (var i = 0; i < branches.Count; i++)
            {
                if (string.Equals(branches[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(branches[i].LastCommitText) && !string.IsNullOrWhiteSpace(lastCommitText))
                    {
                        branches[i].LastCommitText = lastCommitText;
                    }

                    return;
                }
            }

            branches.Add(new TargetBranchOption
            {
                Name = name,
                LastCommitText = lastCommitText ?? "",
                Source = source ?? ""
            });
        }

        private static int CompareTargetBranchOptions(TargetBranchOption left, TargetBranchOption right)
        {
            var leftRank = BranchRank(left);
            var rightRank = BranchRank(right);
            if (leftRank != rightRank)
            {
                return leftRank.CompareTo(rightRank);
            }

            if (leftRank == 2)
            {
                var dateCompare = string.Compare(right.LastCommitText, left.LastCommitText, StringComparison.OrdinalIgnoreCase);
                if (dateCompare != 0)
                {
                    return dateCompare;
                }
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static int BranchRank(TargetBranchOption option)
        {
            if (option.IsPrBase)
            {
                return 0;
            }

            if (option.IsDefault)
            {
                return 1;
            }

            return string.IsNullOrWhiteSpace(option.LastCommitText) ? 3 : 2;
        }

        private static GitHubPreflightSummary ProbeGitHubPreflight(string projectRoot, string repository, bool allowPrAutoDetect, string installHelpUrl)
        {
            var summary = GitHubPreflightSummary.Unknown();
            summary.InstallHelpUrl = FirstNonEmpty(installHelpUrl, "https://cli.github.com/");
            summary.AllowPrAutoDetect = allowPrAutoDetect;

            var git = TryRunTool(projectRoot, "git", new[] { "--version" }, 2500);
            summary.GitAvailable = git.ExitCode == 0;
            if (!summary.GitAvailable)
            {
                summary.Status = "GitHub PR 识别：git 不可用";
                summary.NextStep = "请先安装 git 或确认 Unity 能访问 git 命令。";
                return summary;
            }

            summary.RemoteIsGitHub = !string.IsNullOrWhiteSpace(repository);
            if (!summary.RemoteIsGitHub)
            {
                summary.Status = "GitHub PR 识别：当前仓库不是 GitHub remote";
                summary.NextStep = "可以手动选择目标分支；PR 自动识别只在 GitHub remote 中启用。";
                return summary;
            }

            if (!allowPrAutoDetect)
            {
                summary.Status = "GitHub PR 识别：项目配置已关闭";
                summary.NextStep = "使用目标分支选择器生成合并预览。";
                return summary;
            }

            var gh = TryRunTool(projectRoot, "gh", new[] { "--version" }, 2500);
            summary.GhAvailable = gh.ExitCode == 0;
            if (!summary.GhAvailable)
            {
                summary.Status = "GitHub PR 识别：未安装 gh，只能手动选择目标分支";
                summary.NextStep = "安装 GitHub CLI 后运行 gh auth login；也可以先手动选择目标分支。";
                return summary;
            }

            var auth = TryRunTool(projectRoot, "gh", new[] { "auth", "status" }, 4500);
            summary.GhAuthenticated = auth.ExitCode == 0;
            if (!summary.GhAuthenticated)
            {
                summary.Status = "GitHub PR 识别：gh 未登录";
                summary.NextStep = "请运行 gh auth login；也可以先手动选择目标分支。";
                return summary;
            }

            summary.Status = "GitHub PR 识别：可用";
            summary.NextStep = "合并页会优先使用当前 GitHub PR 的 base branch。";
            return summary;
        }

        private static MergeContextProbeResult ProbeMergeContext(string projectRoot, string sourceBranch, string targetBranch, bool allowPrAutoDetect, string repository, string defaultTargetBranch, string installHelpUrl)
        {
            var result = new MergeContextProbeResult();
            result.Preflight = ProbeGitHubPreflight(projectRoot, repository, allowPrAutoDetect, installHelpUrl);
            result.BranchOptions = ReadRemoteBranchOptions(projectRoot, "", defaultTargetBranch, targetBranch);
            if (allowPrAutoDetect)
            {
                var gh = TryRunTool(projectRoot, "gh", new[] { "pr", "view", "--json", "number,url,baseRefName,headRefName" }, 4500);
                if (gh.ExitCode == 0 && !string.IsNullOrWhiteSpace(gh.Stdout))
                {
                    result.Number = ExtractJsonString(gh.Stdout, "number");
                    result.Url = ExtractJsonString(gh.Stdout, "url");
                    result.BaseBranch = ExtractJsonString(gh.Stdout, "baseRefName");
                    result.HeadBranch = ExtractJsonString(gh.Stdout, "headRefName");
                    result.Found = !string.IsNullOrWhiteSpace(result.Number) || !string.IsNullOrWhiteSpace(result.Url);
                    targetBranch = FirstNonEmpty(result.BaseBranch, targetBranch);
                    result.BranchOptions = ReadRemoteBranchOptions(projectRoot, result.BaseBranch, defaultTargetBranch, targetBranch);
                }
                else
                {
                    result.Message = HumanizeGhFailure(gh);
                }
            }
            else
            {
                result.Message = "项目配置关闭 PR 自动识别";
            }

            result.MergeBase = TryReadMergeBase(projectRoot, sourceBranch, targetBranch);
            if (string.IsNullOrWhiteSpace(result.Message) && !result.Found)
            {
                result.Message = "未识别到 GitHub PR";
            }

            return result;
        }

        private static string HumanizeGhFailure(ProcessCaptureResult gh)
        {
            var stderr = FirstNonEmpty(gh == null ? "" : gh.Stderr, gh == null ? "" : gh.Stdout);
            var lower = (stderr ?? "").ToLowerInvariant();
            if (gh == null || gh.ExitCode < 0 ||
                lower.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("not recognized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("no such file", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "GitHub PR 识别：未安装 gh，只能手动选择目标分支";
            }

            if (lower.IndexOf("not logged", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("authentication", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "GitHub PR 识别：gh 未登录，请运行 gh auth login";
            }

            return string.IsNullOrWhiteSpace(stderr)
                ? "GitHub PR 识别：当前分支没有可识别 PR"
                : "GitHub PR 识别：" + stderr.Trim();
        }

        private static string TryReadMergeBase(string projectRoot, string sourceBranch, string targetBranch)
        {
            if (string.IsNullOrWhiteSpace(sourceBranch) || string.IsNullOrWhiteSpace(targetBranch))
            {
                return "";
            }

            var result = TryRunTool(projectRoot, "git", new[] { "merge-base", sourceBranch, "origin/" + targetBranch }, 4500);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
            {
                result = TryRunTool(projectRoot, "git", new[] { "merge-base", sourceBranch, targetBranch }, 4500);
            }

            return result.ExitCode == 0 ? result.Stdout.Trim() : "";
        }

        private static ProcessCaptureResult TryRunTool(string workingDirectory, string executable, string[] arguments, int timeoutMilliseconds)
        {
            var result = new ProcessCaptureResult { ExitCode = -1 };
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = ConfigSheetForgeEditorUtility.JoinArguments(arguments),
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        result.Stderr = "无法启动 " + executable;
                        return result;
                    }

                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        result.Stderr = executable + " 超时";
                        return result;
                    }

                    result.ExitCode = process.ExitCode;
                    result.Stdout = process.StandardOutput.ReadToEnd();
                    result.Stderr = process.StandardError.ReadToEnd();
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Stderr = ex.Message;
                return result;
            }
        }

        private static string ExtractJsonString(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = (json ?? "").IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return "";
            }

            var colon = json.IndexOf(':', propertyIndex + property.Length);
            if (colon < 0)
            {
                return "";
            }

            var valueStart = colon + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            if (valueStart < json.Length && json[valueStart] == '"')
            {
                int end;
                return ParseJsonString(json, valueStart, out end);
            }

            var rest = valueStart < json.Length ? json.Substring(valueStart) : "";
            var endIndex = rest.IndexOfAny(new[] { ',', '}', '\r', '\n' });
            return (endIndex >= 0 ? rest.Substring(0, endIndex) : rest).Trim().Trim('"');
        }

        private static string ParseJsonString(string json, int start, out int end)
        {
            var builder = new StringBuilder();
            var escaped = false;
            for (var i = start + 1; i < json.Length; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    builder.Append(Unescape(c));
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    end = i;
                    return builder.ToString();
                }

                builder.Append(c);
            }

            end = json.Length - 1;
            return builder.ToString();
        }

        private static char Unescape(char c)
        {
            switch (c)
            {
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                default:
                    return c;
            }
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
            {
                values.Add(value);
            }
        }

        private static string SlugifyPathToken(string value)
        {
            var builder = new StringBuilder();
            foreach (var c in value ?? "")
            {
                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            }

            var result = builder.ToString().Trim('-');
            while (result.IndexOf("--", StringComparison.Ordinal) >= 0)
            {
                result = result.Replace("--", "-");
            }

            return string.IsNullOrWhiteSpace(result) ? "branch" : result;
        }

        private static string ToProjectRelativePath(string path)
        {
            var root = FindProjectRoot();
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(fullRoot.Length).Replace('\\', '/');
            }

            return path;
        }

        private static void RevealPath(string path)
        {
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        private static WorkbookDocument CreateSmokeWorkbook()
        {
            var workbook = new WorkbookDocument
            {
                ProviderId = "unity",
                SourceId = "editor-smoke",
                SourceTitle = "Unity smoke"
            };

            var sheet = new SheetDocument { Id = "smoke", Name = "Smoke" };
            sheet.Columns.Add(new ColumnDefinition { Key = "id", DisplayName = "ID", ValueKind = "string", Required = true });
            sheet.Columns.Add(new ColumnDefinition { Key = "name", DisplayName = "Name", ValueKind = "string" });

            var row = new RowDocument { StableId = "smoke_001", SourceIndex = 2 };
            row.Cells["id"] = new CellValue { RawText = "smoke_001", NormalizedText = "smoke_001" };
            row.Cells["name"] = new CellValue { RawText = "Shared core", NormalizedText = "Shared core" };
            sheet.Rows.Add(row);
            workbook.Sheets.Add(sheet);
            return workbook;
        }

        private static bool? ExtractBoolean(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = (json ?? "").IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return null;
            }

            var colon = json.IndexOf(':', propertyIndex + property.Length);
            if (colon < 0)
            {
                return null;
            }

            var rest = json.Substring(colon + 1).TrimStart();
            if (rest.StartsWith("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (rest.StartsWith("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }

        private static int? ExtractInt(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = (json ?? "").IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return null;
            }

            var colon = json.IndexOf(':', propertyIndex + property.Length);
            if (colon < 0)
            {
                return null;
            }

            var i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            var start = i;
            if (i < json.Length && (json[i] == '-' || json[i] == '+'))
            {
                i++;
            }

            while (i < json.Length && char.IsDigit(json[i]))
            {
                i++;
            }

            int parsed;
            return int.TryParse(json.Substring(start, i - start), out parsed) ? parsed : (int?)null;
        }

        private static string ExtractString(string json, string propertyName)
        {
            return ExtractJsonString(json, propertyName);
        }

        private static string HumanizeCacheStatus(string cacheStatus)
        {
            if (string.Equals(cacheStatus, "upToDate", StringComparison.OrdinalIgnoreCase))
            {
                return "无变化，未重写 cache";
            }

            if (string.Equals(cacheStatus, "needsUpdate", StringComparison.OrdinalIgnoreCase))
            {
                return "有变化，需要写入本地 cache";
            }

            if (string.Equals(cacheStatus, "missingCache", StringComparison.OrdinalIgnoreCase))
            {
                return "缺少本地 cache";
            }

            if (string.Equals(cacheStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                return "被阻断";
            }

            return FirstNonEmpty(cacheStatus, "未知");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        private static List<string> ExtractStringArray(string json, string propertyName)
        {
            var values = new List<string>();
            var property = "\"" + propertyName + "\"";
            var propertyIndex = json.IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return values;
            }

            var arrayStart = json.IndexOf('[', propertyIndex);
            var arrayEnd = json.IndexOf(']', arrayStart < 0 ? propertyIndex : arrayStart);
            if (arrayStart < 0 || arrayEnd < 0)
            {
                return values;
            }

            for (var i = arrayStart + 1; i < arrayEnd; i++)
            {
                if (json[i] != '"')
                {
                    continue;
                }

                var builder = new StringBuilder();
                var escaped = false;
                for (var j = i + 1; j < arrayEnd; j++)
                {
                    var c = json[j];
                    if (escaped)
                    {
                        builder.Append(c);
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        values.Add(builder.ToString());
                        i = j;
                        break;
                    }

                    builder.Append(c);
                }
            }

            return values;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }

    public static class ConfigSheetForgeEditorApi
    {
        public static void OpenStatusWindow()
        {
            ConfigSheetForgeWindow.OpenStatusWindow();
        }

        public static void OpenNewTableWizard()
        {
            ConfigSheetForgeWindow.OpenNewTableWizard();
        }

        public static void OpenSeedFromLocalXlsx()
        {
            ConfigSheetForgeWindow.OpenSeedFromLocalXlsx();
        }

        public static void OpenSyncCache()
        {
            ConfigSheetForgeWindow.OpenSyncCache();
        }

        public static void OpenCompareMerge()
        {
            ConfigSheetForgeWindow.OpenCompareMerge();
        }

        public static void OpenPrGate()
        {
            ConfigSheetForgeWindow.OpenPrGate();
        }
    }

    internal sealed class ProcessCaptureResult
    {
        public int ExitCode { get; set; }
        public string Stdout { get; set; } = "";
        public string Stderr { get; set; } = "";

        public string Render(string command)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Command: " + command);
            builder.AppendLine("ExitCode: " + ExitCode);
            if (!string.IsNullOrWhiteSpace(Stdout))
            {
                builder.AppendLine();
                builder.AppendLine(Stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(Stderr))
            {
                builder.AppendLine();
                builder.AppendLine("stderr:");
                builder.AppendLine(Stderr.TrimEnd());
            }

            return builder.ToString();
        }
    }

    internal sealed class GateFailureView
    {
        public string Reason { get; set; } = "";
        public string NextStep { get; set; } = "";
        public int Priority { get; set; }
        public bool IsMergeReviewMissing { get; set; }
        public bool IsSchemaReviewMissing { get; set; }
        public bool IsWaiverProblem { get; set; }
        public bool IsRegistryMigrationNeeded { get; set; }

        public static GateFailureView FromMessage(string message)
        {
            message = message ?? "";
            var mentionsLarkCli = ContainsAny(message, "lark-cli", "CONFIG_SHEET_FORGE_LARK_CLI", "LARK_CLI_PATH", "ApplicationName='lark-cli'");
            var looksMissingTool = ContainsAny(message, "没有找到", "找不到", "无法运行", "not found", "not recognized", "No such file", "The system cannot find", "系统找不到");
            if (mentionsLarkCli && looksMissingTool)
            {
                return new GateFailureView
                {
                    Reason = "本机没有找到 lark-cli，无法用 bot 身份读取飞书注册中心",
                    NextStep = "请重启 Unity，或配置 toolkit.larkCliPath / CONFIG_SHEET_FORGE_LARK_CLI；也可以确认 %APPDATA%\\npm 下已安装 lark-cli。",
                    Priority = 5
                };
            }

            if (ContainsAny(message, "doctor failed", "lark-cli doctor", "doctor 失败"))
            {
                return new GateFailureView
                {
                    Reason = "lark-cli doctor 未通过",
                    NextStep = "请在终端运行 lark-cli doctor 和 lark-cli auth status，按提示修复登录、token 或版本问题后重试。",
                    Priority = 6
                };
            }

            if (ContainsAny(message, "状态选项", "registry-migrate", "注册中心字段需要迁移", "单选字段缺少"))
            {
                return new GateFailureView
                {
                    Reason = "注册中心字段需要迁移",
                    NextStep = "先运行 registry-migrate --only review-status-options --dry-run 查看窄计划；确认只补状态选项后，再执行 --yes。",
                    Priority = 8,
                    IsRegistryMigrationNeeded = true
                };
            }

            if (ContainsAny(message, "MergeReviews", "merge review", "合并审查", "合并预览"))
            {
                return new GateFailureView
                {
                    Reason = "缺少 MergeReviews 合并审查记录",
                    NextStep = "去“合并”页生成合并预览，通过后点“提交合并审查记录”，再重新运行 PR 检查。",
                    Priority = 10,
                    IsMergeReviewMissing = true
                };
            }

            if (ContainsAny(message, "SchemaReviews", "schema review", "Schema", "结构审查"))
            {
                return new GateFailureView
                {
                    Reason = "Schema review 未完成",
                    NextStep = "请负责人完成 SchemaReviews 审查，或补充变更说明后重新运行 PR 检查。",
                    Priority = 20,
                    IsSchemaReviewMissing = true
                };
            }

            if (ContainsAny(message, "waiver", "Waivers", "豁免", "过期"))
            {
                return new GateFailureView
                {
                    Reason = "waiver 已过期或无效",
                    NextStep = "请更新豁免记录，或移除豁免后按正常审查流程重新检查。",
                    Priority = 30,
                    IsWaiverProblem = true
                };
            }

            if (ContainsAny(message, "missing_scope", "missing scope", "scope", "scopes", "缺少 scope"))
            {
                return new GateFailureView
                {
                    Reason = "bot 缺少读取飞书数据所需的 scope",
                    NextStep = "请让管理员给飞书应用补充 Base/Sheet/Wiki 相关 scope，并重新运行 lark-cli doctor 后再跑 PR 检查。",
                    Priority = 40
                };
            }

            if (ContainsAny(message, "user fallback", "allow-user-fallback", "严格模式", "strict bot"))
            {
                return new GateFailureView
                {
                    Reason = "当前是 strict bot 模式，不会用 user 身份绕过 hard gate",
                    NextStep = "请修复 bot 的 scope 或资源授权；如只想本地诊断，可在高级诊断里用 user 身份单独排查，但结果不能直接作为 CI gate 通过依据。",
                    Priority = 42
                };
            }

            if (ContainsAny(message, "permission", "forbidden", "权限", "resource", "not shared", "未共享", "无权", "access denied"))
            {
                return new GateFailureView
                {
                    Reason = "bot 有命令可用，但 Base/Sheet/Wiki 资源没有授权给 bot",
                    NextStep = "请把在线注册中心、Wiki 节点或 Sheet 共享给飞书应用/bot，然后重新运行 PR 检查。",
                    Priority = 45
                };
            }

            if (ContainsAny(message, "BranchBindings", "分支绑定", "profile"))
            {
                return new GateFailureView
                {
                    Reason = "当前分支的在线工作区未绑定或绑定冲突",
                    NextStep = "如果这是新功能分支，请先从 main/PR base 派生当前分支在线表；如果提示重复绑定，请让配置负责人清理重复 BranchBindings。",
                    Priority = 50
                };
            }

            if (ContainsAny(message, "ConfigSheets", "Sheet token", "SpreadsheetToken", "在线表"))
            {
                return new GateFailureView
                {
                    Reason = "当前分支没有在线表记录",
                    NextStep = "如果这是新功能分支，请先从 main/PR base 派生当前分支在线表；只有历史 Excel 迁移才使用“本地 Excel Seed”。",
                    Priority = 60
                };
            }

            return new GateFailureView
            {
                Reason = string.IsNullOrWhiteSpace(message) ? "PR 检查未通过" : message,
                NextStep = "按上面的原因修正后重新运行 PR 检查；需要细节时打开“输出”页查看日志。",
                Priority = 100
            };
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class GateReportSummaryView
    {
        public string Path { get; set; } = "";
        public string ShortText { get; set; } = "";
        public string DetailText { get; set; } = "";
        public bool HasReport { get; set; }
        public bool? Passed { get; set; }
        public bool Waived { get; set; }
        public string GateState { get; set; } = "";
        public string WaiverSummary { get; set; } = "";
        public List<string> WaivedFailures { get; } = new List<string>();
        public List<GateFailureView> Failures { get; } = new List<GateFailureView>();

        public static GateReportSummaryView NotFound(string path)
        {
            return new GateReportSummaryView
            {
                Path = path ?? "",
                HasReport = false,
                ShortText = "未生成 PR 检查",
                DetailText = string.IsNullOrWhiteSpace(path)
                    ? "还没有生成 pr-gate-report。"
                    : "还没有找到最近一次 pr-gate-report：" + path
            };
        }

        public static GateReportSummaryView FromJson(string path, string json)
        {
            json = json ?? "";
            var passed = ExtractBoolean(json, "passed");
            var waived = ExtractBoolean(json, "waived") == true ||
                         string.Equals(ExtractString(json, "gateState"), "waived", StringComparison.OrdinalIgnoreCase);
            var failureMessages = ExtractStringArray(json, "humanReadableFailures");
            var waivedFailureMessages = ExtractStringArray(json, "waivedFailures");
            var failures = new List<GateFailureView>();
            foreach (var failure in failureMessages)
            {
                failures.Add(GateFailureView.FromMessage(failure));
            }
            if (ExtractBoolean(json, "canReadRegistry") == false)
            {
                AddFailureIfMissing(failures, ExtractString(json, "registryMessage"));
            }

            if (ExtractBoolean(json, "canReadSheets") == false)
            {
                AddFailureIfMissing(failures, ExtractString(json, "sheetsMessage"));
            }
            failures.Sort((left, right) => left.Priority.CompareTo(right.Priority));

            var builder = new StringBuilder();
            if (waived)
            {
                builder.AppendLine("已由配置负责人 waiver 临时放行。");
                builder.AppendLine(BuildWaiverSummary(json));
            }
            else if (failures.Count > 0)
            {
                foreach (var failure in failures)
                {
                    builder.AppendLine("原因：" + failure.Reason);
                    builder.AppendLine("下一步：" + failure.NextStep);
                }
            }
            else if (passed.HasValue && passed.Value)
            {
                builder.AppendLine("最近一次 PR gate 已通过。");
            }
            else
            {
                builder.AppendLine("PR gate 未通过，但报告没有给出明确原因。请重新运行 PR 检查或查看详细日志。");
            }

            var view = new GateReportSummaryView
            {
                Path = path ?? "",
                HasReport = true,
                Passed = passed,
                Waived = waived,
                GateState = ExtractString(json, "gateState"),
                WaiverSummary = BuildWaiverSummary(json),
                ShortText = BuildShortText(passed, failures, waived),
                DetailText = builder.ToString().TrimEnd()
            };
            view.Failures.AddRange(failures);
            view.WaivedFailures.AddRange(waivedFailureMessages);
            return view;
        }

        private static void AddFailureIfMissing(List<GateFailureView> failures, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var view = GateFailureView.FromMessage(message);
            for (var i = 0; i < failures.Count; i++)
            {
                if (string.Equals(failures[i].Reason, view.Reason, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            failures.Add(view);
        }

        private static string BuildShortText(bool? passed, List<GateFailureView> failures)
        {
            return BuildShortText(passed, failures, false);
        }

        private static string BuildShortText(bool? passed, List<GateFailureView> failures, bool waived)
        {
            if (waived && failures.Count == 0)
            {
                return "临时放行";
            }

            if (passed.HasValue && passed.Value && failures.Count == 0)
            {
                return "通过";
            }

            if (failures.Count > 0)
            {
                return "未通过：" + failures[0].Reason;
            }

            if (passed.HasValue && !passed.Value)
            {
                return "未通过：需要查看报告";
            }

            return "状态未知";
        }

        private static string BuildWaiverSummary(string json)
        {
            var role = ExtractNestedString(json, "waiver", "approvedByRole");
            var expiresAt = ExtractNestedString(json, "waiver", "expiresAt");
            var reason = ExtractNestedString(json, "waiver", "reason");
            var recordId = ExtractNestedString(json, "waiver", "recordId");
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(role))
            {
                parts.Add("批准角色：" + role);
            }

            if (!string.IsNullOrWhiteSpace(expiresAt))
            {
                parts.Add("过期时间：" + expiresAt);
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                parts.Add("原因：" + reason);
            }

            if (!string.IsNullOrWhiteSpace(recordId))
            {
                parts.Add("record_id：" + recordId);
            }

            return parts.Count == 0 ? "报告里有有效 waiver 记录。" : string.Join("；", parts);
        }

        private static bool? ExtractBoolean(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = json.IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return null;
            }

            var colon = json.IndexOf(':', propertyIndex + property.Length);
            if (colon < 0)
            {
                return null;
            }

            var rest = json.Substring(colon + 1).TrimStart();
            if (rest.StartsWith("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (rest.StartsWith("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }

        private static int? ExtractInt(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = (json ?? "").IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return null;
            }

            var colon = json.IndexOf(':', propertyIndex + property.Length);
            if (colon < 0)
            {
                return null;
            }

            var i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            var start = i;
            if (i < json.Length && (json[i] == '-' || json[i] == '+'))
            {
                i++;
            }

            while (i < json.Length && char.IsDigit(json[i]))
            {
                i++;
            }

            int parsed;
            return int.TryParse(json.Substring(start, i - start), out parsed) ? parsed : (int?)null;
        }

        private static List<string> ExtractStringArray(string json, string propertyName)
        {
            var values = new List<string>();
            var property = "\"" + propertyName + "\"";
            var propertyIndex = json.IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return values;
            }

            var arrayStart = json.IndexOf('[', propertyIndex);
            if (arrayStart < 0)
            {
                return values;
            }

            var arrayEnd = FindArrayEnd(json, arrayStart);
            if (arrayEnd < 0)
            {
                return values;
            }

            for (var i = arrayStart + 1; i < arrayEnd; i++)
            {
                if (json[i] != '"')
                {
                    continue;
                }

                int stringEnd;
                values.Add(ParseJsonString(json, i, out stringEnd));
                i = stringEnd;
            }

            return values;
        }

        private static string ExtractString(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = json.IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return "";
            }

            var colon = json.IndexOf(':', propertyIndex + property.Length);
            if (colon < 0)
            {
                return "";
            }

            var quote = colon + 1;
            while (quote < json.Length && char.IsWhiteSpace(json[quote]))
            {
                quote++;
            }

            if (quote >= json.Length || json[quote] != '"')
            {
                return "";
            }

            int stringEnd;
            return ParseJsonString(json, quote, out stringEnd);
        }

        private static string ExtractNestedString(string json, string objectName, string propertyName)
        {
            var objectProperty = "\"" + objectName + "\"";
            var objectIndex = json.IndexOf(objectProperty, StringComparison.OrdinalIgnoreCase);
            if (objectIndex < 0)
            {
                return "";
            }

            var objectStart = json.IndexOf('{', objectIndex + objectProperty.Length);
            if (objectStart < 0)
            {
                return "";
            }

            var objectEnd = FindObjectEnd(json, objectStart);
            if (objectEnd < 0)
            {
                return "";
            }

            return ExtractString(json.Substring(objectStart, objectEnd - objectStart + 1), propertyName);
        }

        private static int FindArrayEnd(string json, int start)
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < json.Length; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static int FindObjectEnd(string json, int start)
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < json.Length; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static string ParseJsonString(string json, int start, out int end)
        {
            var builder = new StringBuilder();
            var escaped = false;
            for (var i = start + 1; i < json.Length; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    builder.Append(Unescape(c));
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    end = i;
                    return builder.ToString();
                }

                builder.Append(c);
            }

            end = json.Length - 1;
            return builder.ToString();
        }

        private static char Unescape(char c)
        {
            switch (c)
            {
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                default:
                    return c;
            }
        }
    }

    public sealed class ConfigSheetForgeBackgroundJob
    {
        private readonly object _sync = new object();
        private readonly Queue<string> _pendingLines = new Queue<string>();
        private readonly Func<ConfigSheetForgeBackgroundJob, Task> _body;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private Process _currentProcess;
        private bool _started;
        private volatile bool _isFinished;

        private ConfigSheetForgeBackgroundJob(Func<ConfigSheetForgeBackgroundJob, Task> body)
        {
            _body = body;
        }

        public string Operation { get; private set; } = "";
        public bool DryRun { get; private set; }
        public string CommandLine { get; private set; } = "";
        public string StartOutput { get; private set; } = "";
        public string Status { get; private set; } = "等待启动";
        public string ResultPath { get; private set; } = "";
        public string LifecycleDirectory { get; private set; } = "";
        public string FinalSummary { get; private set; } = "";
        public string InputFingerprint { get; set; } = "";
        public bool Success { get; private set; }
        public bool WasCancelled { get; private set; }
        public bool RefreshReadonlyStatusOnComplete { get; private set; }
        public DateTime StartedAtUtc { get; private set; }
        public double ElapsedSeconds
        {
            get
            {
                if (StartedAtUtc == default(DateTime))
                {
                    return 0;
                }

                return Math.Max(0, (DateTime.UtcNow - StartedAtUtc).TotalSeconds);
            }
        }

        public bool IsFinished
        {
            get { return _isFinished; }
        }

        public static ConfigSheetForgeBackgroundJob CreateSingleProcess(
            string operation,
            bool dryRun,
            string commandLine,
            string executable,
            string[] arguments,
            string workingDirectory,
            string resultPath,
            string lifecycleDirectory,
            bool refreshReadonlyStatusOnComplete,
            ProjectConfigSummary projectConfig)
        {
            var job = new ConfigSheetForgeBackgroundJob(async self =>
            {
                self.SetStatus(BuildCliStartStatus(commandLine));
                var result = await self.RunProcessAsync(
                    executable,
                    arguments,
                    workingDirectory,
                    commandLine,
                    "正在运行 " + operation,
                    projectConfig).ConfigureAwait(false);
                self.Success = result.ExitCode == 0;
                self.SetStatus(self.Success ? "完成" : "失败");
                self.FinalSummary = BuildSummaryFromResult(
                    operation,
                    dryRun,
                    resultPath,
                    File.Exists(resultPath) ? File.ReadAllText(resultPath) : "",
                    result.Stdout + Environment.NewLine + result.Stderr,
                    BuildBranchNodeFallback(projectConfig),
                    result.ExitCode,
                    wasCancelled: false);
            });
            job.Operation = operation;
            job.DryRun = dryRun;
            job.CommandLine = commandLine ?? "";
            job.ResultPath = resultPath ?? "";
            job.LifecycleDirectory = lifecycleDirectory ?? "";
            job.RefreshReadonlyStatusOnComplete = refreshReadonlyStatusOnComplete;
            job.StartOutput = BuildStartOutput(operation, dryRun, commandLine);
            job.FinalSummary = job.BuildLiveSummary();
            return job;
        }

        public static ConfigSheetForgeBackgroundJob CreateLifecycle(
            string operation,
            bool dryRun,
            string projectRoot,
            string requestPath,
            string inputsPath,
            string resultPath,
            string finalGateReportPath,
            string adapterExecutable,
            string[] adapterArguments,
            string adapterCommandLine,
            string cliExecutable,
            string[] cliArguments,
            string cliCommandLine,
            string fullCommandLine,
            ProjectConfigSummary projectConfig)
        {
            var lifecycleDirectory = Path.GetDirectoryName(resultPath) ?? "";
            var job = new ConfigSheetForgeBackgroundJob(async self =>
            {
                self.SetStatus("正在生成 contract");
                self.AppendLine("Inputs: " + inputsPath);
                var adapterResult = await self.RunProcessAsync(
                    adapterExecutable,
                    adapterArguments,
                    projectRoot,
                    adapterCommandLine,
                    "正在生成 contract",
                    projectConfig).ConfigureAwait(false);
                if (adapterResult.ExitCode != 0)
                {
                    self.Success = false;
                    self.SetStatus("失败");
                    self.FinalSummary = BuildFailureSummary(operation, dryRun, resultPath, "adapter 没有成功生成 contract，请展开详细日志处理。");
                    return;
                }

                if (!File.Exists(requestPath))
                {
                    self.Success = false;
                    self.SetStatus("失败");
                    self.AppendLine("adapter 运行成功，但没有生成 contract request: " + requestPath);
                    self.FinalSummary = BuildFailureSummary(operation, dryRun, resultPath, "adapter 没有生成 contract request。请检查项目 config 的 contractArgs/contractRequestPath 设置。");
                    return;
                }

                self.AppendLine("Contract request: " + requestPath);
                self.SetStatus(BuildCliStartStatus(cliCommandLine));
                var applyResult = await self.RunProcessAsync(
                    cliExecutable,
                    cliArguments,
                    projectRoot,
                    cliCommandLine,
                    "正在运行 apply-contract",
                    projectConfig).ConfigureAwait(false);

                var resultJson = File.Exists(resultPath) ? File.ReadAllText(resultPath) : "";
                if (!string.IsNullOrWhiteSpace(resultJson))
                {
                    self.AppendLine("Lifecycle result: " + resultPath);
                    self.AppendLine(resultJson);
                }

                if (string.Equals(operation, "pr-gate-report", StringComparison.OrdinalIgnoreCase))
                {
                    self.AppendLine("Final gate report: " + finalGateReportPath);
                    if (File.Exists(finalGateReportPath))
                    {
                        self.AppendLine(GateReportSummaryView.FromJson(finalGateReportPath, File.ReadAllText(finalGateReportPath)).DetailText);
                    }
                }

                self.Success = applyResult.ExitCode == 0 && !ResultJsonDeclaresFailure(operation, resultJson);
                self.SetStatus(self.Success ? "完成" : "失败");
                self.FinalSummary = BuildSummaryFromResult(
                    operation,
                    dryRun,
                    resultPath,
                    resultJson,
                    applyResult.Stdout + Environment.NewLine + applyResult.Stderr,
                    BuildBranchNodeFallback(projectConfig),
                    applyResult.ExitCode,
                    wasCancelled: false);
            });
            job.Operation = operation;
            job.DryRun = dryRun;
            job.CommandLine = fullCommandLine ?? "";
            job.ResultPath = resultPath ?? "";
            job.LifecycleDirectory = lifecycleDirectory;
            job.RefreshReadonlyStatusOnComplete = true;
            job.StartOutput = BuildStartOutput(operation, dryRun, fullCommandLine);
            job.FinalSummary = job.BuildLiveSummary();
            return job;
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            StartedAtUtc = DateTime.UtcNow;
            Task.Run(async () =>
            {
                try
                {
                    await _body(this).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    Success = false;
                    SetStatus("已取消");
                    FinalSummary = "已取消，未写本地 cache。" + Environment.NewLine +
                                   "操作: " + Operation + Environment.NewLine +
                                   "模式: " + (DryRun ? "dry-run" : "apply") + Environment.NewLine +
                                   "Result path: " + FirstNonEmpty(ResultPath, "未生成");
                    AppendLine("已取消，未写本地 cache。");
                }
                catch (Exception ex)
                {
                    Success = false;
                    SetStatus("失败");
                    FinalSummary = BuildFailureSummary(Operation, DryRun, ResultPath, ex.Message);
                    AppendLine("错误: " + ex.Message);
                }
                finally
                {
                    _isFinished = true;
                }
            });
        }

        public void Cancel(string message)
        {
            WasCancelled = true;
            SetStatus("正在取消");
            AppendLine(message);
            _cancellation.Cancel();
            KillCurrentProcessTree();
        }

        public List<string> DrainLines()
        {
            var lines = new List<string>();
            lock (_sync)
            {
                while (_pendingLines.Count > 0)
                {
                    lines.Add(_pendingLines.Dequeue());
                }
            }

            return lines;
        }

        public string BuildLiveSummary()
        {
            var builder = new StringBuilder();
            builder.AppendLine("状态: " + Status);
            builder.AppendLine("操作: " + Operation);
            builder.AppendLine("模式: " + (DryRun ? "dry-run / 生成预览" : "apply / 执行写入"));
            if (ElapsedSeconds > 0)
            {
                builder.AppendLine("已用时间: " + Math.Floor(ElapsedSeconds).ToString("0") + " 秒");
            }
            if (DryRun)
            {
                builder.AppendLine("安全性: 只生成预览，不写飞书、不改本地 cache、不改 ProjectSettings。");
                builder.AppendLine("是否写本地 cache: 否");
            }
            else
            {
                builder.AppendLine("安全性: apply 会读取在线 Sheet、导出 xlsx、三方一致后才可能更新本地 cache。");
            }

            if (!string.IsNullOrWhiteSpace(ResultPath))
            {
                builder.AppendLine("Result path: " + ResultPath);
            }

            return builder.ToString().TrimEnd();
        }

        private async Task<ProcessCaptureResult> RunProcessAsync(
            string executable,
            string[] arguments,
            string workingDirectory,
            string commandLine,
            string runningStatus,
            ProjectConfigSummary projectConfig)
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            SetStatus(runningStatus);
            AppendLine("");
            AppendLine("== " + runningStatus + " ==");
            AppendLine("Command: " + commandLine);

            var result = new ProcessCaptureResult();
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var stdoutDone = new ManualResetEventSlim(false);
            var stderrDone = new ManualResetEventSlim(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = ConfigSheetForgeEditorUtility.JoinArguments(arguments ?? new string[0]),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            ConfigSheetForgeEditorUtility.ConfigureToolProcessEnvironment(startInfo, projectConfig);

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        stdoutDone.Set();
                        return;
                    }

                    lock (stdout)
                    {
                        stdout.AppendLine(e.Data);
                    }

                    UpdateStatusFromLine(e.Data);
                    AppendLine(e.Data);
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        stderrDone.Set();
                        return;
                    }

                    lock (stderr)
                    {
                        stderr.AppendLine(e.Data);
                    }

                    UpdateStatusFromLine(e.Data);
                    AppendLine("stderr: " + e.Data);
                };

                try
                {
                    if (!process.Start())
                    {
                        result.ExitCode = -1;
                        result.Stderr = "Could not start process.";
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    result.ExitCode = -1;
                    result.Stderr = ex.Message;
                    AppendLine(ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(commandLine, ex.Message));
                    return result;
                }

                _currentProcess = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                while (!process.HasExited)
                {
                    if (_cancellation.Token.WaitHandle.WaitOne(100))
                    {
                        KillCurrentProcessTree();
                        throw new OperationCanceledException();
                    }
                }

                stdoutDone.Wait(2000);
                stderrDone.Wait(2000);
                result.ExitCode = process.ExitCode;
                lock (stdout)
                {
                    result.Stdout = stdout.ToString();
                }

                lock (stderr)
                {
                    result.Stderr = stderr.ToString();
                }

                AppendLine("ExitCode: " + result.ExitCode);
                _currentProcess = null;
                return result;
            }
        }

        private void KillCurrentProcessTree()
        {
            var process = _currentProcess;
            if (process == null || process.HasExited)
            {
                return;
            }

            try
            {
                if (Path.DirectorySeparatorChar == '\\')
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = "/PID " + process.Id.ToString() + " /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else
                {
                    process.Kill();
                }
            }
            catch
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                }
            }
        }

        private void UpdateStatusFromLine(string line)
        {
            line = line ?? "";
            if (line.IndexOf("正在读取在线 Sheet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("read online", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("sheets +read", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetStatus("正在读取在线 Sheet");
            }
            else if (line.IndexOf("正在导出 xlsx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     line.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetStatus("正在导出 xlsx");
            }
            else if (line.IndexOf("三方", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     line.IndexOf("triangulation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetStatus("正在三方一致性检查");
            }
            else if (line.IndexOf("hash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     line.IndexOf("cache updated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     line.IndexOf("无变化", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetStatus("正在 hash gate");
            }
        }

        private void SetStatus(string status)
        {
            Status = status ?? "";
        }

        private void AppendLine(string line)
        {
            lock (_sync)
            {
                _pendingLines.Enqueue(line ?? "");
            }
        }

        private static string BuildStartOutput(string operation, bool dryRun, string commandLine)
        {
            var builder = new StringBuilder();
            builder.AppendLine("已清空上一次输出，后台任务已启动。");
            builder.AppendLine("操作: " + operation);
            builder.AppendLine("模式: " + (dryRun ? "dry-run / 生成预览" : "apply / 执行写入"));
            if (dryRun)
            {
                builder.AppendLine("dry-run：只生成预览，不写飞书、不改本地 cache、不改 ProjectSettings。");
            }
            else
            {
                builder.AppendLine("apply：已通过 UI 确认，三方一致和 hash gate 通过后才可能写本地 cache。");
            }

            builder.AppendLine("正在启动后台进程，Unity 窗口可以继续滚动和切换 tab。");
            builder.AppendLine("Command:");
            builder.AppendLine(commandLine ?? "");
            return builder.ToString();
        }

        private static string BuildCliStartStatus(string commandLine)
        {
            commandLine = commandLine ?? "";
            if (commandLine.IndexOf("dotnet run", StringComparison.OrdinalIgnoreCase) >= 0 ||
                commandLine.IndexOf(" --project ", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "正在启动 config-sheet-forge CLI（源码 fallback，首次运行可能较慢）";
            }

            return "正在启动 config-sheet-forge CLI";
        }

        private static string BuildSummaryFromResult(string operation, bool dryRun, string resultPath, string resultJson, string processOutput, string branchFallback, int exitCode, bool wasCancelled)
        {
            if (wasCancelled)
            {
                return "已取消，未写本地 cache。";
            }

            var success = ExtractBoolean(resultJson, "success");
            var finalSuccess = success.HasValue ? success.Value : exitCode == 0;
            var plannedActions = CountArrayObjects(resultJson, "actions");
            var branchNode = FirstNonEmpty(
                ExtractString(resultJson, "branchWikiNodeTitle"),
                ExtractString(resultJson, "wikiNodeTitle"),
                ExtractString(resultJson, "nodeTitle"),
                branchFallback);
            var failures = ExtractStringArray(resultJson, "humanReadableFailures");
            if (string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase) &&
                finalSuccess &&
                !string.IsNullOrWhiteSpace(resultJson) &&
                resultJson.IndexOf("\"merge.inputs.prepare\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                finalSuccess = false;
                failures.Add("合并预览没有生成可审查的表范围、source/target 工作区和 merge report 路径。请重新生成合并预览；如果仍失败，请检查在线注册中心的 BranchBindings/ConfigSheets。");
            }

            var builder = new StringBuilder();
            builder.AppendLine(finalSuccess ? "成功" : "失败");
            builder.AppendLine("操作: " + operation);
            builder.AppendLine("模式: " + (dryRun ? "dry-run / 生成预览" : "apply / 执行写入"));
            builder.AppendLine("planned action 数量: " + (plannedActions >= 0 ? plannedActions.ToString() : "未记录"));
            builder.AppendLine("branch node: " + FirstNonEmpty(branchNode, "未记录"));
            builder.AppendLine("result path: " + FirstNonEmpty(resultPath, "未生成"));
            var requestFingerprint = ExtractString(resultJson, "requestFingerprint");
            if (!string.IsNullOrWhiteSpace(requestFingerprint))
            {
                builder.AppendLine("request fingerprint: " + requestFingerprint);
            }

            if (string.Equals(operation, "sync-cache", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation, "sync-from-online-sheet", StringComparison.OrdinalIgnoreCase))
            {
                var cacheStatus = ExtractString(resultJson, "cacheStatus");
                if (!string.IsNullOrWhiteSpace(cacheStatus))
                {
                    builder.AppendLine("cache 状态: " + HumanizeCacheStatus(cacheStatus));
                }

                var changed = ExtractStringArray(resultJson, "changedTables");
                var missing = ExtractStringArray(resultJson, "missingCacheTables");
                var upToDate = ExtractStringArray(resultJson, "upToDateTables");
                if (changed.Count > 0)
                {
                    builder.AppendLine("需要更新: " + string.Join(", ", changed));
                }

                if (missing.Count > 0)
                {
                    builder.AppendLine("缺少 cache: " + string.Join(", ", missing));
                }

                if (upToDate.Count > 0)
                {
                    builder.AppendLine("无变化: " + upToDate.Count.ToString() + " 张");
                }
            }

            if (string.Equals(operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase) && !dryRun)
            {
                var postflightPassed = ExtractString(resultJson, "postflightPassed");
                if (resultJson.IndexOf("\"target_branch.bootstrap.postflight\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    string.Equals(postflightPassed, "true", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendLine("postflight: 已通过");
                }
                else if (resultJson.IndexOf("\"target_branch.bootstrap.postflight\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    builder.AppendLine("postflight: 请展开详细日志查看");
                }
            }

            builder.AppendLine("是否写本地 cache: " + BuildCacheWriteText(dryRun, processOutput));
            if (failures.Count > 0)
            {
                builder.AppendLine("需要处理:");
                foreach (var failure in failures)
                {
                    var view = GateFailureView.FromMessage(failure);
                    builder.AppendLine("- " + view.Reason);
                    builder.AppendLine("  下一步: " + view.NextStep);
                }
            }
            else if (!finalSuccess && string.IsNullOrWhiteSpace(resultJson))
            {
                builder.AppendLine("原因: 进程退出码 " + exitCode.ToString() + "。请展开详细日志查看命令、stderr 和下一步。");
            }

            return builder.ToString().TrimEnd();
        }

        private static string HumanizeCacheStatus(string cacheStatus)
        {
            if (string.Equals(cacheStatus, "upToDate", StringComparison.OrdinalIgnoreCase))
            {
                return "无变化，未重写 cache";
            }

            if (string.Equals(cacheStatus, "needsUpdate", StringComparison.OrdinalIgnoreCase))
            {
                return "有变化，需要写入本地 cache";
            }

            if (string.Equals(cacheStatus, "missingCache", StringComparison.OrdinalIgnoreCase))
            {
                return "缺少本地 cache";
            }

            if (string.Equals(cacheStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                return "被阻断";
            }

            return cacheStatus;
        }

        private static string BuildFailureSummary(string operation, bool dryRun, string resultPath, string reason)
        {
            return "失败" + Environment.NewLine +
                   "操作: " + operation + Environment.NewLine +
                   "模式: " + (dryRun ? "dry-run / 生成预览" : "apply / 执行写入") + Environment.NewLine +
                   "planned action 数量: 未生成" + Environment.NewLine +
                   "branch node: 未生成" + Environment.NewLine +
                   "result path: " + FirstNonEmpty(resultPath, "未生成") + Environment.NewLine +
                   "是否写本地 cache: 否" + Environment.NewLine +
                   "原因: " + reason;
        }

        private static string BuildCacheWriteText(bool dryRun, string processOutput)
        {
            if (dryRun)
            {
                return "否";
            }

            processOutput = processOutput ?? "";
            if (processOutput.IndexOf("cache updated", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "是（内容变化后已更新）";
            }

            if (processOutput.IndexOf("无变化", StringComparison.OrdinalIgnoreCase) >= 0 ||
                processOutput.IndexOf("cache unchanged", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "否（无变化，未重写 cache）";
            }

            return "请展开详细日志确认";
        }

        private static bool ResultJsonDeclaresFailure(string operation, string resultJson)
        {
            var success = ExtractBoolean(resultJson, "success");
            if (success.HasValue && !success.Value)
            {
                return true;
            }

            return string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase) &&
                   success.HasValue &&
                   success.Value &&
                   !string.IsNullOrWhiteSpace(resultJson) &&
                   resultJson.IndexOf("\"merge.inputs.prepare\"", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string BuildBranchNodeFallback(ProjectConfigSummary projectConfig)
        {
            if (projectConfig == null)
            {
                return "";
            }

            return FirstNonEmpty(projectConfig.BranchWikiNodeTitle, projectConfig.BranchWikiNodeUrl, projectConfig.BranchWikiNodeToken);
        }

        private static bool? ExtractBoolean(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = (json ?? "").IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return null;
            }

            var colon = json.IndexOf(':', propertyIndex + property.Length);
            if (colon < 0)
            {
                return null;
            }

            var rest = json.Substring(colon + 1).TrimStart();
            if (rest.StartsWith("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (rest.StartsWith("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }

        private static string ExtractString(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = (json ?? "").IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return "";
            }

            var colon = json.IndexOf(':', propertyIndex + property.Length);
            if (colon < 0)
            {
                return "";
            }

            var quote = json.IndexOf('"', colon + 1);
            if (quote < 0)
            {
                return "";
            }

            int end;
            return ParseJsonString(json, quote, out end);
        }

        private static List<string> ExtractStringArray(string json, string propertyName)
        {
            var values = new List<string>();
            var arrayStart = FindArrayStart(json, propertyName);
            if (arrayStart < 0)
            {
                return values;
            }

            var arrayEnd = FindArrayEnd(json, arrayStart);
            if (arrayEnd < 0)
            {
                return values;
            }

            for (var i = arrayStart + 1; i < arrayEnd; i++)
            {
                if (json[i] != '"')
                {
                    continue;
                }

                int stringEnd;
                values.Add(ParseJsonString(json, i, out stringEnd));
                i = stringEnd;
            }

            return values;
        }

        private static int CountArrayObjects(string json, string propertyName)
        {
            var arrayStart = FindArrayStart(json, propertyName);
            if (arrayStart < 0)
            {
                return -1;
            }

            var arrayEnd = FindArrayEnd(json, arrayStart);
            if (arrayEnd < 0)
            {
                return -1;
            }

            var count = 0;
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = arrayStart + 1; i < arrayEnd; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                    {
                        count++;
                    }

                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                }
            }

            return count;
        }

        private static int FindArrayStart(string json, string propertyName)
        {
            var property = "\"" + propertyName + "\"";
            var propertyIndex = (json ?? "").IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return -1;
            }

            return json.IndexOf('[', propertyIndex + property.Length);
        }

        private static int FindArrayEnd(string json, int start)
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < json.Length; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static string ParseJsonString(string json, int start, out int end)
        {
            var builder = new StringBuilder();
            var escaped = false;
            for (var i = start + 1; i < json.Length; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    builder.Append(Unescape(c));
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    end = i;
                    return builder.ToString();
                }

                builder.Append(c);
            }

            end = json.Length - 1;
            return builder.ToString();
        }

        private static char Unescape(char c)
        {
            switch (c)
            {
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                default:
                    return c;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }
    }

    internal sealed class ProjectFieldInput
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ValueKind { get; set; } = "string";
        public string Description { get; set; } = "";
        public bool IsPrimary { get; set; }
        public List<string> EnumValues { get; set; } = new List<string>();
    }

    internal sealed class NewTableValidationResult
    {
        public bool Valid { get; set; }
        public string Message { get; set; } = "";
    }

    internal sealed class FieldTypeSpec
    {
        public string Canonical { get; set; } = "";
        public string Label { get; set; } = "";
    }

    internal sealed class TargetBranchOption
    {
        public string Name { get; set; } = "";
        public string LastCommitText { get; set; } = "";
        public string Source { get; set; } = "";
        public bool IsPrBase { get; set; }
        public bool IsDefault { get; set; }
    }

    internal sealed class GitHubPreflightSummary
    {
        public bool AllowPrAutoDetect { get; set; }
        public bool GitAvailable { get; set; }
        public bool RemoteIsGitHub { get; set; }
        public bool GhAvailable { get; set; }
        public bool GhAuthenticated { get; set; }
        public string Status { get; set; } = "";
        public string NextStep { get; set; } = "";
        public string InstallHelpUrl { get; set; } = "https://cli.github.com/";

        public bool IsReady
        {
            get { return GitAvailable && RemoteIsGitHub && (!AllowPrAutoDetect || GhAvailable && GhAuthenticated); }
        }

        public static GitHubPreflightSummary Unknown()
        {
            return new GitHubPreflightSummary
            {
                Status = "GitHub PR 识别：待刷新",
                NextStep = "刷新合并上下文后会检查 git、gh 和当前仓库 remote。"
            };
        }

        public static GitHubPreflightSummary Pending()
        {
            return new GitHubPreflightSummary
            {
                Status = "GitHub PR 识别：后台检查中",
                NextStep = "可以继续切换页面或编辑；检查完成后会自动更新目标分支来源。"
            };
        }
    }

    internal sealed class MergeContextProbeResult
    {
        public GitHubPreflightSummary Preflight { get; set; } = GitHubPreflightSummary.Unknown();
        public List<TargetBranchOption> BranchOptions { get; set; } = new List<TargetBranchOption>();
        public bool Found { get; set; }
        public string Number { get; set; } = "";
        public string Url { get; set; } = "";
        public string BaseBranch { get; set; } = "";
        public string HeadBranch { get; set; } = "";
        public string MergeBase { get; set; } = "";
        public string Message { get; set; } = "";
    }

    internal sealed class ExcelToSoSettingsPreflight
    {
        public bool Ready { get; set; }
        public bool HasOldExcelReferences { get; set; }
        public bool CanUpdateToCache { get; set; }
        public int TypeRow { get; set; } = 1;
        public string ShortStatus { get; set; } = "";
        public string Message { get; set; } = "";
        public string SettingsPath { get; set; } = "";
    }

    internal sealed class ExcelToSoSettingsEntry
    {
        public string ExcelName { get; set; } = "";
    }

    [Serializable]
    internal sealed class ExcelToSoSettingsDocument
    {
        public ExcelToSoGlobalConfigs configs = new ExcelToSoGlobalConfigs();
        public ExcelToSoSetting[] excels = new ExcelToSoSetting[0];
    }

    [Serializable]
    internal sealed class ExcelToSoGlobalConfigs
    {
        public int field_row = 0;
        public int type_row = 1;
        public int data_from_row = 2;
    }

    [Serializable]
    internal sealed class ExcelToSoSetting
    {
        public string excel_name = "";
        public string script_directory = "Assets";
        public string asset_directory = "Assets";
        public string name_space = "";
        public bool use_hash_string = false;
        public bool hide_asset_properties = true;
        public bool use_public_items_getter = false;
        public bool compress_color_into_int = true;
        public bool treat_unknown_types_as_enum = false;
        public bool generate_tostring_method = true;
        public ExcelToSoSlave[] slaves = new ExcelToSoSlave[0];
    }

    [Serializable]
    internal sealed class ExcelToSoSlave
    {
        public string excel_name = "";
        public string asset_directory = "Assets";
    }

    internal sealed class ExcelToSoUnityImportSession
    {
        private readonly List<ExcelToSoImportItem> _items;
        private readonly Queue<string> _pendingLines = new Queue<string>();
        private readonly List<ExcelToSoSingleImportResult> _results = new List<ExcelToSoSingleImportResult>();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _index;
        private bool _cancelRequested;
        private string _cancelReason = "";

        public ExcelToSoUnityImportSession(List<ExcelToSoImportItem> items)
        {
            _items = items == null ? new List<ExcelToSoImportItem>() : new List<ExcelToSoImportItem>(items);
            Status = "准备导入 Unity asset";
            Append("准备导入 Unity 配表资产，共 " + _items.Count.ToString() + " 张表。");
        }

        public bool IsFinished { get; private set; }
        public bool Success { get; private set; }
        public string Status { get; private set; }
        public double ElapsedSeconds { get { return _stopwatch.Elapsed.TotalSeconds; } }
        public string ProgressText { get { return Math.Min(_index + 1, Math.Max(1, _items.Count)).ToString() + " / " + Math.Max(1, _items.Count).ToString(); } }

        public void Cancel(string reason)
        {
            _cancelRequested = true;
            _cancelReason = string.IsNullOrWhiteSpace(reason) ? "已取消。" : reason;
            Append(_cancelReason);
        }

        public bool Tick()
        {
            if (IsFinished)
            {
                return false;
            }

            if (_cancelRequested)
            {
                Finish(false, _cancelReason);
                return true;
            }

            if (_items.Count == 0)
            {
                Finish(false, "没有可导入的 cache xlsx。");
                return true;
            }

            if (_index >= _items.Count)
            {
                var failed = _results.Count(result => !result.Success);
                Finish(failed == 0, failed == 0 ? "Unity 配表资产导入完成。" : "Unity 配表资产导入完成，但有失败表。");
                return true;
            }

            var item = _items[_index];
            Status = "正在导入 " + FirstNonEmpty(item.DisplayName, item.TableId);
            Append("导入: " + item.TableId + " -> " + item.CacheXlsxPath);
            var result = ConfigSheetForgeExcelToSoImporter.ImportExcelPath(item.TableId, item.CacheXlsxPath);
            _results.Add(result);
            if (result.Success)
            {
                Append("成功: " + item.TableId + " -> " + FirstNonEmpty(result.AssetPath, "Unity asset"));
            }
            else
            {
                Append("失败: " + item.TableId);
                foreach (var error in result.Errors)
                {
                    Append("  " + error);
                }
            }

            foreach (var warning in result.Warnings)
            {
                Append("警告: " + item.TableId + " - " + warning);
            }

            _index++;
            return true;
        }

        public IEnumerable<string> DrainLines()
        {
            while (_pendingLines.Count > 0)
            {
                yield return _pendingLines.Dequeue();
            }
        }

        public string BuildSummary()
        {
            var imported = _results.Count(result => result.Success);
            var failed = _results.Count(result => !result.Success);
            return "正在导入 Unity 配表资产" + Environment.NewLine +
                   "进度: " + Math.Min(_index + 1, Math.Max(1, _items.Count)).ToString() + " / " + Math.Max(1, _items.Count).ToString() + Environment.NewLine +
                   "已成功: " + imported.ToString() + "，失败: " + failed.ToString() + Environment.NewLine +
                   "写入边界: 只写 Unity asset，不写飞书/registry/main/旧 Excel。";
        }

        public string BuildFinalSummary()
        {
            var imported = _results.Where(result => result.Success).Select(result => result.TableId).ToList();
            var failed = _results.Where(result => !result.Success).ToList();
            var builder = new StringBuilder();
            builder.AppendLine(failed.Count == 0 ? "Unity 配表资产导入成功" : "Unity 配表资产导入有失败");
            builder.AppendLine("成功: " + imported.Count.ToString() + " 张" + (imported.Count > 0 ? "（" + string.Join(", ", imported) + "）" : ""));
            builder.AppendLine("失败: " + failed.Count.ToString() + " 张" + (failed.Count > 0 ? "（" + string.Join(", ", failed.Select(result => result.TableId)) + "）" : ""));
            builder.AppendLine("写入边界: 只写 Unity asset；没有写飞书、registry、main、ProjectSettings 或旧 Excel。");
            if (failed.Count > 0)
            {
                builder.AppendLine("下一步: 展开结果查看失败表；通常需要检查 ExcelToSO settings 是否指向 cache、生成的 C# 类型是否已编译、字段是否匹配。");
            }
            else
            {
                builder.AppendLine("下一步: 运行 PR 检查。");
            }

            return builder.ToString().TrimEnd();
        }

        private void Finish(bool success, string message)
        {
            Success = success;
            Status = message;
            IsFinished = true;
            _stopwatch.Stop();
            Append(message);
        }

        private void Append(string line)
        {
            _pendingLines.Enqueue(line ?? "");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }
    }
}
