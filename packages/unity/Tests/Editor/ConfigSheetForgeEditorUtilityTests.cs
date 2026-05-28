using System.IO;
using System.Diagnostics;
using System.Threading;
using ConfigSheetForge.Core;
using ConfigSheetForge.Unity.Editor;
using NUnit.Framework;

namespace ConfigSheetForge.Unity.Editor.Tests
{
    public sealed class ConfigSheetForgeEditorUtilityTests
    {
        [Test]
        public void SharedCoreComputesSemanticHash()
        {
            var workbook = new WorkbookDocument
            {
                ProviderId = "unity",
                SourceId = "edit-mode-test",
                SourceTitle = "Edit Mode Test"
            };
            var sheet = new SheetDocument { Id = "items", Name = "Items" };
            sheet.Columns.Add(new ColumnDefinition { Key = "id", DisplayName = "ID", ValueKind = "string" });
            var row = new RowDocument { StableId = "item_001", SourceIndex = 2 };
            row.Cells["id"] = new CellValue { RawText = "item_001", NormalizedText = "item_001" };
            sheet.Rows.Add(row);
            workbook.Sheets.Add(sheet);

            Assert.That(SemanticHasher.ComputeHash(workbook), Has.Length.EqualTo(64));
        }

        [Test]
        public void CommandLineQuotesPathsAndValues()
        {
            var command = ConfigSheetForgeEditorUtility.BuildCommandLine(
                "C:/Tools/config sheet forge.exe",
                new[] { "discover-root", "--query", "配置 根" });

            Assert.That(command, Does.Contain("\"C:/Tools/config sheet forge.exe\""));
            Assert.That(command, Does.Contain("\"配置 根\""));
        }

        [Test]
        public void ConfigPathsUseProjectLocalStateDirectory()
        {
            var root = "C:/Unity/Project";

            Assert.That(ConfigSheetForgeEditorUtility.GetConfigPath(root).Replace('\\', '/'), Is.EqualTo("C:/Unity/Project/.config-sheet-forge/config.json"));
            Assert.That(ConfigSheetForgeEditorUtility.GetRegistryPath(root).Replace('\\', '/'), Is.EqualTo("C:/Unity/Project/.config-sheet-forge/registry.json"));
        }

