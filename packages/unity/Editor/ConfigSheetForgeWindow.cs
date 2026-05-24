using System;
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
        private string _cliPath = "config-sheet-forge";
        private string _query = "";
        private string _tableId = "";
        private string _output = "";
        private Vector2 _scroll;

        [MenuItem("Tools/Config Sheet Forge")]
        public static void Open()
        {
            GetWindow<ConfigSheetForgeWindow>("Config Sheet Forge");
        }

        private void OnEnable()
        {
            var report = SchemaReviewer.Review(CreateSmokeWorkbook());
            _output = "Shared core loaded. Smoke validation findings: " + report.Findings.Count;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Config Sheet Forge", EditorStyles.boldLabel);
            _cliPath = EditorGUILayout.TextField("CLI", _cliPath);
            _query = EditorGUILayout.TextField("Root Query", _query);
            _tableId = EditorGUILayout.TextField("Table Id", _tableId);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Doctor"))
            {
                RunCli("doctor");
            }

            if (GUILayout.Button("Discover Root"))
            {
                RunCli("discover-root", "--query", _query);
            }

            if (GUILayout.Button("Sync"))
            {
                RunCli(string.IsNullOrWhiteSpace(_tableId) ? new[] { "sync" } : new[] { "sync", "--table", _tableId });
            }

            if (GUILayout.Button("Gate"))
            {
                RunCli("gate");
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Run Shared Core Smoke Check"))
            {
                var workbook = CreateSmokeWorkbook();
                _output = "Semantic hash: " + SemanticHasher.ComputeHash(workbook);
            }

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_output, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void RunCli(params string[] args)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _cliPath,
                    WorkingDirectory = FindProjectRoot(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                foreach (var arg in args)
                {
                    if (!string.IsNullOrWhiteSpace(arg))
                    {
                        startInfo.ArgumentList.Add(arg);
                    }
                }

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
                    _output = stdout + (string.IsNullOrWhiteSpace(stderr) ? "" : Environment.NewLine + stderr);
                }
            }
            catch (Exception ex)
            {
                _output = "Could not run Config Sheet Forge CLI." + Environment.NewLine + ex.Message;
            }
        }

        private static string FindProjectRoot()
        {
            var dataPath = Application.dataPath;
            return string.IsNullOrWhiteSpace(dataPath) ? Directory.GetCurrentDirectory() : Directory.GetParent(dataPath).FullName;
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
