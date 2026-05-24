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

        private string _cliPath = "config-sheet-forge";
        private string _rootQuery = "";
        private string _tableId = "items";
        private string _tableName = "Items";
        private string _spreadsheet = "";
        private string _sheetId = "";
        private string _range = "A1:Z500";
        private string _basePath = "";
        private string _oursPath = "";
        private string _theirsPath = "";
        private string _mergeReportPath = "merge-report.md";
        private string _mergedPath = "merged.semantic.json";
        private string _output = "";
        private string _lastCommand = "";
        private int _selectedTab;
        private Vector2 _mainScroll;
        private Vector2 _outputScroll;

        [MenuItem("Tools/Config Sheet Forge/打开同步窗口")]
        public static void Open()
        {
            var window = GetWindow<ConfigSheetForgeWindow>("配表 Source of Truth");
            window.minSize = new Vector2(640, 520);
        }

        private void OnEnable()
        {
            var report = SchemaReviewer.Review(CreateSmokeWorkbook());
            _output = "配表 Source of Truth 窗口已打开。" + Environment.NewLine +
                      "这里只刷新本地状态，不会下载、不导出、不改文件。" + Environment.NewLine +
                      "Shared core smoke findings: " + report.Findings.Count + Environment.NewLine +
                      "下一步：先确认项目配置路径，再按需运行检查。";
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
                EditorGUIUtility.systemCopyBuffer = "https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.3.0";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("飞书在线 Sheet 是 Source of Truth，本地 Excel 只是兼容缓存。", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
        }

        private void DrawProjectSummary()
        {
            var projectRoot = FindProjectRoot();
            var configPath = ConfigSheetForgeEditorUtility.GetConfigPath(projectRoot);
            var registryPath = ConfigSheetForgeEditorUtility.GetRegistryPath(projectRoot);
            var projectConfigPath = ConfigSheetForgeEditorUtility.FindProjectConfigPath(projectRoot);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("当前项目", EditorStyles.boldLabel);
            DrawReadonlyRow("项目根目录", projectRoot, "CLI 会以此目录作为工作目录。");
            DrawStatusRow("项目配置", !string.IsNullOrWhiteSpace(projectConfigPath), ToProjectRelativePath(projectConfigPath), "未发现 ProjectSettings/*ConfigSheetForge*.json。");
            DrawStatusRow("本地配置", File.Exists(configPath), "已找到 .config-sheet-forge/config.json", "未找到；项目 adapter 可直接提供 contract。");
            DrawStatusRow("本地 Registry", File.Exists(registryPath), "已找到 .config-sheet-forge/registry.json", "未找到；在线 Base 注册中心可由 contract 驱动。");
            _cliPath = EditorGUILayout.TextField(new GUIContent("CLI", "CLI 可执行文件或绝对路径，通常是 config-sheet-forge。"), _cliPath);
            EditorGUILayout.EndVertical();
        }

        private void DrawStartTab()
        {
            DrawSectionTitle("状态检查");
            EditorGUILayout.HelpBox("打开窗口只读取状态，不会下载、不导出、不写飞书、不改 Excel 或 ProjectSettings。", MessageType.Info);

            DrawStep(
                "1",
                "检查本地工具",
                "运行 doctor --details，确认 CLI、lark-cli、权限和本地配置状态。",
                "运行检查",
                "运行 config-sheet-forge doctor --details。",
                delegate { RunCli("doctor", "--details"); });

            DrawStep(
                "2",
                "创建本地配置",
                "只创建 .config-sheet-forge/config.json 和 registry.json；项目专用配置优先由 ProjectSettings/*ConfigSheetForge*.json 提供。",
                "初始化本地配置",
                "在 Unity 项目根目录运行 config-sheet-forge init。",
                delegate { RunCli("init"); });

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("3. 查找候选根文档", EditorStyles.boldLabel);
            _rootQuery = EditorGUILayout.TextField(new GUIContent("搜索关键词", "飞书/Lark 文档标题的一部分。"), _rootQuery);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("查找候选", "只列出候选根文档，不会自动选择。"), GUILayout.Height(28)))
            {
                RunCli("discover-root", "--query", _rootQuery);
            }

            if (GUILayout.Button(new GUIContent("复制命令", "复制 discover-root 命令。"), GUILayout.Width(116), GUILayout.Height(28)))
            {
                CopyCommand("discover-root", "--query", _rootQuery);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            DrawStep(
                "4",
                "提交前检查",
                "运行 gate，检查 semantic cache、portable subset 和 schema 风险。",
                "运行 Gate",
                "运行 config-sheet-forge gate。",
                delegate { RunCli("gate"); });
        }

        private void DrawTablesTab()
        {
            DrawSectionTitle("配表");
            EditorGUILayout.HelpBox("低风险操作可预览；创建在线表、改 schema、写回 main 等危险动作必须通过项目 contract 和确认流程。", MessageType.None);

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
            if (GUILayout.Button(new GUIContent("运行 Gate", "检查 semantic cache 和同步报告。"), GUILayout.Height(28)))
            {
                RunCli("gate", "--details");
            }

            if (GUILayout.Button(new GUIContent("运行 Core Smoke", "通过 Unity 加载的 shared core 计算 semantic hash。"), GUILayout.Height(28)))
            {
                var workbook = CreateSmokeWorkbook();
                _lastCommand = "Shared core smoke check";
                _output = "Semantic hash: " + SemanticHasher.ComputeHash(workbook);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("打开缓存目录", "在文件浏览器中显示 .config-sheet-forge/cache。"), GUILayout.Height(24)))
            {
                RevealPath(Path.Combine(FindProjectRoot(), ".config-sheet-forge", "cache"));
            }

            if (GUILayout.Button(new GUIContent("复制 Gate 命令", "复制 gate 命令。"), GUILayout.Height(24)))
            {
                CopyCommand("gate", "--details");
            }
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
                var startInfo = new ProcessStartInfo
                {
                    FileName = ConfigSheetForgeEditorUtility.ResolveExecutable(_cliPath),
                    Arguments = ConfigSheetForgeEditorUtility.JoinArguments(cleanArgs),
                    WorkingDirectory = FindProjectRoot(),
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
                        _output = "Could not start CLI process.";
                        return;
                    }

                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    var builder = new StringBuilder();
                    builder.AppendLine("Command: " + _lastCommand);
                    builder.AppendLine("ExitCode: " + process.ExitCode);
                    if (!string.IsNullOrWhiteSpace(stdout))
                    {
                        builder.AppendLine();
                        builder.AppendLine(stdout.TrimEnd());
                    }

                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        builder.AppendLine();
                        builder.AppendLine("stderr:");
                        builder.AppendLine(stderr.TrimEnd());
                    }

                    _output = builder.ToString();
                }
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

        private void CopyCommand(params string[] args)
        {
            var cleanArgs = CleanArgs(args);
            EditorGUIUtility.systemCopyBuffer = ConfigSheetForgeEditorUtility.BuildCommandLine(_cliPath, cleanArgs);
        }

        private static string FindProjectRoot()
        {
            var dataPath = Application.dataPath;
            return string.IsNullOrWhiteSpace(dataPath) ? Directory.GetCurrentDirectory() : Directory.GetParent(dataPath).FullName;
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
    }
}
