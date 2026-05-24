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
        public void LaunchFailureMessageIsHumanReadable()
        {
            var message = ConfigSheetForgeEditorUtility.FormatCliLaunchFailure("config-sheet-forge doctor", "file not found");

            Assert.That(message, Does.Contain("无法运行 Config Sheet Forge CLI"));
            Assert.That(message, Does.Contain("建议"));
        }
    }
}
