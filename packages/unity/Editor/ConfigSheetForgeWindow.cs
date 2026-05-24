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
        private static readonly string[] Tabs = { "Start", "Tables", "Merge", "Gate", "Output" };

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

        [MenuItem("Tools/Config Sheet Forge")]
        public static void Open()
        {
            var window = GetWindow<ConfigSheetForgeWindow>("Config Sheet Forge");
            window.minSize = new Vector2(640, 520);
        }

        private void OnEnable()
        {
            var report = SchemaReviewer.Review(CreateSmokeWorkbook());
            _output = "Welcome to Config Sheet Forge." + Environment.NewLine +
                      "Shared core loaded. Smoke validation findings: " + report.Findings.Count + Environment.NewLine +
                      "Start with Doctor, then Init if this Unity project has no local config yet.";
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
            EditorGUILayout.LabelField("Config Sheet Forge", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("Docs", "Open the getting started guide in your browser."), GUILayout.Width(64)))
            {
                Application.OpenURL("https://github.com/today080221/config-sheet-forge/blob/main/docs/getting-started.md");
            }

            if (GUILayout.Button(new GUIContent("Copy UPM", "Copy the Git URL for installing this package through Unity Package Manager."), GUILayout.Width(88)))
            {
                EditorGUIUtility.systemCopyBuffer = "https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.1.0";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Use Feishu/Lark sheets as a reviewed config source of truth.", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
        }

        private void DrawProjectSummary()
        {
            var projectRoot = FindProjectRoot();
            var configPath = Path.Combine(projectRoot, ".config-sheet-forge", "config.json");
            var registryPath = Path.Combine(projectRoot, ".config-sheet-forge", "registry.json");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Current Project", EditorStyles.boldLabel);
            DrawReadonlyRow("Root", projectRoot, "Unity project root used as the CLI working directory.");
            DrawStatusRow("Local config", File.Exists(configPath), "Found .config-sheet-forge/config.json", "Missing. Run Init first.");
            DrawStatusRow("Registry", File.Exists(registryPath), "Found .config-sheet-forge/registry.json", "Missing. Run Init first.");
            _cliPath = EditorGUILayout.TextField(new GUIContent("CLI", "CLI executable or absolute path. Usually config-sheet-forge."), _cliPath);
            EditorGUILayout.EndVertical();
        }

        private void DrawStartTab()
        {
            DrawSectionTitle("Quick Start");
            EditorGUILayout.HelpBox("New here? Run these steps top to bottom. Discovery only recommends roots; it will not silently select a Feishu/Lark document for you.", MessageType.Info);

            DrawStep(
                "1",
                "Check local tools",
                "Runs doctor --details. Confirms the CLI, lark-cli, auth, local config, registry, and root setup.",
                "Run Doctor",
                "Run config-sheet-forge doctor --details.",
                delegate { RunCli("doctor", "--details"); });

            DrawStep(
                "2",
                "Create local config",
                "Creates .config-sheet-forge/config.json and registry.json in this Unity project. These files stay local and are ignored by git.",
                "Init Project",
                "Run config-sheet-forge init in the Unity project root.",
                delegate { RunCli("init"); });

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("3. Find candidate root", EditorStyles.boldLabel);
            _rootQuery = EditorGUILayout.TextField(new GUIContent("Search Query", "Part of the Feishu/Lark document title to search for."), _rootQuery);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Discover Root", "List candidate Feishu/Lark roots. You still choose and copy the right one into local config."), GUILayout.Height(28)))
            {
                RunCli("discover-root", "--query", _rootQuery);
            }

            if (GUILayout.Button(new GUIContent("Copy Command", "Copy the discover-root command."), GUILayout.Width(116), GUILayout.Height(28)))
            {
                CopyCommand("discover-root", "--query", _rootQuery);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            DrawStep(
                "4",
                "Validate before committing",
                "Runs gate against synced semantic cache. It catches duplicate ids, invalid column keys, and unsupported portable subset types.",
                "Run Gate",
                "Run config-sheet-forge gate.",
                delegate { RunCli("gate"); });
        }

        private void DrawTablesTab()
        {
            DrawSectionTitle("Tables");
            EditorGUILayout.HelpBox("Register one source-of-truth sheet, then sync it into the local semantic cache. Table ids should be stable and safe for filenames.", MessageType.None);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _tableId = EditorGUILayout.TextField(new GUIContent("Table Id", "Stable machine id, for example items or rewards_daily."), _tableId);
            _tableName = EditorGUILayout.TextField(new GUIContent("Display Name", "Human name shown in reports."), _tableName);
            _spreadsheet = EditorGUILayout.TextField(new GUIContent("Spreadsheet", "Feishu/Lark sheet URL or token. Keep real private URLs in local config only."), _spreadsheet);
            _sheetId = EditorGUILayout.TextField(new GUIContent("Sheet Id", "Provider sheet id. Leave empty only when your provider can infer it."), _sheetId);
            _range = EditorGUILayout.TextField(new GUIContent("Range", "A1 range to read, for example A1:Z500."), _range);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Register Table", "Add or update this table in .config-sheet-forge/registry.json."), GUILayout.Height(28)))
            {
                RunCli("new-table", "--id", _tableId, "--name", _tableName, "--spreadsheet", _spreadsheet, "--sheet-id", _sheetId, "--range", _range);
            }

            if (GUILayout.Button(new GUIContent("Sync Table", "Export/read this table and compute a semantic hash."), GUILayout.Height(28)))
            {
                RunCli("sync", "--table", _tableId);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Sync All", "Sync every table currently registered in the local registry."), GUILayout.Height(24)))
            {
                RunCli("sync");
            }

            if (GUILayout.Button(new GUIContent("Copy Register Command", "Copy the new-table command with the current fields."), GUILayout.Height(24)))
            {
                CopyCommand("new-table", "--id", _tableId, "--name", _tableName, "--spreadsheet", _spreadsheet, "--sheet-id", _sheetId, "--range", _range);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawMergeTab()
        {
            DrawSectionTitle("Merge Review");
            EditorGUILayout.HelpBox("Use three semantic workbook JSON files: base, ours, and theirs. The merged workbook keeps ours where conflicts need human review.", MessageType.None);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawPathField("Base", ref _basePath, "Common ancestor semantic workbook JSON.");
            DrawPathField("Ours", ref _oursPath, "Local semantic workbook JSON.");
            DrawPathField("Theirs", ref _theirsPath, "Incoming semantic workbook JSON.");
            _mergeReportPath = EditorGUILayout.TextField(new GUIContent("Report", "Markdown report path."), _mergeReportPath);
            _mergedPath = EditorGUILayout.TextField(new GUIContent("Merged", "Merged semantic workbook JSON path."), _mergedPath);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Run Merge", "Generate a three-way merge report and merged workbook."), GUILayout.Height(28)))
            {
                RunCli("merge", "--base", _basePath, "--ours", _oursPath, "--theirs", _theirsPath, "--out", _mergeReportPath, "--merged", _mergedPath);
            }

            if (GUILayout.Button(new GUIContent("Copy Command", "Copy the merge command."), GUILayout.Width(116), GUILayout.Height(28)))
            {
                CopyCommand("merge", "--base", _basePath, "--ours", _oursPath, "--theirs", _theirsPath, "--out", _mergeReportPath, "--merged", _mergedPath);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawGateTab()
        {
            DrawSectionTitle("PR Gate");
            EditorGUILayout.HelpBox("Gate is the safe pre-commit check. It validates semantic cache files and reports issues in wording that non-program users can act on.", MessageType.None);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Run Gate", "Validate cached semantic workbook files."), GUILayout.Height(28)))
            {
                RunCli("gate", "--details");
            }

            if (GUILayout.Button(new GUIContent("Run Shared Core Smoke", "Compute a semantic hash through the same core assembly used by CLI and Unity."), GUILayout.Height(28)))
            {
                var workbook = CreateSmokeWorkbook();
                _lastCommand = "Shared core smoke check";
                _output = "Semantic hash: " + SemanticHasher.ComputeHash(workbook);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Open Cache Folder", "Reveal .config-sheet-forge/cache in your file browser."), GUILayout.Height(24)))
            {
                RevealPath(Path.Combine(FindProjectRoot(), ".config-sheet-forge", "cache"));
            }

            if (GUILayout.Button(new GUIContent("Copy Gate Command", "Copy the gate command."), GUILayout.Height(24)))
            {
                CopyCommand("gate", "--details");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawOutputTab(bool expanded)
        {
            DrawSectionTitle("Command Output");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawReadonlyRow("Last command", string.IsNullOrWhiteSpace(_lastCommand) ? "(none yet)" : _lastCommand, "Most recent command launched by this window.");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Copy Output", "Copy the command output to clipboard."), GUILayout.Width(104)))
            {
                EditorGUIUtility.systemCopyBuffer = _output ?? "";
            }

            if (GUILayout.Button(new GUIContent("Clear", "Clear the output panel."), GUILayout.Width(72)))
            {
                _output = "";
            }
            EditorGUILayout.EndHorizontal();

            _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll, GUILayout.MinHeight(expanded ? 260 : 140));
            EditorGUILayout.TextArea(string.IsNullOrWhiteSpace(_output) ? "No command output yet." : _output, GUILayout.ExpandHeight(true));
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

            if (GUILayout.Button(new GUIContent("Copy Command", "Copy this command instead of running it."), GUILayout.Width(116), GUILayout.Height(28)))
            {
                if (buttonLabel == "Run Doctor")
                {
                    CopyCommand("doctor", "--details");
                }
                else if (buttonLabel == "Init Project")
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
            _lastCommand = BuildCommandLine(_cliPath, cleanArgs);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ResolveExecutable(_cliPath),
                    Arguments = JoinArguments(cleanArgs),
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
                _output = "Could not run Config Sheet Forge CLI." + Environment.NewLine +
                          "Command: " + _lastCommand + Environment.NewLine +
                          "Reason: " + ex.Message + Environment.NewLine +
                          "Tip: install the CLI or set the CLI field to an absolute path.";
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
            EditorGUIUtility.systemCopyBuffer = BuildCommandLine(_cliPath, cleanArgs);
        }

        private static string BuildCommandLine(string executable, IEnumerable<string> args)
        {
            var builder = new StringBuilder();
            builder.Append(QuoteArgument(executable));
            foreach (var arg in args)
            {
                builder.Append(' ').Append(QuoteArgument(arg));
            }

            return builder.ToString();
        }

        private static string JoinArguments(IEnumerable<string> args)
        {
            var builder = new StringBuilder();
            foreach (var arg in args)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(QuoteArgument(arg));
            }

            return builder.ToString();
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            var builder = new StringBuilder();
            builder.Append('"');
            var backslashes = 0;
            foreach (var c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }

                builder.Append('\\', backslashes);
                backslashes = 0;
                builder.Append(c);
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private static string ResolveExecutable(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
            {
                return executable;
            }

            if (Path.IsPathRooted(executable) || executable.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            {
                return executable;
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var extensions = Application.platform == RuntimePlatform.WindowsEditor
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';')
                : new[] { "" };

            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory.Trim().Trim('"'), executable + extension);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return executable;
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
