using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        private const int StatusTab = 0;
        private const int TablesTab = 1;
        private const int MergeTab = 2;
        private const int GateTab = 3;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private const string BottomOutputExpandedPrefKey = "ConfigSheetForge.Unity.BottomOutputExpanded";
        private const string BottomOutputHeightPrefKey = "ConfigSheetForge.Unity.BottomOutputHeight";
        private const string OnboardingDismissedPrefKey = "ConfigSheetForge.Unity.OnboardingDismissed";
        private const float CollapsedOutputBarHeight = 34f;
        private const float MinBottomDrawerHeight = 220f;
        private const float DefaultBottomDrawerHeight = 260f;

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
        private string _mergeContextStatus = "";
        private List<string> _targetBranchOptions = new List<string>();
        private Task<MergeContextProbeResult> _mergeContextTask;
        private bool _writeBackToMain;
        private bool _confirmWriteMain;
        private bool _confirmSeedApply;
        private bool _confirmSeedExcelToSo;
        private bool _confirmSyncApply;
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
        private bool _isResizingOutputPanel;
        private float _bottomOutputHeight = 260f;
        private string _output = "";
        private string _resultSummary = "";
        private string _lastCommand = "";
        private string _lastResultPath = "";
        private string _lastLifecycleDir = "";
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
        private string _activeJobStatus = "";
        private Vector2 _mainScroll;
        private Vector2 _outputScroll;

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
            window.minSize = new Vector2(640, 520);
            window._selectedTab = tab;
            window.RefreshReadonlyStatus();
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            _showBottomOutput = EditorPrefs.GetBool(BottomOutputExpandedPrefKey, false);
            _bottomOutputHeight = EditorPrefs.HasKey(BottomOutputHeightPrefKey) ? EditorPrefs.GetFloat(BottomOutputHeightPrefKey, DefaultBottomDrawerHeight) : 0f;
            _showOnboarding = !EditorPrefs.GetBool(OnboardingDismissedPrefKey, false);
            RefreshReadonlyStatus();
            _resultSummary = "配表 Source of Truth 窗口已打开。" + Environment.NewLine +
                             "这里只刷新本地状态，不会下载、不导出、不改文件。" + Environment.NewLine +
                             "主流程只保留刷新状态、预览同步计划、运行 PR 检查。";
            _output = "";
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (IsJobRunning)
            {
                _activeJob.Cancel("窗口已关闭，已取消，未写本地 cache。");
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
            if (GUILayout.Button(new GUIContent("教程", "打开 5 分钟入门、项目文档或飞书入口。"), GUILayout.Width(64)))
            {
                ShowHelpMenu();
            }

            if (GUILayout.Button(new GUIContent("复制 UPM", "复制通过 Unity Package Manager 安装此包的 Git URL。"), GUILayout.Width(88)))
            {
                EditorGUIUtility.systemCopyBuffer = "https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.4.12";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("飞书在线 Sheet 是 Source of Truth，本地 Excel 只是兼容缓存。", EditorStyles.miniLabel);
            GUILayout.Space(4);
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
            if (!IsJobRunning)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(FirstNonEmpty(_activeJobStatus, "后台任务运行中"), EditorStyles.wordWrappedLabel);
            if (GUILayout.Button(new GUIContent("取消", "终止当前 adapter / CLI / lark-cli 进程树。"), GUILayout.Width(80)))
            {
                CancelActiveJob();
            }
            EditorGUILayout.EndHorizontal();
            if (_activeJob != null && _activeJob.DryRun)
            {
                EditorGUILayout.HelpBox("dry-run：只生成预览，不写飞书、不改本地 cache、不改 ProjectSettings。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("任务在后台运行；窗口仍可滚动、复制命令和切换 tab。运行中会禁用相关执行按钮，避免重复点击。", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTargetBranchField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("目标分支", "target/base branch。没有 PR 时默认 main，可从远端分支列表选择。"), GUILayout.Width(110));
            var selected = Math.Max(0, _targetBranchOptions.IndexOf(_targetBranch));
            var previous = _targetBranch;
            if (_targetBranchOptions.Count > 0)
            {
                selected = EditorGUILayout.Popup(selected, _targetBranchOptions.ToArray());
                _targetBranch = _targetBranchOptions[Mathf.Clamp(selected, 0, _targetBranchOptions.Count - 1)];
            }
            else
            {
                _targetBranch = EditorGUILayout.TextField(_targetBranch);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.Equals(previous, _targetBranch, StringComparison.OrdinalIgnoreCase))
            {
                _mergeContextTask = null;
                _prNumber = "";
                _prUrl = "";
                RefreshMergeContext();
            }
        }

        private string BuildPrText()
        {
            if (!string.IsNullOrWhiteSpace(_prNumber))
            {
                return "#" + _prNumber + " -> " + FirstNonEmpty(_prUrl, "未记录 URL");
            }

            return _allowPrAutoDetect ? "未识别到 PR，使用目标分支 fallback" : "项目配置关闭 PR 自动识别";
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

        private string BuildRecommendationText(string projectRoot)
        {
            var next = BuildNextStepText(projectRoot);
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
            var next = BuildNextStepText(projectRoot);
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
            RefreshReadonlyStatus();
            _lastCommand = "只读刷新状态";
            SetImmediateOutput("已刷新状态。没有下载、导出或写入任何文件。", "");
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
                RefreshReadonlyStatus();
                _lastCommand = "只读刷新状态";
                _resultSummary = "已刷新状态。没有下载、导出或写入任何文件。";
                _output = "";
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
                _showSyncSection = EditorGUILayout.Foldout(_showSyncSection, "同步当前分支 cache", true);
                if (_showSyncSection)
                {
                    DrawCurrentBranchTables(compact: false);
                    DrawSyncCacheModeCard();
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

                if (DrawJobButton(new GUIContent("确认写回 main", "危险操作：必须勾选申请写回、确认写回，并且最近一次合并预览成功。"), mergeWriteReady, GUILayout.Height(28)))
                {
                    if (EditorUtility.DisplayDialog("确认写回 main", "将按项目 adapter 的 compare-merge contract 执行写回。请确认预览已经通过。", "确认执行", "取消"))
                    {
                        RunProjectLifecycle("compare-merge", dryRun: false);
                    }
                }
                EditorGUILayout.EndHorizontal();
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
            EditorGUILayout.EndVertical();
        }

        private void DrawGateReportCards()
        {
            if (_gateReportSummary == null || !_gateReportSummary.HasReport)
            {
                EditorGUILayout.HelpBox("还没有生成 PR 检查报告。点击“生成 PR gate report”后，这里会显示是否可以合并。", MessageType.Info);
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

        private static void DrawGateFailureCard(GateFailureView failure)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("原因：" + failure.Reason, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("下一步：" + failure.NextStep, EditorStyles.wordWrappedLabel);
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
            EditorGUILayout.LabelField("预览同步计划只读取在线注册中心并生成计划，不写飞书、不改本地 cache、不改 ProjectSettings。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("写入本地 cache 只有在预览通过、勾选确认并通过弹窗后才会执行；它会读取在线 Sheet、导出 xlsx、完成三方一致性检查和 hash gate 后更新本地 cache。", EditorStyles.wordWrappedLabel);
            _confirmSyncApply = EditorGUILayout.Toggle(new GUIContent("确认写入本地 cache", "允许更新 .config-sheet-forge/cache 和 excel-cache；必须先预览通过。"), _confirmSyncApply);
            var syncApplyReady = _confirmSyncApply && LastPreviewPassed("sync-cache");
            if (_confirmSyncApply && !LastPreviewPassed("sync-cache"))
            {
                EditorGUILayout.HelpBox("请先预览同步计划，并确认预览成功后再写入本地 cache。", MessageType.Warning);
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

        private void DrawProjectNewTableInputs()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("新表输入", EditorStyles.boldLabel);
            _tableId = EditorGUILayout.TextField(new GUIContent("配表ID", "必填。稳定机器 key，例如 ItemsData 或 MonsterData。"), _tableId);
            if (string.IsNullOrWhiteSpace(_tableId))
            {
                EditorGUILayout.LabelField("例：ItemsData、MonsterData。不要使用临时中文名。", EditorStyles.wordWrappedMiniLabel);
            }

            _tableName = EditorGUILayout.TextField(new GUIContent("显示名称", "必填。策划在窗口和报告里看到的名称。"), _tableName);
            if (string.IsNullOrWhiteSpace(_tableName))
            {
                EditorGUILayout.LabelField("例：道具表、怪物表。", EditorStyles.wordWrappedMiniLabel);
            }

            _ownerRole = EditorGUILayout.TextField(new GUIContent("负责人角色", "例如 configOwner；为空时由项目 adapter 默认。"), _ownerRole);
            _schemaChangeSummary = EditorGUILayout.TextField(new GUIContent("Schema 变更说明", "写入 inputs.schemaChangeSummary，供 SchemaReviews reason 使用。"), _schemaChangeSummary);
            _excelPath = EditorGUILayout.TextField(new GUIContent("本地 Excel 路径", "可选；写入 inputs.excelPath。"), _excelPath);
            _sheetName = EditorGUILayout.TextField(new GUIContent("工作表名", "可选；写入 inputs.sheetName。"), _sheetName);
            EditorGUILayout.LabelField(new GUIContent("字段示例（可编辑）", "每行：字段 key | 显示名 | 类型 | 说明。复杂字段写入 inputs JSON，不走长 inline JSON 参数。"));
            EditorGUILayout.HelpBox("示例含义：id 是唯一 ID 字段，name 是显示名称字段。可以先保留示例做 dry-run，再按项目规范补字段。", MessageType.Info);
            DrawFieldTemplatePreview();
            _showFieldTemplateEditor = EditorGUILayout.Foldout(_showFieldTemplateEditor, "编辑字段模板", true);
            if (_showFieldTemplateEditor)
            {
                EditorGUILayout.LabelField("每行一个字段：字段 key | 显示名 | 类型 | 说明。", EditorStyles.wordWrappedMiniLabel);
                _fieldsText = EditorGUILayout.TextArea(_fieldsText, GUILayout.MinHeight(72));
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawFieldTemplatePreview()
        {
            var fields = ParseFieldsText();
            if (fields.Count == 0)
            {
                EditorGUILayout.HelpBox("还没有字段模板。至少保留一个稳定 ID 字段，例如 id / ID / string。", MessageType.Warning);
                return;
            }

            for (var i = 0; i < fields.Count; i++)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(FirstNonEmpty(fields[i].Key, "未命名"), EditorStyles.boldLabel, GUILayout.Width(110));
                EditorGUILayout.LabelField(FirstNonEmpty(fields[i].DisplayName, fields[i].Key, "未命名"), GUILayout.Width(120));
                EditorGUILayout.LabelField(FirstNonEmpty(fields[i].ValueKind, "string"), GUILayout.Width(72));
                EditorGUILayout.LabelField(FirstNonEmpty(fields[i].Description, "无说明"), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawProjectNewTableActions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("新建配表操作", EditorStyles.boldLabel);
            var hasRequiredFields = HasNewTableRequiredFields();
            if (!hasRequiredFields)
            {
                EditorGUILayout.HelpBox("请先填写配表ID和显示名称。", MessageType.Warning);
            }

            _confirmNewTableApply = EditorGUILayout.Toggle(new GUIContent("确认创建在线表并登记", "允许创建/复用在线 Sheet，并登记到 Base。必须先预览通过。"), _confirmNewTableApply);
            var applyReady = hasRequiredFields && _confirmNewTableApply && LastPreviewPassed("new-table");
            if (_confirmNewTableApply && !LastPreviewPassed("new-table"))
            {
                EditorGUILayout.HelpBox("请先预览新建配表，并确认预览成功后再创建在线表并登记。", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("预览新建配表", "生成 new-table dry-run，预览创建 Sheet、登记 Base 和 SchemaReviews，不写飞书。"), hasRequiredFields, GUILayout.Height(28)))
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
            return !string.IsNullOrWhiteSpace(_tableId) &&
                   !string.IsNullOrWhiteSpace(_tableName);
        }

        private void DrawProjectMergeInputs()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("PR 合并上下文", EditorStyles.boldLabel);
            DrawReadonlyRow("当前分支", FirstNonEmpty(_mergeSourceBranch, _currentGitBranch, "未知"), "source/head branch。");
            DrawTargetBranchField();
            DrawReadonlyRow("GitHub PR", BuildPrText(), "如果 gh 可用且 allowPrAutoDetect=true，会尝试自动识别当前分支的 PR。");
            DrawReadonlyRow("当前状态", BuildMergeStatusText(), "合并预览/写回的当前状态。");
            DrawReadonlyRow("下一步", BuildMergeNextStepText(), "按当前状态推荐下一步操作。");
            DrawReadonlyRow("比较范围", BuildMergeTablesText(), "默认比较当前分支所有在线表；单表模式在高级选项中。");
            if (!string.IsNullOrWhiteSpace(_mergeContextStatus))
            {
                EditorGUILayout.LabelField(_mergeContextStatus, EditorStyles.wordWrappedMiniLabel);
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
                _mergeTableId = EditorGUILayout.TextField(new GUIContent("只比较单表", "可选；留空时 adapter 可比较当前分支所有在线表。"), _mergeTableId);
                _mergeReportPath = EditorGUILayout.TextField(new GUIContent("报告路径", "写入 inputs.mergeReportPath。"), _mergeReportPath);
                _mergedPath = EditorGUILayout.TextField(new GUIContent("合并结果路径", "写入 inputs.mergedPath。"), _mergedPath);
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
                _output = "";
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
            _output = details ?? "";
            SetBottomOutputExpanded(false, persist: false);
            _showDetailedLogs = false;
            Repaint();
        }

        private bool IsJobRunning
        {
            get { return _activeJob != null && !_activeJob.IsFinished; }
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
            if (_activeJob == null)
            {
                if (mergeChanged)
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
                _lastCompletedOperation = _activeJob.Operation;
                _lastCompletedDryRun = _activeJob.DryRun;
                _lastCompletedSuccess = _activeJob.Success;
                _lastCompletedInputFingerprint = _activeJob.InputFingerprint;
                SetBottomOutputExpanded(false, persist: false);
                _showDetailedLogs = false;
                _activeJob = null;
                if (refresh)
                {
                    RefreshReadonlyStatus();
                }
                changed = true;
            }

            if (changed || mergeChanged)
            {
                Repaint();
            }
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
            if (string.IsNullOrEmpty(_output))
            {
                _output = line ?? "";
            }
            else
            {
                if (!_output.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                {
                    _output += Environment.NewLine;
                }

                _output += line ?? "";
            }

            if (!_output.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                _output += Environment.NewLine;
            }
        }

        private void StartBackgroundJob(ConfigSheetForgeBackgroundJob job)
        {
            if (job == null)
            {
                return;
            }

            if (IsJobRunning)
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
            _output = job.StartOutput;
            SetBottomOutputExpanded(false, persist: false);
            _showDetailedLogs = false;
            job.Start();
            Repaint();
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
            GUI.enabled = oldEnabled && enabled && !IsJobRunning;
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
                builder.Append("excel=").Append(_excelPath).Append('|');
                builder.Append("sheet=").Append(_sheetName).Append('|');
                builder.Append("fields=").Append(_fieldsText);
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
                branchBound ? "已找到当前分支对应的在线工作区。" : "先预览同步计划，确认是否还没绑定或权限不足。",
                branchBound ? WorkflowStatusKind.Ok : WorkflowStatusKind.Warning));
            cards.Add(new WorkflowStatusCard(
                "在线表",
                BuildOnlineTableStatus(),
                BuildOnlineTableStatusDetail(),
                onlineReadable ? WorkflowStatusKind.Ok : WorkflowStatusKind.Warning));
            cards.Add(new WorkflowStatusCard(
                "本地 cache",
                cacheFresh ? "本地 cache 新鲜" : "待同步",
                cacheFresh ? "当前分支表已有本地 semantic/hash cache。" : "先预览同步计划；apply 只在确认后写本地 cache。",
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

            RefreshReadonlyStatus();
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

        private void CopyProjectLifecycleAdapterCommand(string operation, bool dryRun)
        {
            RefreshReadonlyStatus();
            var projectRoot = FindProjectRoot();
            var requestPath = Path.Combine(projectRoot, "Temp", "ConfigSheetForge", "unity-lifecycle", operation + ".contract.json");
            var inputsPath = Path.Combine(projectRoot, "Temp", "ConfigSheetForge", "unity-lifecycle", operation + ".inputs.json");
            var adapter = ConfigSheetForgeEditorUtility.CreateProjectLifecycleCommand(_projectConfig, projectRoot, operation, requestPath, inputsPath, dryRun);
            EditorGUIUtility.systemCopyBuffer = adapter.ToCommandLine();
        }

        private void RefreshReadonlyStatus()
        {
            var projectRoot = FindProjectRoot();
            _currentGitBranch = TryRunReadOnlyGit("branch", "--show-current");
            _projectConfig = ConfigSheetForgeEditorUtility.LoadProjectConfigSummary(projectRoot, _currentGitBranch);
            _cliInvocation = ConfigSheetForgeEditorUtility.ResolveCoreCli(_projectConfig, projectRoot, _cliPath);
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
            _allowPrAutoDetect = _projectConfig.AllowPrAutoDetect;
            _targetBranchOptions = ReadRemoteBranches(projectRoot);
            if (!_targetBranchOptions.Contains(_targetBranch))
            {
                _targetBranchOptions.Insert(0, _targetBranch);
            }

            _mergeBase = "正在计算";
            _mergeContextStatus = "已按当前分支和目标分支推导合并上下文；不需要手动选择 base/ours/theirs 文件。";
            if (_mergeContextTask == null)
            {
                var source = _mergeSourceBranch;
                var target = _targetBranch;
                var allowPr = _allowPrAutoDetect;
                _mergeContextTask = Task.Run(() => ProbeMergeContext(projectRoot, source, target, allowPr));
                _mergeContextStatus = allowPr
                    ? "正在尝试识别当前分支的 GitHub PR 并计算 merge-base；gh 不可用时会使用目标分支 fallback。"
                    : "正在按目标分支计算 merge-base。";
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
                if (result != null && result.Found)
                {
                    _prNumber = result.Number;
                    _prUrl = result.Url;
                    _targetBranch = FirstNonEmpty(result.BaseBranch, _targetBranch, _defaultTargetBranch);
                    _mergeBase = FirstNonEmpty(result.MergeBase, "未计算到共同祖先");
                    UpdateMergeInputPaths();
                    UpdateMergeWorkspaceContext();
                    _mergeContextStatus = "已识别 GitHub PR #" + _prNumber + "，目标分支为 " + _targetBranch + "。";
                }
                else if (result != null && !string.IsNullOrWhiteSpace(result.Message))
                {
                    _mergeBase = FirstNonEmpty(result.MergeBase, "未计算到共同祖先");
                    _mergeContextStatus = result.Message + "；使用目标分支 fallback。";
                }
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
            return !string.IsNullOrWhiteSpace(_projectConfig.BranchWikiNodeToken) ||
                   !string.IsNullOrWhiteSpace(_projectConfig.BranchWikiNodeUrl) ||
                   !string.IsNullOrWhiteSpace(_projectConfig.BranchWikiNodeTitle);
        }

        private bool OnlineTablesReadable()
        {
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

            if (!BranchLooksBound() || !OnlineTablesReadable())
            {
                return "预览同步计划";
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
            if (string.Equals(next, "运行 PR 检查", StringComparison.OrdinalIgnoreCase))
            {
                return "运行 PR 检查";
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
                RefreshReadonlyStatus();
                SetImmediateOutput("已刷新状态。当前看起来可以提交 PR。", "");
                return;
            }

            RunSyncCache(apply: false);
        }

        private string BuildCacheOverviewText(string projectRoot)
        {
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
                return "暂时没有读到当前分支的在线工作区。可能是未绑定分支、权限不足，或最近还没有预览同步计划。下一步：先点“预览同步计划”。";
            }

            return "当前分支没有在线表记录，或记录缺少表 ID / 在线表链接。下一步：确认这个分支是否已经 Seed；如果要迁移旧 Excel，请展开“本地 Excel Seed”。";
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
            AppendJsonProperty(builder, "excelPath", _excelPath, comma: true);
            AppendJsonProperty(builder, "sheetName", _sheetName, comma: true);
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
            AppendJsonProperty(builder, "gateReportPath", finalGateReportPath, comma: true);
            builder.AppendLine("  \"fields\": [");
            var fields = ParseFieldsText();
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
            var fields = new List<ProjectFieldInput>();
            foreach (var rawLine in (_fieldsText ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawLine.Split('|');
                var key = parts.Length > 0 ? parts[0].Trim() : "";
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                fields.Add(new ProjectFieldInput
                {
                    Key = key,
                    DisplayName = parts.Length > 1 ? parts[1].Trim() : key,
                    ValueKind = parts.Length > 2 ? parts[2].Trim() : "string",
                    Description = parts.Length > 3 ? parts[3].Trim() : ""
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

        private static List<string> ReadRemoteBranches(string projectRoot)
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

        private static MergeContextProbeResult ProbeMergeContext(string projectRoot, string sourceBranch, string targetBranch, bool allowPrAutoDetect)
        {
            var result = new MergeContextProbeResult();
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
                }
                else
                {
                    result.Message = string.IsNullOrWhiteSpace(gh.Stderr) ? "gh 不可用或当前分支没有可识别 PR" : "gh 未返回 PR：" + gh.Stderr.Trim();
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

        public static GateFailureView FromMessage(string message)
        {
            message = message ?? "";
            if (ContainsAny(message, "MergeReviews", "merge review", "合并审查", "合并预览"))
            {
                return new GateFailureView
                {
                    Reason = "缺少 MergeReviews 合并审查记录",
                    NextStep = "去“合并”页生成合并预览，通过后补审查记录，再重新运行 PR 检查。",
                    Priority = 10
                };
            }

            if (ContainsAny(message, "SchemaReviews", "schema review", "Schema", "结构审查"))
            {
                return new GateFailureView
                {
                    Reason = "Schema review 未完成",
                    NextStep = "请负责人完成 SchemaReviews 审查，或补充变更说明后重新运行 PR 检查。",
                    Priority = 20
                };
            }

            if (ContainsAny(message, "waiver", "Waivers", "豁免", "过期"))
            {
                return new GateFailureView
                {
                    Reason = "waiver 已过期或无效",
                    NextStep = "请更新豁免记录，或移除豁免后按正常审查流程重新检查。",
                    Priority = 30
                };
            }

            if (ContainsAny(message, "permission", "forbidden", "权限", "scope", "bot"))
            {
                return new GateFailureView
                {
                    Reason = "权限不足，无法读取在线注册中心或表格",
                    NextStep = "请确认 bot / lark-cli 权限和 Base、Wiki、Sheet 资源授权后重新运行 PR 检查。",
                    Priority = 40
                };
            }

            if (ContainsAny(message, "BranchBindings", "分支绑定", "profile"))
            {
                return new GateFailureView
                {
                    Reason = "当前分支的在线工作区未绑定或绑定冲突",
                    NextStep = "先预览同步计划，确认当前分支只对应一个有效在线工作区；权限不足时请找配置负责人处理。",
                    Priority = 50
                };
            }

            if (ContainsAny(message, "ConfigSheets", "Sheet token", "SpreadsheetToken", "在线表"))
            {
                return new GateFailureView
                {
                    Reason = "当前分支没有在线表记录",
                    NextStep = "先确认这个分支是否已经 Seed；如果只是预览新分支，请点“预览同步计划”；如果要迁移旧 Excel，请展开“本地 Excel Seed”。",
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
            var failureMessages = ExtractStringArray(json, "humanReadableFailures");
            var failures = new List<GateFailureView>();
            foreach (var failure in failureMessages)
            {
                failures.Add(GateFailureView.FromMessage(failure));
            }
            failures.Sort((left, right) => left.Priority.CompareTo(right.Priority));

            var builder = new StringBuilder();
            if (failures.Count > 0)
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
                ShortText = BuildShortText(passed, failures),
                DetailText = builder.ToString().TrimEnd()
            };
            view.Failures.AddRange(failures);
            return view;
        }

        private static string BuildShortText(bool? passed, List<GateFailureView> failures)
        {
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
                self.SetStatus("正在启动 config-sheet-forge CLI");
                var result = await self.RunProcessAsync(
                    executable,
                    arguments,
                    workingDirectory,
                    commandLine,
                    "正在运行 " + operation).ConfigureAwait(false);
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
                    "正在生成 contract").ConfigureAwait(false);
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
                self.SetStatus("正在启动 config-sheet-forge CLI");
                var applyResult = await self.RunProcessAsync(
                    cliExecutable,
                    cliArguments,
                    projectRoot,
                    cliCommandLine,
                    "正在运行 apply-contract").ConfigureAwait(false);

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

                self.Success = applyResult.ExitCode == 0 && !ResultJsonDeclaresFailure(resultJson);
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
            string runningStatus)
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
            var builder = new StringBuilder();
            builder.AppendLine(finalSuccess ? "成功" : "失败");
            builder.AppendLine("操作: " + operation);
            builder.AppendLine("模式: " + (dryRun ? "dry-run / 生成预览" : "apply / 执行写入"));
            builder.AppendLine("planned action 数量: " + (plannedActions >= 0 ? plannedActions.ToString() : "未记录"));
            builder.AppendLine("branch node: " + FirstNonEmpty(branchNode, "未记录"));
            builder.AppendLine("result path: " + FirstNonEmpty(resultPath, "未生成"));
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

        private static bool ResultJsonDeclaresFailure(string resultJson)
        {
            var success = ExtractBoolean(resultJson, "success");
            return success.HasValue && !success.Value;
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
    }

    internal sealed class MergeContextProbeResult
    {
        public bool Found { get; set; }
        public string Number { get; set; } = "";
        public string Url { get; set; } = "";
        public string BaseBranch { get; set; } = "";
        public string HeadBranch { get; set; } = "";
        public string MergeBase { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
