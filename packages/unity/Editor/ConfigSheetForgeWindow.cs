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

        private string _cliPath = "config-sheet-forge";
        private string _rootQuery = "";
        private string _tableId = "items";
        private string _tableName = "Items";
        private string _spreadsheet = "";
        private string _sheetId = "";
        private string _range = "A1:Z500";
        private string _ownerRole = "";
        private string _schemaChangeSummary = "";
        private string _excelPath = "";
        private string _sheetName = "";
        private string _fieldsText = "id|ID|string|唯一ID" + "\n" + "name|名称|string|显示名称";
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
        private bool _showAdvancedDiagnostics;
        private bool _showBottomOutput = true;
        private bool _isResizingOutputPanel;
        private float _bottomOutputHeight = 260f;
        private string _output = "";
        private string _resultSummary = "";
        private string _lastCommand = "";
        private string _lastResultPath = "";
        private string _lastLifecycleDir = "";
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
            RefreshReadonlyStatus();
            _resultSummary = "配表 Source of Truth 窗口已打开。" + Environment.NewLine +
                             "这里只刷新本地状态，不会下载、不导出、不改文件。" + Environment.NewLine +
                             "主流程只保留刷新状态、生成同步预览、运行 PR 检查。";
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
                DrawOutputTab(expanded: true, preferredHeight: Math.Max(320f, position.height - 230f));
                return;
            }

            var outputHeight = CalculateInlineOutputHeight();
            var contentHeight = Math.Max(180f, position.height - outputHeight - 260f);
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll, GUILayout.MaxHeight(contentHeight), GUILayout.ExpandHeight(false));
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
            DrawOutputResizeHandle();
            DrawOutputTab(expanded: false, preferredHeight: outputHeight);
        }

        private void DrawHeader()
        {
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("配表 Source of Truth", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("文档", "打开入门文档。"), GUILayout.Width(64)))
            {
                Application.OpenURL("https://github.com/today080221/config-sheet-forge/blob/main/docs/getting-started.md");
            }

            if (GUILayout.Button(new GUIContent("复制 UPM", "复制通过 Unity Package Manager 安装此包的 Git URL。"), GUILayout.Width(88)))
            {
                EditorGUIUtility.systemCopyBuffer = "https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.4.10";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("飞书在线 Sheet 是 Source of Truth，本地 Excel 只是兼容缓存。", EditorStyles.miniLabel);
            GUILayout.Space(4);
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

            return "按 BranchBindings/模板推导中";
        }

        private void DrawProjectSummary()
        {
            var projectRoot = FindProjectRoot();
            if (_projectConfig == null)
            {
                RefreshReadonlyStatus();
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("当前状态", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            DrawStatusMiniCard("当前分支", FirstNonEmpty(_currentGitBranch, _projectConfig.GitBranch, "未知"), MessageType.None);
            DrawStatusMiniCard("Feishu branch/profile", FirstNonEmpty(_projectConfig.BranchProfile, "按当前分支推导中"), MessageType.None);
            DrawStatusMiniCard("在线表", BuildOnlineTableStatus(), OnlineTablesReadable() ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawStatusMiniCard("Cache", BuildCacheOverviewText(projectRoot), CacheLooksFresh(projectRoot) ? MessageType.Info : MessageType.Warning);
            DrawStatusMiniCard("PR Gate", BuildGateStatusText(), GateLooksPassed() ? MessageType.Info : MessageType.Warning);
            DrawStatusMiniCard("下一步", BuildNextStepText(projectRoot), MessageType.None);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent(BuildNextStepButtonText(projectRoot), "按当前状态执行推荐动作。"), GUILayout.Height(26)))
            {
                RunNextStep(projectRoot);
            }

            if (GUILayout.Button(new GUIContent(_showBottomOutput ? "收起结果" : "展开结果", "显示或隐藏底部结果摘要面板。"), GUILayout.Width(92), GUILayout.Height(26)))
            {
                _showBottomOutput = !_showBottomOutput;
            }
            EditorGUILayout.EndHorizontal();

            _cliInvocation = ConfigSheetForgeEditorUtility.ResolveCoreCli(_projectConfig, projectRoot, _cliPath);
            EditorGUILayout.EndVertical();
        }

        private void DrawStartTab()
        {
            DrawSectionTitle("今日操作");
            EditorGUILayout.HelpBox("打开窗口只读取状态，不会下载、不导出、不写飞书、不改 Excel 或 ProjectSettings。", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("刷新状态", "只重新读取 ProjectSettings、git branch、最近 gate report 和本地 cache 文件状态。"), GUILayout.Height(32)))
            {
                RefreshReadonlyStatus();
                _lastCommand = "只读刷新状态";
                _resultSummary = "已刷新状态。没有下载、导出或写入任何文件。";
                _output = "";
            }

            if (DrawJobButton(new GUIContent("生成同步预览", "等同于同步页 dry-run；不会写飞书，也不会改本地 cache。"), GUILayout.Height(32)))
            {
                RunSyncCache(apply: false);
            }

            if (DrawJobButton(new GUIContent("运行 PR 检查", "生成最近一次 pr-gate-report，按钮旁和 PR 检查页会显示摘要。"), GUILayout.Height(32)))
            {
                RunPrGateReport();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("“生成同步预览”等同于 sync-cache dry-run，只生成计划，不写飞书、不改本地 cache、不改 ProjectSettings。", EditorStyles.wordWrappedMiniLabel);

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
            DrawSectionTitle("配表");
            EditorGUILayout.HelpBox("低风险操作可预览；创建在线表、改 schema、写回 main 等危险动作必须通过项目 contract 和确认流程。", MessageType.None);

            if (_projectConfig.Exists)
            {
                DrawCurrentBranchTables(compact: false);
                DrawSyncCacheModeCard();
                DrawProjectNewTableInputs();
                DrawProjectLifecycleCard(
                    "新建配表向导",
                    "发现项目配置后，这里走 adapter 生成 new-table lifecycle contract，再交给 core apply-contract dry-run。",
                    "生成 dry-run 预览",
                    "new-table",
                    dryRun: true,
                    includeNewTableSteps: true);
                DrawProjectSeedCard();
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
            DrawSectionTitle("合并审查");
            EditorGUILayout.HelpBox("合并审查按当前 Git 分支和目标分支自动推导，像 GitHub PR 一样先生成预览；写回 main 必须显式确认。", MessageType.None);

            DrawProjectMergeInputs();
            if (_projectConfig.Exists)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("执行", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("低风险 merge 只生成预览，不写回 main；adapter 会根据 source/target/merge-base 自动准备 base/ours/theirs semantic 输入。", EditorStyles.wordWrappedLabel);
                EditorGUILayout.BeginHorizontal();
                if (DrawJobButton(new GUIContent("生成合并预览", "生成 compare-merge dry-run，不写回 main。"), GUILayout.Height(28)))
                {
                    RunProjectLifecycle("compare-merge", dryRun: true);
                }

                if (DrawJobButton(new GUIContent("确认写回 main", "危险操作：必须勾选申请写回和确认写回。"), _writeBackToMain && _confirmWriteMain, GUILayout.Height(28)))
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
            EditorGUILayout.HelpBox("未发现 ProjectSettings/*ConfigSheetForge*.json，无法按 BranchBindings 自动定位在线表。请先接入项目 adapter；下面保留本地 CLI 兼容入口给工程诊断使用。", MessageType.Warning);
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
            DrawSectionTitle("PR 同步检查");
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

            EditorGUILayout.LabelField(_gateReportSummary.ShortText, EditorStyles.wordWrappedMiniLabel, GUILayout.MinWidth(220));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(_gateReportSummary.DetailText, EditorStyles.wordWrappedLabel);
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
            DrawReadonlyRow("semantic", FirstNonEmpty(ShortHash(table.SemanticHash), "未记录"), "ConfigSheets 或 ProjectSettings 中记录的 semantic hash。");
            if (!compact)
            {
                DrawReadonlyRow("更新时间", FirstNonEmpty(table.UpdatedAt, "未记录"), "ConfigSheets 或 ProjectSettings 中记录的更新时间。");
                DrawReadonlyRow("Schema", FirstNonEmpty(table.SchemaStatus, "未知"), "schema 是否变化或是否需要审查。");
                DrawReadonlyRow("负责人", FirstNonEmpty(table.OwnerRole, "未声明"), "ConfigSheets 负责人角色。");
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
            EditorGUILayout.LabelField("生成 dry-run 预览与首页“生成同步预览”等价：只生成计划，不写飞书、不改本地 cache、不改 ProjectSettings。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("apply 只有勾选确认并通过弹窗后才会执行；它会读取在线 Sheet、导出 xlsx、完成三方一致性检查和 hash gate 后，才可能更新本地 cache。", EditorStyles.wordWrappedLabel);
            _confirmSyncApply = EditorGUILayout.Toggle(new GUIContent("确认执行 apply", "apply 会更新 .config-sheet-forge/cache 和 excel-cache；必须先确认。"), _confirmSyncApply);
            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("生成 dry-run 预览", "读取在线注册中心并生成同步计划，不改本地 cache。"), GUILayout.Height(28)))
            {
                RunSyncCache(apply: false);
            }

            if (DrawJobButton(new GUIContent("执行 apply", "危险操作：会更新本地 cache；需要先确认。"), _confirmSyncApply, GUILayout.Height(28)))
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
            _tableId = EditorGUILayout.TextField(new GUIContent("配表ID", "会写入 inputs.tableId，并由项目 adapter 转成 contract.table.tableId。"), _tableId);
            _tableName = EditorGUILayout.TextField(new GUIContent("标题/显示名称", "会同时写入 inputs.title 和 inputs.displayName。"), _tableName);
            _ownerRole = EditorGUILayout.TextField(new GUIContent("负责人角色", "例如 configOwner；为空时由项目 adapter 默认。"), _ownerRole);
            _schemaChangeSummary = EditorGUILayout.TextField(new GUIContent("Schema 变更说明", "写入 inputs.schemaChangeSummary，供 SchemaReviews reason 使用。"), _schemaChangeSummary);
            _excelPath = EditorGUILayout.TextField(new GUIContent("本地 Excel 路径", "可选；写入 inputs.excelPath。"), _excelPath);
            _sheetName = EditorGUILayout.TextField(new GUIContent("工作表名", "可选；写入 inputs.sheetName。"), _sheetName);
            EditorGUILayout.LabelField(new GUIContent("字段模板", "每行格式：key|displayName|valueKind|description。复杂字段写入 inputs JSON，不走长 inline JSON 参数。"));
            _fieldsText = EditorGUILayout.TextArea(_fieldsText, GUILayout.MinHeight(72));
            EditorGUILayout.EndVertical();
        }

        private void DrawProjectMergeInputs()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("PR 合并上下文", EditorStyles.boldLabel);
            DrawReadonlyRow("当前分支", FirstNonEmpty(_mergeSourceBranch, _currentGitBranch, "未知"), "source/head branch。");
            DrawTargetBranchField();
            DrawReadonlyRow("目标 Feishu", FirstNonEmpty(_targetFeishuProfile, "按目标分支推导中"), "目标分支对应的 Feishu profile。");
            DrawReadonlyRow("目标 Wiki", BuildTargetWikiText(), "目标分支对应的 Wiki branch 节点。");
            DrawReadonlyRow("共同祖先", FirstNonEmpty(_mergeBase, "待推导"), "source/head 与 target/base 的 merge-base。");
            DrawReadonlyRow("GitHub PR", BuildPrText(), "如果 gh 可用且 allowPrAutoDetect=true，会尝试自动识别当前分支的 PR。");
            DrawReadonlyRow("比较范围", BuildMergeTablesText(), "将比较当前 branch/profile 下可见的在线表。");
            DrawReadonlyRow("模式", _writeBackToMain && _confirmWriteMain ? "申请写回 main" : "只生成预览", "低风险默认只生成预览。");
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

            _mergeTableId = EditorGUILayout.TextField(new GUIContent("只比较单表", "可选；留空时 adapter 可比较当前分支所有在线表。"), _mergeTableId);
            _mergeReportPath = EditorGUILayout.TextField(new GUIContent("报告路径", "写入 inputs.mergeReportPath。"), _mergeReportPath);
            _mergedPath = EditorGUILayout.TextField(new GUIContent("合并结果路径", "写入 inputs.mergedPath。"), _mergedPath);
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
            EditorGUILayout.HelpBox("打开窗口不会自动 seed、不会下载、不会改文件。apply 需要下面两个确认勾选，并且还会弹出确认框。", MessageType.Warning);
            _confirmSeedApply = EditorGUILayout.Toggle(new GUIContent("确认执行 seed apply", "允许创建/复用在线 Sheet，并回填本地/Base 状态。"), _confirmSeedApply);
            _confirmSeedExcelToSo = EditorGUILayout.Toggle(new GUIContent("确认更新 ExcelToSO settings", "允许只追加/更新目标表的 ExcelToSO JSON/YAML settings。"), _confirmSeedExcelToSo);

            EditorGUILayout.BeginHorizontal();
            if (DrawJobButton(new GUIContent("生成 seed dry-run", "生成 seed-from-local-xlsx contract 并运行 dry-run，不写飞书、不改本地文件。"), GUILayout.Height(28)))
            {
                RunProjectLifecycle("seed-from-local-xlsx", dryRun: true);
            }

            if (DrawJobButton(new GUIContent("执行 seed apply", "危险操作：必须先 dry-run 通过，再显式确认。"), _confirmSeedApply && _confirmSeedExcelToSo, GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("确认 seed apply", "将把本地 xlsx 迁移到在线 Sheet，并在三方一致后回填 cache、项目配置、Base 和 ExcelToSO settings。请确认 dry-run 已通过。", "确认执行", "取消"))
                {
                    RunProjectLifecycle("seed-from-local-xlsx", dryRun: false);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawOutputTab(bool expanded, float preferredHeight)
        {
            DrawSectionTitle(expanded ? "输出" : "最近结果");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(preferredHeight), GUILayout.ExpandHeight(expanded));
            if (!expanded)
            {
                EditorGUILayout.BeginHorizontal();
                _showBottomOutput = EditorGUILayout.Foldout(_showBottomOutput, "结果摘要", true);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("复制输出", "复制摘要、命令和详细日志。"), GUILayout.Width(84)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildCopyOutput();
                }

                if (GUILayout.Button(new GUIContent(_showBottomOutput ? "收起" : "展开", "折叠或展开底部结果面板。"), GUILayout.Width(64)))
                {
                    _showBottomOutput = !_showBottomOutput;
                }

                EditorGUILayout.EndHorizontal();
                if (!_showBottomOutput)
                {
                    EditorGUILayout.LabelField(FirstLine(BuildVisibleSummary()), EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.EndVertical();
                    return;
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("复制完整命令", "复制最近一次完整命令。"), GUILayout.Width(116)))
            {
                EditorGUIUtility.systemCopyBuffer = _lastCommand ?? "";
            }

            if (expanded && GUILayout.Button(new GUIContent("复制输出", "复制命令输出。"), GUILayout.Width(104)))
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
            var summaryHeight = Math.Min(expanded ? 180f : 132f, Math.Max(72f, summaryStyle.CalcHeight(new GUIContent(summary), Math.Max(240f, EditorGUIUtility.currentViewWidth - 56f)) + 10f));
            EditorGUILayout.SelectableLabel(summary, summaryStyle, GUILayout.Height(summaryHeight), GUILayout.ExpandWidth(true));

            _showRecentCommand = EditorGUILayout.Foldout(_showRecentCommand, expanded ? "命令详情" : "查看完整命令", true);
            if (_showRecentCommand)
            {
                DrawWrappedReadonlyBlock("", string.IsNullOrWhiteSpace(_lastCommand) ? "（暂无）" : _lastCommand, "此窗口最近启动的命令。");
            }

            _showDetailedLogs = EditorGUILayout.Foldout(_showDetailedLogs || IsJobRunning, "详细日志", true);
            if (_showDetailedLogs || IsJobRunning)
            {
                var outputStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                var logHeight = Math.Max(80f, preferredHeight - summaryHeight - (expanded ? 190f : 210f));
                _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll, false, true, GUILayout.Height(logHeight), GUILayout.ExpandHeight(true));
                EditorGUILayout.TextArea(string.IsNullOrWhiteSpace(_output) ? "暂无详细日志。" : _output, outputStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
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
            _showDetailedLogs = !string.IsNullOrWhiteSpace(_output);
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
                return 58f;
            }

            if (_bottomOutputHeight <= 0f)
            {
                _bottomOutputHeight = Math.Max(240f, position.height * 0.34f);
            }

            var max = Math.Max(240f, position.height - 280f);
            if (position.height > 780f && Math.Abs(_bottomOutputHeight - 260f) < 2f)
            {
                _bottomOutputHeight = Math.Max(_bottomOutputHeight, position.height * 0.38f);
            }

            _bottomOutputHeight = Mathf.Clamp(_bottomOutputHeight, 220f, max);
            return _bottomOutputHeight;
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
                _bottomOutputHeight = Mathf.Clamp(_bottomOutputHeight - Event.current.delta.y, 220f, Math.Max(240f, position.height - 280f));
                Repaint();
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp && _isResizingOutputPanel)
            {
                _isResizingOutputPanel = false;
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
                _showBottomOutput = true;
                _showDetailedLogs = _activeJob.WasCancelled || !_activeJob.Success;
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
            _outputScroll = Vector2.zero;
            _resultSummary = job.BuildLiveSummary();
            _output = job.StartOutput;
            _showBottomOutput = true;
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

        private static void DrawStatusMiniCard(string label, string value, MessageType messageType)
        {
            var oldColor = GUI.color;
            if (messageType == MessageType.Warning)
            {
                GUI.color = new Color(1f, 0.82f, 0.48f);
            }
            else if (messageType == MessageType.Info)
            {
                GUI.color = new Color(0.65f, 0.92f, 0.7f);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(150), GUILayout.ExpandWidth(true));
            GUI.color = oldColor;
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(value, EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 2f));
            EditorGUILayout.EndVertical();
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
                return "未绑定";
            }

            if (OnlineTablesReadable())
            {
                return "在线表可读（" + _projectConfig.CurrentBranchTables.Count.ToString() + " 张）";
            }

            if (_projectConfig.CurrentBranchTables.Count == 0)
            {
                return "未读到 ConfigSheets";
            }

            return "部分不可读";
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
                   _gateReportSummary.ShortText.IndexOf("passed=true", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   _gateReportSummary.ShortText.IndexOf("failures=0", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string BuildGateStatusText()
        {
            if (_gateReportSummary == null || string.IsNullOrWhiteSpace(_gateReportSummary.ShortText) || _gateReportSummary.ShortText == "暂无报告")
            {
                return "未生成";
            }

            return GateLooksPassed() ? "通过" : _gateReportSummary.ShortText;
        }

        private string BuildNextStepText(string projectRoot)
        {
            if (!_projectConfig.Exists)
            {
                return "先配置项目";
            }

            if (!BranchLooksBound() || !OnlineTablesReadable())
            {
                return "生成同步预览";
            }

            if (!CacheLooksFresh(projectRoot))
            {
                return "同步 cache";
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

            return "生成同步预览";
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
                return "暂时没有读到当前分支的 BranchBindings。可能是未绑定分支、bot 权限不足，或最近还没有生成 sync-cache dry-run 结果。";
            }

            return "当前 branch/profile 下没有 ConfigSheets 记录，或记录缺少 TableId / Sheet token。";
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

    internal sealed class GateReportSummaryView
    {
        public string Path { get; set; } = "";
        public string ShortText { get; set; } = "";
        public string DetailText { get; set; } = "";

        public static GateReportSummaryView NotFound(string path)
        {
            return new GateReportSummaryView
            {
                Path = path ?? "",
                ShortText = "暂无报告",
                DetailText = string.IsNullOrWhiteSpace(path)
                    ? "还没有生成 pr-gate-report。"
                    : "还没有找到最近一次 pr-gate-report：" + path
            };
        }

        public static GateReportSummaryView FromJson(string path, string json)
        {
            json = json ?? "";
            var passed = ExtractBoolean(json, "passed");
            var failures = ExtractStringArray(json, "humanReadableFailures");
            var builder = new StringBuilder();
            builder.AppendLine("passed: " + (passed.HasValue ? passed.Value.ToString().ToLowerInvariant() : "unknown"));
            builder.AppendLine("failures: " + failures.Count.ToString());
            if (failures.Count > 0)
            {
                builder.AppendLine("需要处理：");
                foreach (var failure in failures)
                {
                    builder.AppendLine("- " + failure);
                }
            }
            else if (passed.HasValue && passed.Value)
            {
                builder.AppendLine("最近一次 PR gate 已通过。");
            }

            return new GateReportSummaryView
            {
                Path = path ?? "",
                ShortText = "passed=" + (passed.HasValue ? passed.Value.ToString().ToLowerInvariant() : "unknown") + "，failures=" + failures.Count.ToString(),
                DetailText = builder.ToString().TrimEnd()
            };
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
                    builder.AppendLine("- " + failure);
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
