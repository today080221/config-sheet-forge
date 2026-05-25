using System.IO;
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
            Assert.That(message, Does.Contain("建议"));
        }
    }
}
