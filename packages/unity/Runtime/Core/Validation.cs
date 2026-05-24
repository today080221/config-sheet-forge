using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ConfigSheetForge.Core
{
    public enum FindingSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ValidationFinding
    {
        public FindingSeverity Severity { get; set; }
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public string Location { get; set; } = "";
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ValidationReport
    {
        public List<ValidationFinding> Findings { get; set; } = new List<ValidationFinding>();

        public bool HasErrors
        {
            get { return Findings.Any(f => f.Severity == FindingSeverity.Error); }
        }

        public void Add(FindingSeverity severity, string code, string message, string location)
        {
            Findings.Add(new ValidationFinding
            {
                Severity = severity,
                Code = code,
                Message = message,
                Location = location
            });
        }
    }

    public static class PortableSubsetValidator
    {
        private static readonly Regex ColumnKeyPattern = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
        private static readonly ISet<string> PortableKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "string",
            "text",
            "number",
            "integer",
            "bool",
            "boolean",
            "date",
            "datetime",
            "enum",
            "json"
        };

        public static ValidationReport Validate(WorkbookDocument workbook)
        {
            var report = new ValidationReport();

            if (workbook == null)
            {
                report.Add(FindingSeverity.Error, "workbook.missing", "Workbook data is missing.", "$");
                return report;
            }

            if (workbook.Sheets.Count == 0)
            {
                report.Add(FindingSeverity.Error, "workbook.no_sheets", "No sheets were found in this workbook.", "$.sheets");
            }

            var sheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in workbook.Sheets)
            {
                ValidateSheet(report, sheet, sheetNames);
            }

            return report;
        }

        private static void ValidateSheet(ValidationReport report, SheetDocument sheet, ISet<string> sheetNames)
        {
            var sheetName = string.IsNullOrWhiteSpace(sheet.Name) ? sheet.Id : sheet.Name;
            var location = "$.sheets[" + sheetName + "]";

            if (string.IsNullOrWhiteSpace(sheet.Name))
            {
                report.Add(FindingSeverity.Error, "sheet.name_missing", "A sheet is missing its human-readable name.", location);
            }
            else if (!sheetNames.Add(sheet.Name))
            {
                report.Add(FindingSeverity.Error, "sheet.duplicate_name", "Two sheets use the same name. Rename one sheet before syncing.", location);
            }

            var columnKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in sheet.Columns)
            {
                ValidateColumn(report, column, columnKeys, location);
            }

            var rowIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in sheet.Rows)
            {
                var rowLocation = location + ".rows[" + row.SourceIndex + "]";
                if (string.IsNullOrWhiteSpace(row.StableId))
                {
                    report.Add(FindingSeverity.Error, "row.id_missing", "A row is missing a stable id. Add an id/key column before this table can be merged safely.", rowLocation);
                }
                else if (!rowIds.Add(row.StableId))
                {
                    report.Add(FindingSeverity.Error, "row.duplicate_id", "Two rows have the same stable id. Give each editable row a unique id.", rowLocation);
                }

                foreach (var key in row.Cells.Keys)
                {
                    if (!columnKeys.Contains(key))
                    {
                        report.Add(FindingSeverity.Warning, "cell.unknown_column", "A cell references a column that is not declared in the schema.", rowLocation + ".cells[" + key + "]");
                    }
                }
            }
        }

        private static void ValidateColumn(ValidationReport report, ColumnDefinition column, ISet<string> columnKeys, string sheetLocation)
        {
            var key = column.Key ?? "";
            var location = sheetLocation + ".columns[" + key + "]";

            if (string.IsNullOrWhiteSpace(key))
            {
                report.Add(FindingSeverity.Error, "column.key_missing", "A column is missing its machine-readable key.", location);
                return;
            }

            if (!ColumnKeyPattern.IsMatch(key))
            {
                report.Add(FindingSeverity.Error, "column.key_not_portable", "Column keys must use letters, numbers, and underscores, and must not start with a number.", location);
            }

            if (!columnKeys.Add(key))
            {
                report.Add(FindingSeverity.Error, "column.duplicate_key", "Two columns use the same machine-readable key.", location);
            }

            var valueKind = column.ValueKind ?? "";
            if (!PortableKinds.Contains(valueKind))
            {
                var severity = valueKind.Equals("formula", StringComparison.OrdinalIgnoreCase) ? FindingSeverity.Warning : FindingSeverity.Error;
                report.Add(severity, "column.kind_not_portable", "This column type is not part of the portable subset. Convert it to text, number, bool, date, enum, or json.", location);
            }
        }
    }
}
