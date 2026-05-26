using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        private bool _writeBackToMain;
        private bool _confirmWriteMain;
        private bool _confirmSeedApply;
        private bool _confirmSeedExcelToSo;
        private bool _confirmSyncApply;
        private bool _showAdvancedDiagnostics;
        private string _output = "";
        private string _lastCommand = "";
        private int _selectedTab;
        private ProjectConfigSummary _projectConfig = new ProjectConfigSummary();
        private string _currentGitBranch = "";
        private GateReportSummaryView _gateReportSummary = GateReportSummaryView.NotFound("");
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
            RefreshReadonlyStatus();
            _output = "配表 Source of Truth 窗口已打开。" + Environment.NewLine +
                      "这里只刷新本地状态，不会下载、不导出、不改文件。" + Environment.NewLine +
                      "主流程只保留刷新状态、同步当前分支 cache、运行 PR 检查。";
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawProjectSummary();

            _selectedTab = GUILayout.Toolbar(_selectedTab, Tabs);

            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);
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
                default:
                    DrawOutputTab(expanded: true);
                    break;
            }

            if (_selectedTab != 4)
            {
                DrawOutputTab(expanded: false);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("配表 Source of Truth", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("文档", "打开入门文档。"), GUILayout.Width(64)))
            {
                Application.OpenURL("https://github.com/today080221/config-sheet-forge/blob/main/docs/getting-started.md");
            }

            if (GUILayout.Button(new GUIContent("复制 UPM", "复制通过 Unity Package Manager 安装此包的 Git URL。"), GUILayout.Width(88)))
            {
                EditorGUIUtility.systemCopyBuffer = "https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.4.6";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("飞书在线 Sheet 是 Source of Truth，本地 Excel 只是兼容缓存。", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
        }

        private void DrawProjectSummary()
        {
            var projectRoot = FindProjectRoot();
            if (_projectConfig == null)
            {
                RefreshReadonlyStatus();
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("当前工作流", EditorStyles.boldLabel);
            DrawReadonlyRow("Git 分支", FirstNonEmpty(_currentGitBranch, _projectConfig.GitBranch, "未知"), "当前本地 git branch。");
            DrawReadonlyRow("Feishu Profile", FirstNonEmpty(_projectConfig.BranchProfile, "按当前分支推导中"), "当前 Git 分支对应的 Feishu profile。");
            DrawReadonlyRow("Wiki 节点", FirstNonEmpty(_projectConfig.BranchWikiNodeTitle, "按规则推导中"), "当前 branch/profile 对应的 Wiki branch 节点。");
            DrawReadonlyRow("Wiki 链接", FirstNonEmpty(_projectConfig.BranchWikiNodeUrl, _projectConfig.BranchWikiNodeToken, "待读取 BranchBindings"), "当前 branch/profile 对应的 Wiki 节点链接或 token。");
            DrawReadonlyRow("当前分支表", BuildCurrentBranchTableCountText(), "当前 branch/profile 下可见的配表数量。");
            DrawReadonlyRow("Cache 状态", BuildCacheOverviewText(projectRoot), "本地 semantic/xlsx cache 是否需要同步。");
            DrawReadonlyRow("PR Gate", _gateReportSummary.ShortText, "最近一次 Temp/ConfigSheetForge/pr-gate-report.json 摘要。");
            DrawStatusRow("项目配置", _projectConfig.Exists, ToProjectRelativePath(_projectConfig.ProjectConfigPath), "未发现 ProjectSettings/*ConfigSheetForge*.json。");
            _cliPath = EditorGUILayout.TextField(new GUIContent("CLI", "CLI 可执行文件或绝对路径，通常是 config-sheet-forge。"), _cliPath);
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
                _output = "已刷新状态。没有下载、导出或写入任何文件。";
            }

            if (GUILayout.Button(new GUIContent("同步当前分支 cache", "先生成 dry-run 预览；不会写飞书，也不会改本地 cache。"), GUILayout.Height(32)))
            {
                RunSyncCacheCli(apply: false);
            }

            if (GUILayout.Button(new GUIContent("运行 PR 检查", "生成最近一次 pr-gate-report，按钮旁和 PR 检查页会显示摘要。"), GUILayout.Height(32)))
            {
                RunPrGateReport();
            }
            EditorGUILayout.EndHorizontal();

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
            DrawReadonlyRow("项目根目录", projectRoot, "CLI 会以此目录作为工作目录。");
            DrawReadonlyRow("schemaVersion", FirstNonEmpty(_projectConfig.SchemaVersion, "未声明"), "项目 config 中声明的 schema 版本。");
            DrawReadonlyRow("共享表数量", _projectConfig.TableCount > 0 ? _projectConfig.TableCount.ToString() : "未声明", "ProjectSettings 顶层 tables/configSheets 数组中的配表数量。");
            DrawReadonlyRow("lifecycle", FirstNonEmpty(_projectConfig.LifecycleApplyMode, "未声明"), "项目 config 中声明的 lifecycle 写入模式。");
            DrawReadonlyRow("Gate 报告", FirstNonEmpty(_projectConfig.GateReportPath, "Temp/ConfigSheetForge/pr-gate-report.json"), "PR gate report 输出路径。");
            DrawReadonlyRow("Adapter", FirstNonEmpty(_projectConfig.AdapterDescription, "未配置"), "项目 adapter 负责把项目 config 转成 lifecycle contract。");
            DrawReadonlyRow("本地状态目录", BuildLocalStateText(configPath, registryPath), ".config-sheet-forge 是 gitignored 本地状态/cache；可忽略，可重建，不参与共享项目摘要。");
            if (_projectConfig.Diagnostics.Count > 0)
            {
                foreach (var diagnostic in _projectConfig.Diagnostics)
                {
                    EditorGUILayout.HelpBox(diagnostic, MessageType.Warning);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("doctor --details", "运行 doctor --details，确认 CLI、lark-cli、权限和本地配置状态。"), GUILayout.Height(24)))
            {
                RunCli("doctor", "--details");
            }

            if (GUILayout.Button(new GUIContent("Core Smoke", "通过 Unity 加载的 shared core 计算 semantic hash。"), GUILayout.Height(24)))
            {
                var report = SchemaReviewer.Review(CreateSmokeWorkbook());
                _lastCommand = "Shared core smoke check";
                _output = "Shared core smoke findings: " + report.Findings.Count + Environment.NewLine +
                          "Semantic hash: " + SemanticHasher.ComputeHash(CreateSmokeWorkbook());
            }

            if (GUILayout.Button(new GUIContent("打开缓存目录", "在文件浏览器中显示 .config-sheet-forge/cache。"), GUILayout.Height(24)))
            {
                RevealPath(Path.Combine(FindProjectRoot(), ".config-sheet-forge", "cache"));
            }
            EditorGUILayout.EndHorizontal();

            _rootQuery = EditorGUILayout.TextField(new GUIContent("Root 搜索", "飞书/Lark 文档标题的一部分。"), _rootQuery);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("查找候选根文档", "只列出候选根文档，不会自动选择。"), GUILayout.Height(24)))
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
            if (GUILayout.Button(new GUIContent("登记本地表", "添加或更新本地 registry.json。项目 Source of Truth 流程请优先使用 contract。"), GUILayout.Height(28)))
            {
                RunCli("new-table", "--id", _tableId, "--name", _tableName, "--spreadsheet", _spreadsheet, "--sheet-id", _sheetId, "--range", _range);
            }

            if (GUILayout.Button(new GUIContent("同步单表", "导出/读取此表并计算 semantic hash。"), GUILayout.Height(28)))
            {
                RunCli("sync", "--table", _tableId);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("同步全部", "同步本地 registry 里的全部表。"), GUILayout.Height(24)))
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
            EditorGUILayout.HelpBox("三方合并只生成报告和预览；写回 main 必须由项目 contract 显式确认。", MessageType.None);

            if (_projectConfig.Exists)
            {
                DrawProjectMergeInputs();
                DrawProjectLifecycleCard(
                    "项目三方比较与合并",
                    "发现项目配置后，这里走 adapter 生成 compare-merge lifecycle contract。低风险 merge 默认只生成预览。",
                    "生成合并预览",
                    "compare-merge",
                    dryRun: true,
                    includeNewTableSteps: false);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawPathField("基线", ref _basePath, "共同祖先 semantic workbook JSON。");
            DrawPathField("本分支", ref _oursPath, "本地 semantic workbook JSON。");
            DrawPathField("对方", ref _theirsPath, "待合入 semantic workbook JSON。");
            _mergeReportPath = EditorGUILayout.TextField(new GUIContent("报告", "Markdown 报告路径。"), _mergeReportPath);
            _mergedPath = EditorGUILayout.TextField(new GUIContent("合并结果", "合并后的 semantic workbook JSON 路径。"), _mergedPath);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("生成合并预览", "生成三方合并报告和合并预览。"), GUILayout.Height(28)))
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
            if (_projectConfig.Exists && GUILayout.Button(new GUIContent("生成 PR gate report", "通过项目 adapter 生成 pr-gate-report contract，再由 core 输出 gate report。"), GUILayout.Height(28)))
            {
                RunPrGateReport();
            }
            else if (!_projectConfig.Exists && GUILayout.Button(new GUIContent("运行 Gate", "检查 semantic cache 和同步报告。"), GUILayout.Height(28)))
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
            EditorGUILayout.LabelField("dry-run 只生成预览，不写飞书、不改本地 cache。apply 会在线读取、导出 xlsx、做三方一致性检查，并且只有内容变化时才重写 cache。", EditorStyles.wordWrappedLabel);
            _confirmSyncApply = EditorGUILayout.Toggle(new GUIContent("确认执行 apply", "apply 会更新 .config-sheet-forge/cache 和 excel-cache；必须先确认。"), _confirmSyncApply);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("生成 dry-run 预览", "读取在线注册中心并生成同步计划，不改本地 cache。"), GUILayout.Height(28)))
            {
                RunSyncCacheCli(apply: false);
            }

            GUI.enabled = _confirmSyncApply;
            if (GUILayout.Button(new GUIContent("执行 apply", "危险操作：会更新本地 cache；需要先确认。"), GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("确认同步 cache", "将读取当前 branch/profile 的在线 Sheet，导出 xlsx，三方一致后更新本地 cache。无变化时会显示“无变化，未重写 cache”。", "确认执行", "取消"))
                {
                    RunSyncCacheCli(apply: true);
                }
            }
            GUI.enabled = true;
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
            EditorGUILayout.LabelField("合并输入", EditorStyles.boldLabel);
            _tableId = EditorGUILayout.TextField(new GUIContent("配表ID", "写入 inputs.tableId。"), _tableId);
            DrawPathField("基线", ref _basePath, "共同祖先 semantic workbook JSON。");
            DrawPathField("本分支", ref _oursPath, "本地 semantic workbook JSON。");
            DrawPathField("对方", ref _theirsPath, "待合入 semantic workbook JSON。");
            _mergeReportPath = EditorGUILayout.TextField(new GUIContent("报告", "写入 inputs.mergeReportPath。"), _mergeReportPath);
            _mergedPath = EditorGUILayout.TextField(new GUIContent("合并结果", "写入 inputs.mergedPath。"), _mergedPath);
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

                if (GUILayout.Button(new GUIContent(ProjectButtonLabel(buttonLabel, operation), "先生成 contract，再运行 apply-contract。dry-run 不写飞书、不改本地文件。"), GUILayout.Height(28)))
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
            if (GUILayout.Button(new GUIContent("生成 seed dry-run", "生成 seed-from-local-xlsx contract 并运行 dry-run，不写飞书、不改本地文件。"), GUILayout.Height(28)))
            {
                RunProjectLifecycle("seed-from-local-xlsx", dryRun: true);
            }

            GUI.enabled = _confirmSeedApply && _confirmSeedExcelToSo;
            if (GUILayout.Button(new GUIContent("执行 seed apply", "危险操作：必须先 dry-run 通过，再显式确认。"), GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("确认 seed apply", "将把本地 xlsx 迁移到在线 Sheet，并在三方一致后回填 cache、项目配置、Base 和 ExcelToSO settings。请确认 dry-run 已通过。", "确认执行", "取消"))
                {
                    RunProjectLifecycle("seed-from-local-xlsx", dryRun: false);
                }
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawOutputTab(bool expanded)
        {
            DrawSectionTitle("命令输出");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawReadonlyRow("最近命令", string.IsNullOrWhiteSpace(_lastCommand) ? "（暂无）" : _lastCommand, "此窗口最近启动的命令。");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("复制输出", "复制命令输出。"), GUILayout.Width(104)))
            {
                EditorGUIUtility.systemCopyBuffer = _output ?? "";
            }

            if (GUILayout.Button(new GUIContent("清空", "清空输出面板。"), GUILayout.Width(72)))
            {
                _output = "";
            }
            EditorGUILayout.EndHorizontal();

            _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll, GUILayout.MinHeight(expanded ? 260 : 140));
            EditorGUILayout.TextArea(string.IsNullOrWhiteSpace(_output) ? "暂无命令输出。" : _output, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private bool EffectiveDryRun(string operation, bool defaultDryRun)
        {
            if (string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase))
            {
                return !(_writeBackToMain && _confirmWriteMain);
            }

            return defaultDryRun;
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
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void DrawReadonlyRow(string label, string value, string tooltip)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), GUILayout.Width(110));
            EditorGUILayout.SelectableLabel(value, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
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
            var cleanArgs = CleanArgs(args);
            _lastCommand = ConfigSheetForgeEditorUtility.BuildCommandLine(_cliPath, cleanArgs);

            try
            {
                _output = RunProcessCapture(ConfigSheetForgeEditorUtility.ResolveExecutable(_cliPath), cleanArgs, FindProjectRoot()).Render(_lastCommand);
            }
            catch (Exception ex)
            {
                _output = ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, ex.Message);
            }
        }

        private void RunSyncCacheCli(bool apply)
        {
            RefreshReadonlyStatus();
            if (!_projectConfig.Exists)
            {
                _output = "未发现项目配置。请确认 ProjectSettings 下存在 *ConfigSheetForge*.json。";
                return;
            }

            var projectRoot = FindProjectRoot();
            var resultPath = GetUnityLifecyclePath(projectRoot, "sync-cache.result.json");
            var args = BuildSyncCacheArgs(apply, resultPath);
            _lastCommand = ConfigSheetForgeEditorUtility.BuildCommandLine(_cliPath, args);
            try
            {
                var result = RunProcessCapture(ConfigSheetForgeEditorUtility.ResolveExecutable(_cliPath), args, projectRoot);
                var output = result.Render(_lastCommand);
                if (output.IndexOf("cache unchanged", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    output.IndexOf("无变化", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    output += Environment.NewLine + "无变化，未重写 cache。";
                }

                _output = output;
                RefreshReadonlyStatus();
            }
            catch (Exception ex)
            {
                _output = ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, ex.Message);
            }
        }

        private void RunPrGateReport()
        {
            if (_projectConfig.Exists && _projectConfig.HasLifecycleAdapter)
            {
                RunProjectLifecycle("pr-gate-report", dryRun: false);
            }
            else
            {
                RunCli("gate", "--details", "--report", ResolveGateReportPath(FindProjectRoot()));
            }

            RefreshReadonlyStatus();
        }

        private void RunProjectLifecycle(string operation, bool dryRun)
        {
            RefreshReadonlyStatus();
            if (!_projectConfig.Exists)
            {
                _output = "未发现项目配置。请确认 ProjectSettings 下存在 *ConfigSheetForge*.json。";
                return;
            }

            if (!_projectConfig.HasLifecycleAdapter)
            {
                _output = "项目配置缺少 adapterScript 或 contractCommand，无法生成 lifecycle contract。";
                return;
            }

            var projectRoot = FindProjectRoot();
            var workDir = Path.Combine(projectRoot, "Temp", "ConfigSheetForge", "unity-lifecycle");
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
            _lastCommand = adapter.ToCommandLine() + Environment.NewLine +
                           ConfigSheetForgeEditorUtility.BuildCommandLine(_cliPath, BuildApplyContractArgs(operation, requestPath, resultPath, finalGateReportPath));

            try
            {
                var output = new StringBuilder();
                var adapterResult = RunProcessCapture(ConfigSheetForgeEditorUtility.ResolveExecutable(adapter.Executable), adapter.Arguments, projectRoot);
                output.AppendLine(adapterResult.Render(adapter.ToCommandLine()));
                output.AppendLine("Inputs: " + inputsPath);
                if (adapterResult.ExitCode != 0)
                {
                    output.AppendLine("adapter 没有成功生成 contract，请按上面的错误处理。");
                    _output = output.ToString();
                    return;
                }

                if (!File.Exists(requestPath))
                {
                    output.AppendLine("adapter 运行成功，但没有生成 contract request：");
                    output.AppendLine(requestPath);
                    output.AppendLine("请检查项目 config 的 contractArgs/contractRequestPath 设置。");
                    _output = output.ToString();
                    return;
                }

                output.AppendLine("Contract request: " + requestPath);
                var applyArgs = BuildApplyContractArgs(operation, requestPath, resultPath, finalGateReportPath);
                var applyResult = RunProcessCapture(ConfigSheetForgeEditorUtility.ResolveExecutable(_cliPath), applyArgs, projectRoot);
                output.AppendLine(applyResult.Render(ConfigSheetForgeEditorUtility.BuildCommandLine(_cliPath, applyArgs)));
                if (File.Exists(resultPath))
                {
                    output.AppendLine("Lifecycle result: " + resultPath);
                    output.AppendLine(File.ReadAllText(resultPath));
                }

                if (string.Equals(operation, "pr-gate-report", StringComparison.OrdinalIgnoreCase))
                {
                    output.AppendLine("Final gate report: " + finalGateReportPath);
                    if (File.Exists(finalGateReportPath))
                    {
                        output.AppendLine(BuildGateReportSummary(File.ReadAllText(finalGateReportPath)));
                    }
                }

                _output = output.ToString();
                RefreshReadonlyStatus();
            }
            catch (Exception ex)
            {
                _output = ConfigSheetForgeEditorUtility.FormatCliLaunchFailure(_lastCommand, ex.Message);
            }
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
            EditorGUIUtility.systemCopyBuffer = ConfigSheetForgeEditorUtility.BuildCommandLine(_cliPath, cleanArgs);
        }

        private void CopySyncCacheCommand(bool apply)
        {
            RefreshReadonlyStatus();
            var resultPath = GetUnityLifecyclePath(FindProjectRoot(), "sync-cache.result.json");
            EditorGUIUtility.systemCopyBuffer = ConfigSheetForgeEditorUtility.BuildCommandLine(_cliPath, BuildSyncCacheArgs(apply, resultPath));
        }

        private void CopyPrGateCommand()
        {
            RefreshReadonlyStatus();
            if (_projectConfig.Exists && _projectConfig.HasLifecycleAdapter)
            {
                CopyProjectLifecycleAdapterCommand("pr-gate-report", dryRun: false);
                return;
            }

            EditorGUIUtility.systemCopyBuffer = ConfigSheetForgeEditorUtility.BuildCommandLine(_cliPath, BuildPrGateArgs());
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
            MergeLifecycleSummary(projectRoot, "sync-cache.result.json");
            MergeLifecycleSummary(projectRoot, "pr-gate-report.result.json");
            _gateReportSummary = LoadGateReportSummary(projectRoot);
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

        private string[] BuildApplyContractArgs(string operation, string requestPath, string resultPath, string finalGateReportPath)
        {
            var args = new List<string> { "apply-contract", "--request", requestPath, "--out", resultPath };
            if (string.Equals(operation, "pr-gate-report", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--report");
                args.Add(finalGateReportPath);
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

            return args.ToArray();
        }

        private string ResolveGateReportPath(string projectRoot)
        {
            var path = FirstNonEmpty(_projectConfig.GateReportPath, Path.Combine("Temp", "ConfigSheetForge", "pr-gate-report.json"));
            return ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, path);
        }

        private static string GetUnityLifecyclePath(string projectRoot, string fileName)
        {
            return Path.Combine(projectRoot, "Temp", "ConfigSheetForge", "unity-lifecycle", fileName);
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
            AppendJsonProperty(builder, "tableId", _tableId, comma: true);
            AppendJsonProperty(builder, "title", _tableName, comma: true);
            AppendJsonProperty(builder, "displayName", _tableName, comma: true);
            AppendJsonProperty(builder, "ownerRole", _ownerRole, comma: true);
            AppendJsonProperty(builder, "schemaChangeSummary", _schemaChangeSummary, comma: true);
            AppendJsonProperty(builder, "excelPath", _excelPath, comma: true);
            AppendJsonProperty(builder, "sheetName", _sheetName, comma: true);
            AppendJsonProperty(builder, "basePath", _basePath, comma: true);
            AppendJsonProperty(builder, "oursPath", _oursPath, comma: true);
            AppendJsonProperty(builder, "theirsPath", _theirsPath, comma: true);
            AppendJsonProperty(builder, "mergeReportPath", _mergeReportPath, comma: true);
            AppendJsonProperty(builder, "mergedPath", _mergedPath, comma: true);
            AppendJsonProperty(builder, "writeBackToMain", _writeBackToMain, comma: true);
            AppendJsonProperty(builder, "confirmWriteMain", _writeBackToMain && _confirmWriteMain, comma: true);
            AppendJsonProperty(builder, "confirmApply", _confirmSeedApply, comma: true);
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
            try
            {
                var result = RunProcessCapture("git", args, FindProjectRoot());
                return result.ExitCode == 0 ? result.Stdout.Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        private static ProcessCaptureResult RunProcessCapture(string executable, IEnumerable<string> args, string workingDirectory)
        {
            var cleanArgs = CleanArgs(args);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = ConfigSheetForgeEditorUtility.JoinArguments(cleanArgs),
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
                    return new ProcessCaptureResult { ExitCode = -1, Stderr = "Could not start process." };
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new ProcessCaptureResult { ExitCode = process.ExitCode, Stdout = stdout, Stderr = stderr };
            }
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

    internal sealed class ProjectFieldInput
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ValueKind { get; set; } = "string";
        public string Description { get; set; } = "";
    }
}