        [Test]
        public void FindsProjectSettingsConfigSheetForgeConfig()
        {
            var root = Path.Combine(Path.GetTempPath(), "csforge-unity-config-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var projectSettings = Path.Combine(root, "ProjectSettings");
                Directory.CreateDirectory(projectSettings);
                var config = Path.Combine(projectSettings, "Project.ConfigSheetForge.json");
                File.WriteAllText(config, "{}");

                Assert.That(ConfigSheetForgeEditorUtility.FindProjectConfigPath(root), Is.EqualTo(config));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void ExcelToSoUpdaterCanAppendJsonSettingsWithoutReordering()
        {
            var json = "{\n  \"configs\": [\n    { \"tableId\": \"SkillsData\", \"excelPath\": \"Excel/SkillsData.xlsx\" }\n  ]\n}\n";
            var updated = UnityExcelToSoSettingsUpdater.UpsertText(json, new UnityExcelToSoEntry
            {
                TableId = "MonsterData",
                ExcelPath = "Excel/MonsterData.xlsx",
                ScriptableObjectType = "MonsterConfig"
            });

            Assert.That(updated, Does.Contain("\"tableId\": \"SkillsData\""));
            Assert.That(updated, Does.Contain("\"tableId\": \"MonsterData\""));
            Assert.That(updated.IndexOf("SkillsData", System.StringComparison.Ordinal), Is.LessThan(updated.IndexOf("MonsterData", System.StringComparison.Ordinal)));
        }

        [Test]
        public void LaunchFailureMessageIsHumanReadable()
        {
            var message = ConfigSheetForgeEditorUtility.FormatCliLaunchFailure("config-sheet-forge doctor", "file not found");

            Assert.That(message, Does.Contain("无法运行 Config Sheet Forge CLI"));
            Assert.That(message, Does.Contain("下一步"));
            Assert.That(message, Does.Contain("CONFIG_SHEET_FORGE_CLI"));
        }

        [Test]
        public void BackgroundJobStartDoesNotWaitForProcessExit()
        {
            var isWindows = Path.DirectorySeparatorChar == '\\';
            var executable = isWindows ? "cmd.exe" : "/bin/sh";
            var arguments = isWindows
                ? new[] { "/C", "ping -n 3 127.0.0.1 > nul" }
                : new[] { "-c", "sleep 2" };
            var job = ConfigSheetForgeBackgroundJob.CreateSingleProcess(
                "test-sleep",
                dryRun: true,
                commandLine: ConfigSheetForgeEditorUtility.BuildCommandLine(executable, arguments),
                executable: executable,
                arguments: arguments,
                workingDirectory: Path.GetTempPath(),
                resultPath: "",
                lifecycleDirectory: Path.GetTempPath(),
                refreshReadonlyStatusOnComplete: false,
                projectConfig: new ProjectConfigSummary());

            var stopwatch = Stopwatch.StartNew();
            job.Start();
            stopwatch.Stop();
            try
            {
                Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(250), "Start must return immediately instead of blocking IMGUI MouseUp.");
                Assert.That(job.IsFinished, Is.False, "The background job should still be running right after Start.");
            }
            finally
            {
                job.Cancel("test cleanup");
                for (var i = 0; i < 30 && !job.IsFinished; i++)
                {
                    Thread.Sleep(100);
                }
            }
        }

        [Test]
        public void ExcelToSoImporterStaysOptionalAndBlocksOldExcelPaths()
        {
            var importerSource = File.ReadAllText("Packages/dev.config-sheet-forge.unity/Editor/ConfigSheetForgeExcelToSoImporter.cs");
            var windowSource = File.ReadAllText("Packages/dev.config-sheet-forge.unity/Editor/ConfigSheetForgeWindow.cs");
            var asmdef = File.ReadAllText("Packages/dev.config-sheet-forge.unity/Editor/ConfigSheetForge.Editor.asmdef");

            Assert.That(importerSource, Does.Contain("ExcelToScriptableObjectApi"));
            Assert.That(importerSource, Does.Contain("ImportByProfile"));
            Assert.That(importerSource, Does.Contain("SourceOfTruthCache"));
            Assert.That(windowSource, Does.Contain("导入 Unity 配表资产"));
            Assert.That(windowSource, Does.Contain("安装/更新 Source of Truth 导入 profile"));
            Assert.That(windowSource, Does.Contain("不改变本地 Excel profile"));
            Assert.That(windowSource, Does.Contain("CloneExcelToSoSettingForCache"));
            Assert.That(windowSource, Does.Contain("CloneExcelToSoSlavesForCache"));
            Assert.That(windowSource, Does.Contain("ValidateSourceOfTruthSettings"));
            Assert.That(windowSource, Does.Contain("use_hash_string = template.use_hash_string"));
            Assert.That(windowSource, Does.Contain("generate_tostring_method = template.generate_tostring_method"));
            Assert.That(windowSource, Does.Contain("slaves = CloneExcelToSoSlavesForCache"));
            Assert.That(windowSource, Does.Contain("script_directory 不能是空或裸 Assets"));
            Assert.That(windowSource, Does.Contain("SourceOfTruthCache profile 不安全"));
            Assert.That(windowSource, Does.Contain("不会写旧 Excel/"));
            Assert.That(asmdef, Does.Not.Contain("GreatClock.ExcelToScriptableObject.Editor"));
        }

        [Test]
        public void DesktopBridgeSupportsExplicitInstallFlow()
        {
            var bridgeSource = File.ReadAllText("Packages/dev.config-sheet-forge.unity/Editor/ConfigSheetForgeBridgeWindow.cs");

            Assert.That(bridgeSource, Does.Contain("安装 Desktop"));
            Assert.That(bridgeSource, Does.Contain("DesktopInstallPathPrefKey"));
            Assert.That(bridgeSource, Does.Contain("config-sheet-forge-desktop-windows-x64-"));
            Assert.That(bridgeSource, Does.Contain("sha256 校验失败"));
            Assert.That(bridgeSource, Does.Contain("不会写仓库文件"));
            Assert.That(bridgeSource, Does.Contain("EditorPrefs.SetString(DesktopInstallPathPrefKey"));
            Assert.That(bridgeSource, Does.Contain("CONFIG_SHEET_FORGE_DESKTOP"));
            Assert.That(bridgeSource, Does.Contain("CONFIG_SHEET_FORGE_ROOT"));
            Assert.That(bridgeSource, Does.Contain("LooksLikeDevDesktopBuild"));
            Assert.That(bridgeSource, Does.Contain("Desktop release 包疑似开发构建"));
        }
    }
}
