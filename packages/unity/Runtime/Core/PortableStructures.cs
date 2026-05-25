using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
#if !UNITY_5_3_OR_NEWER
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
#endif

namespace ConfigSheetForge.Core
{
    public static class PortableStructureValidator
    {
        private static readonly Dictionary<string, string> BlockedDetailKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "formula", "使用了公式。配表导出只支持普通值，请把公式结果复制为纯文本或数字。" },
            { "hasFormula", "使用了公式。配表导出只支持普通值，请把公式结果复制为纯文本或数字。" },
            { "floatingImage", "包含浮动图片。配表导出只支持普通单元格值，请删除图片或改为资源路径文本。" },
            { "cellImage", "包含单元格图片。配表导出只支持普通单元格值，请删除图片或改为资源路径文本。" },
            { "image", "包含图片。配表导出只支持普通单元格值，请删除图片或改为资源路径文本。" },
            { "mergedRange", "属于合并单元格。请取消合并，并把值填到每一行需要的位置。" },
            { "richText", "使用了富文本。请改成普通文本。" },
            { "crossSheetReference", "引用了其它工作表。请把引用结果复制为纯文本或数字。" },
            { "mentionUser", "包含 @人。请改成普通文本，不要插入人员对象。" },
            { "mentionDoc", "包含 @文档。请改成普通文本或直接填写链接。" },
            { "dateObject", "使用了日期对象。请改成固定格式文本，例如 2026-05-24。" },
            { "unsupportedCellType", "使用了不支持的单元格类型。请改成普通文本、数字或布尔值。" }
        };

        public static ValidationReport Validate(WorkbookDocument workbook)
        {
            var report = new ValidationReport();
            if (workbook == null)
            {
                report.Add(FindingSeverity.Error, "workbook.missing", "Workbook data is missing.", "$");
                return report;
            }

            foreach (var sheet in workbook.Sheets)
            {
                ValidateSheet(report, sheet);
            }

            return report;
        }

        private static void ValidateSheet(ValidationReport report, SheetDocument sheet)
        {
            var sheetName = string.IsNullOrWhiteSpace(sheet.Name) ? sheet.Id : sheet.Name;
            foreach (var metadata in sheet.Metadata)
            {
                if (BlockedDetailKeys.TryGetValue(metadata.Key, out var message) && IsTruthy(metadata.Value))
                {
                    AddFinding(report, "portable." + CanonicalCode(metadata.Key), sheetName, metadata.Value, message, "sheet");
                }
            }

            foreach (var row in sheet.Rows)
            {
                foreach (var pair in row.Cells)
                {
                    var column = sheet.Columns.FirstOrDefault(c => string.Equals(c.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
                    var a1 = FirstNonEmpty(GetDetail(pair.Value, "sourceA1"), ToA1(column == null ? "" : column.SourceColumn, row.SourceIndex));
                    foreach (var detail in pair.Value.Details)
                    {
                        if (BlockedDetailKeys.TryGetValue(detail.Key, out var message) && IsTruthy(detail.Value))
                        {
                            AddFinding(report, "portable." + CanonicalCode(detail.Key), sheetName, a1, message, "cell").Details["detail"] = detail.Value;
                        }
                    }

                    if (string.Equals(pair.Value.ValueKind, "formula", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFinding(report, "portable.formula", sheetName, a1, BlockedDetailKeys["formula"], "cell");
                    }
                }
            }
        }

        public static ValidationFinding AddFinding(ValidationReport report, string code, string sheetName, string a1, string message, string scope)
        {
            var location = string.IsNullOrWhiteSpace(a1) ? sheetName : sheetName + "!" + a1;
            var finding = report.Add(FindingSeverity.Error, code, location + " " + message, location);
            finding.Details["table"] = sheetName;
            finding.Details["range"] = a1;
            finding.Details["scope"] = scope;
            finding.Details["repair"] = message;
            return finding;
        }

        private static string GetDetail(CellValue cell, string key)
        {
            return cell != null && cell.Details.TryGetValue(key, out var value) ? value : "";
        }

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            return !string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase);
        }

        private static string ToA1(string sourceColumn, int sourceIndex)
        {
            if (string.IsNullOrWhiteSpace(sourceColumn) || sourceIndex <= 0)
            {
                return "";
            }

            return sourceColumn + sourceIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static string CanonicalCode(string key)
        {
            var result = new System.Text.StringBuilder();
            foreach (var c in key ?? "")
            {
                if (char.IsLetterOrDigit(c))
                {
                    result.Append(char.ToLowerInvariant(c));
                }
                else if (result.Length == 0 || result[result.Length - 1] != '_')
                {
                    result.Append('_');
                }
            }

            return result.ToString().Trim('_');
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

    public sealed class TriangulationReport
    {
        public bool Passed { get; set; } = true;
        public string OnlineHash { get; set; } = "";
        public string ExportedXlsxHash { get; set; } = "";
        public string NormalizedHash { get; set; } = "";
        public List<string> DiffSummary { get; set; } = new List<string>();
        public List<ValidationFinding> Findings { get; set; } = new List<ValidationFinding>();
    }

    public static class SemanticTriangulator
    {
        public static TriangulationReport Compare(WorkbookDocument onlineRead, WorkbookDocument exportedXlsx, WorkbookDocument normalized)
        {
            var report = new TriangulationReport();
            if (onlineRead == null || exportedXlsx == null || normalized == null)
            {
                report.Passed = false;
                report.DiffSummary.Add("三方比较缺少输入：online-read、exported-xlsx、semantic-normalize 都必须存在。");
                return report;
            }

            var comparableOnline = ComparableWorkbook(onlineRead);
            var comparableExported = ComparableWorkbook(exportedXlsx);
            var comparableNormalized = ComparableWorkbook(normalized);
            report.OnlineHash = SemanticHasher.ComputeHash(comparableOnline);
            report.ExportedXlsxHash = SemanticHasher.ComputeHash(comparableExported);
            report.NormalizedHash = SemanticHasher.ComputeHash(comparableNormalized);
            if (report.OnlineHash == report.ExportedXlsxHash && report.OnlineHash == report.NormalizedHash)
            {
                return report;
            }

            report.Passed = false;
            AddDiffs(report, "online-read", comparableOnline, "exported-xlsx", comparableExported);
            AddDiffs(report, "online-read", comparableOnline, "semantic-normalize", comparableNormalized);
            if (report.DiffSummary.Count == 0)
            {
                report.DiffSummary.Add("三方 hash 不一致，但没有定位到具体单元格差异。请检查 schema、隐藏格式或导出结构。");
            }

            return report;
        }

        public static WorkbookDocument Normalize(WorkbookDocument workbook)
        {
            return WorkbookCloner.Clone(workbook);
        }

        private static WorkbookDocument ComparableWorkbook(WorkbookDocument workbook)
        {
            var clone = WorkbookCloner.Clone(workbook);
            clone.ProviderId = "";
            clone.SourceId = "";
            clone.Revision = "";
            return clone;
        }

        private static void AddDiffs(TriangulationReport report, string leftName, WorkbookDocument left, string rightName, WorkbookDocument right)
        {
            foreach (var sheet in left.Sheets)
            {
                var rightSheet = right.Sheets.FirstOrDefault(s => string.Equals(s.Name, sheet.Name, StringComparison.OrdinalIgnoreCase) ||
                                                                  string.Equals(s.Id, sheet.Id, StringComparison.OrdinalIgnoreCase));
                if (rightSheet == null)
                {
                    Add(report, leftName + " 有工作表 “" + sheet.Name + "”，但 " + rightName + " 没有。");
                    continue;
                }

                foreach (var column in sheet.Columns)
                {
                    if (!rightSheet.Columns.Any(c => string.Equals(c.Key, column.Key, StringComparison.OrdinalIgnoreCase)))
                    {
                        Add(report, sheet.Name + " 缺少列：" + column.Key + "（" + rightName + "）。");
                    }
                }

                foreach (var row in sheet.Rows)
                {
                    var rightRow = rightSheet.Rows.FirstOrDefault(r => string.Equals(r.StableId, row.StableId, StringComparison.OrdinalIgnoreCase));
                    if (rightRow == null)
                    {
                        Add(report, sheet.Name + " 缺少行：" + row.StableId + "（" + rightName + "）。");
                        continue;
                    }

                    foreach (var cell in row.Cells)
                    {
                        rightRow.Cells.TryGetValue(cell.Key, out var rightCell);
                        var leftFingerprint = SemanticHasher.CellFingerprint(cell.Value);
                        var rightFingerprint = SemanticHasher.CellFingerprint(rightCell);
                        if (!string.Equals(leftFingerprint, rightFingerprint, StringComparison.Ordinal))
                        {
                            Add(report, sheet.Name + " 行 “" + row.StableId + "” 列 “" + cell.Key + "” 在 " + leftName + " 与 " + rightName + " 中不一致。");
                        }
                    }
                }
            }
        }

        private static void Add(TriangulationReport report, string message)
        {
            if (report.DiffSummary.Count < 50)
            {
                report.DiffSummary.Add(message);
            }
        }
    }

#if !UNITY_5_3_OR_NEWER
    public sealed class XlsxImportResult
    {
        public WorkbookDocument Workbook { get; set; }
        public ValidationReport Report { get; set; } = new ValidationReport();
    }

    public static class XlsxWorkbookReader
    {
        private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static XlsxImportResult Import(string path, MatrixWorkbookImportOptions options)
        {
            var result = new XlsxImportResult();
            if (options == null)
            {
                options = new MatrixWorkbookImportOptions();
            }

            if (!File.Exists(path))
            {
                result.Report.Add(FindingSeverity.Error, "xlsx.missing", "导出的 xlsx 文件不存在，无法做三方一致性比较。", path);
                result.Workbook = MatrixWorkbookImporter.Import(new List<IList<string>>(), options).Workbook;
                return result;
            }

            using (var archive = ZipFile.OpenRead(path))
            {
                var sharedStrings = ReadSharedStrings(archive, result.Report);
                var workbook = ReadWorkbookSheets(archive);
                if (workbook.Count == 0)
                {
                    workbook.Add(new XlsxSheetInfo { Name = FirstNonEmpty(options.SheetName, options.SheetId, "Sheet1"), Path = "xl/worksheets/sheet1.xml" });
                }

                var target = ResolveSheet(workbook, options);
                var matrix = ReadWorksheetMatrix(archive, target, sharedStrings, result.Report);
                var import = MatrixWorkbookImporter.Import(matrix.Cast<IList<string>>().ToList(), options);
                result.Workbook = import.Workbook;
                foreach (var finding in import.Report.Findings)
                {
                    result.Report.Findings.Add(finding);
                }
            }

            return result;
        }

        public static ValidationReport InspectPortableStructures(string path, string tableName)
        {
            var report = new ValidationReport();
            if (!File.Exists(path))
            {
                report.Add(FindingSeverity.Error, "xlsx.missing", "导出的 xlsx 文件不存在。", path);
                return report;
            }

            using (var archive = ZipFile.OpenRead(path))
            {
                var sharedStrings = ReadSharedStrings(archive, report);
                var workbook = ReadWorkbookSheets(archive);
                foreach (var sheet in workbook)
                {
                    InspectWorksheet(archive, sheet, sharedStrings, report, FirstNonEmpty(tableName, sheet.Name));
                }
            }

            return report;
        }

        private static void InspectWorksheet(ZipArchive archive, XlsxSheetInfo sheet, IList<SharedStringInfo> sharedStrings, ValidationReport report, string tableName)
        {
            var entry = archive.GetEntry(sheet.Path);
            if (entry == null)
            {
                return;
            }

            var document = ReadXml(entry);
            var root = document.Root;
            if (root == null)
            {
                return;
            }

            foreach (var formula in root.Descendants(SpreadsheetNs + "f"))
            {
                var cell = formula.Parent;
                var a1 = cell == null ? "" : (string)cell.Attribute("r") ?? "";
                PortableStructureValidator.AddFinding(report, "portable.formula", tableName, a1, "使用了公式。配表导出只支持普通值，请把公式结果复制为纯文本或数字。", "cell");
            }

            foreach (var merge in root.Descendants(SpreadsheetNs + "mergeCell"))
            {
                var range = (string)merge.Attribute("ref") ?? "";
                PortableStructureValidator.AddFinding(report, "portable.merged_cells", tableName, range, "使用了合并单元格。请取消合并，并把值填到每一行需要的位置。", "range");
            }

            foreach (var drawing in root.Descendants().Where(e => e.Name.LocalName == "drawing"))
            {
                PortableStructureValidator.AddFinding(report, "portable.image", tableName, sheet.Name, "包含图片。配表导出只支持普通单元格值，请删除图片或改为资源路径文本。", "sheet");
            }

            foreach (var cell in root.Descendants(SpreadsheetNs + "c"))
            {
                var a1 = (string)cell.Attribute("r") ?? "";
                var type = (string)cell.Attribute("t") ?? "";
                if (!string.IsNullOrWhiteSpace(type) &&
                    type != "s" && type != "str" && type != "inlineStr" && type != "b" && type != "n" && type != "e")
                {
                    PortableStructureValidator.AddFinding(report, "portable.unsupported_cell_type", tableName, a1, "使用了不支持的单元格类型。请改成普通文本、数字或布尔值。", "cell")
                        .Details["cellType"] = type;
                }

                if (type == "e")
                {
                    PortableStructureValidator.AddFinding(report, "portable.unsupported_cell_type", tableName, a1, "单元格是错误值。请修正为普通文本、数字或布尔值。", "cell");
                }

                var inlineRich = cell.Descendants(SpreadsheetNs + "r").Any();
                if (inlineRich)
                {
                    PortableStructureValidator.AddFinding(report, "portable.rich_text", tableName, a1, "使用了富文本。请改成普通文本。", "cell");
                }

                if (type == "s")
                {
                    var valueText = (string)cell.Element(SpreadsheetNs + "v") ?? "";
                    if (int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                        index >= 0 && index < sharedStrings.Count && sharedStrings[index].RichText)
                    {
                        PortableStructureValidator.AddFinding(report, "portable.rich_text", tableName, a1, "使用了富文本。请改成普通文本。", "cell");
                    }
                }
            }
        }

        private static IList<SharedStringInfo> ReadSharedStrings(ZipArchive archive, ValidationReport report)
        {
            var strings = new List<SharedStringInfo>();
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return strings;
            }

            var document = ReadXml(entry);
            foreach (var item in document.Descendants(SpreadsheetNs + "si"))
            {
                var text = string.Concat(item.Descendants(SpreadsheetNs + "t").Select(t => t.Value));
                strings.Add(new SharedStringInfo { Text = text, RichText = item.Elements(SpreadsheetNs + "r").Any() });
            }

            return strings;
        }

        private static IList<XlsxSheetInfo> ReadWorkbookSheets(ZipArchive archive)
        {
            var sheets = new List<XlsxSheetInfo>();
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry == null)
            {
                return sheets;
            }

            var relationships = ReadRelationships(archive, "xl/_rels/workbook.xml.rels");
            var workbook = ReadXml(workbookEntry);
            foreach (var sheet in workbook.Descendants(SpreadsheetNs + "sheet"))
            {
                var relationId = (string)sheet.Attribute(RelationshipNs + "id") ?? "";
                relationships.TryGetValue(relationId, out var target);
                sheets.Add(new XlsxSheetInfo
                {
                    Name = (string)sheet.Attribute("name") ?? "",
                    Id = (string)sheet.Attribute("sheetId") ?? "",
                    Path = NormalizeWorkbookTarget(target)
                });
            }

            return sheets;
        }

        private static Dictionary<string, string> ReadRelationships(ZipArchive archive, string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var entry = archive.GetEntry(path);
            if (entry == null)
            {
                return result;
            }

            var document = ReadXml(entry);
            foreach (var relationship in document.Descendants(PackageRelationshipNs + "Relationship"))
            {
                var id = (string)relationship.Attribute("Id") ?? "";
                var target = (string)relationship.Attribute("Target") ?? "";
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result[id] = target;
                }
            }

            return result;
        }

        private static XlsxSheetInfo ResolveSheet(IList<XlsxSheetInfo> sheets, MatrixWorkbookImportOptions options)
        {
            var match = sheets.FirstOrDefault(s => string.Equals(s.Id, options.SheetId, StringComparison.OrdinalIgnoreCase) ||
                                                   string.Equals(s.Name, options.SheetName, StringComparison.OrdinalIgnoreCase));
            return match ?? sheets[0];
        }

        private static List<List<string>> ReadWorksheetMatrix(ZipArchive archive, XlsxSheetInfo sheet, IList<SharedStringInfo> sharedStrings, ValidationReport report)
        {
            var entry = archive.GetEntry(sheet.Path);
            if (entry == null)
            {
                report.Add(FindingSeverity.Error, "xlsx.sheet_missing", "xlsx 中找不到工作表。", sheet.Path);
                return new List<List<string>>();
            }

            var document = ReadXml(entry);
            var rows = new SortedDictionary<int, SortedDictionary<int, string>>();
            foreach (var cell in document.Descendants(SpreadsheetNs + "c"))
            {
                var reference = (string)cell.Attribute("r") ?? "";
                var position = ParseA1(reference);
                if (position.Row < 0 || position.Column < 0)
                {
                    continue;
                }

                if (!rows.TryGetValue(position.Row, out var row))
                {
                    row = new SortedDictionary<int, string>();
                    rows[position.Row] = row;
                }

                row[position.Column] = ReadCellValue(cell, sharedStrings);
            }

            var matrix = new List<List<string>>();
            if (rows.Count == 0)
            {
                return matrix;
            }

            var maxRow = rows.Keys.Max();
            var maxColumn = rows.Values.SelectMany(r => r.Keys).DefaultIfEmpty(0).Max();
            for (var rowIndex = 0; rowIndex <= maxRow; rowIndex++)
            {
                var row = new List<string>();
                rows.TryGetValue(rowIndex, out var values);
                for (var columnIndex = 0; columnIndex <= maxColumn; columnIndex++)
                {
                    row.Add(values != null && values.TryGetValue(columnIndex, out var value) ? value : "");
                }

                matrix.Add(row);
            }

            return matrix;
        }

        private static string ReadCellValue(XElement cell, IList<SharedStringInfo> sharedStrings)
        {
            var type = (string)cell.Attribute("t") ?? "";
            if (type == "inlineStr")
            {
                return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(t => t.Value));
            }

            var raw = (string)cell.Element(SpreadsheetNs + "v") ?? "";
            if (type == "s" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sharedStrings.Count)
            {
                return sharedStrings[index].Text;
            }

            if (type == "b")
            {
                return raw == "1" ? "true" : "false";
            }

            return raw;
        }

        private static XDocument ReadXml(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            {
                return XDocument.Load(stream, LoadOptions.None);
            }
        }

        private static string NormalizeWorkbookTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return "xl/worksheets/sheet1.xml";
            }

            target = target.Replace('\\', '/');
            if (target.StartsWith("/", StringComparison.Ordinal))
            {
                return target.TrimStart('/');
            }

            return target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target.TrimStart('/');
        }

        private static CellPosition ParseA1(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return new CellPosition(-1, -1);
            }

            var column = 0;
            var index = 0;
            while (index < reference.Length && char.IsLetter(reference[index]))
            {
                column = column * 26 + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
                index++;
            }

            var rowText = reference.Substring(index);
            if (column == 0 || !int.TryParse(rowText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) || row <= 0)
            {
                return new CellPosition(-1, -1);
            }

            return new CellPosition(row - 1, column - 1);
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

        private sealed class SharedStringInfo
        {
            public string Text = "";
            public bool RichText;
        }

        private sealed class XlsxSheetInfo
        {
            public string Name = "";
            public string Id = "";
            public string Path = "";
        }

        private struct CellPosition
        {
            public CellPosition(int row, int column)
            {
                Row = row;
                Column = column;
            }

            public int Row { get; }
            public int Column { get; }
        }
    }
#endif
}
