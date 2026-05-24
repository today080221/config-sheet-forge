using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ConfigSheetForge.Core
{
    public sealed class MatrixWorkbookImportOptions
    {
        public string ProviderId { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceTitle { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string SheetName { get; set; } = "";
        public int FieldRow { get; set; } = 0;
        public int TypeRow { get; set; } = -1;
        public int DescriptionRow { get; set; } = -1;
        public int DataStartRow { get; set; } = -1;
        public bool TreatUnknownTypesAsEnum { get; set; }
    }

    public sealed class MatrixWorkbookImportResult
    {
        public WorkbookDocument Workbook { get; set; }
        public ValidationReport Report { get; set; } = new ValidationReport();
    }

    public static class MatrixWorkbookImporter
    {
        private static readonly Regex ColumnKeyPattern = new Regex("[^A-Za-z0-9_]+", RegexOptions.Compiled);
        private static readonly Regex DatePattern = new Regex(@"^\d{4}[-/.]\d{1,2}[-/.]\d{1,2}$", RegexOptions.Compiled);
        private static readonly Regex DateTimePattern = new Regex(@"^\d{4}[-/.]\d{1,2}[-/.]\d{1,2}[ T]\d{1,2}:\d{2}", RegexOptions.Compiled);

        public static MatrixWorkbookImportResult Import(IList<IList<string>> matrix, MatrixWorkbookImportOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var result = new MatrixWorkbookImportResult();
            var workbook = new WorkbookDocument
            {
                ProviderId = options.ProviderId ?? "",
                SourceId = options.SourceId ?? "",
                SourceTitle = FirstNonEmpty(options.SourceTitle, options.SheetName, "Workbook")
            };
            result.Workbook = workbook;

            var sheet = new SheetDocument
            {
                Id = options.SheetId ?? "",
                Name = FirstNonEmpty(options.SheetName, options.SheetId, "Sheet1")
            };
            workbook.Sheets.Add(sheet);

            if (matrix == null || matrix.Count == 0)
            {
                result.Report.Add(FindingSeverity.Warning, "matrix.empty", "没有可导入的表格数据。", "$.sheets[" + sheet.Name + "]");
                return result;
            }

            var fieldRow = ClampRow(options.FieldRow, 0);
            if (fieldRow >= matrix.Count)
            {
                result.Report.Add(FindingSeverity.Error, "layout.field_row_missing", "字段名所在行超出了表格范围。", "$.layout.fieldRow");
                return result;
            }

            var dataStartRow = ResolveDataStartRow(options, fieldRow);
            var header = matrix[fieldRow];
            var columnCount = header.Count;
            var descriptors = new List<ColumnDescriptor>();
            for (var i = 0; i < columnCount; i++)
            {
                var displayName = GetCell(matrix, fieldRow, i);
                var key = MakeColumnKey(displayName, i);
                var typeText = GetOptionalCell(matrix, options.TypeRow, i);
                var description = GetOptionalCell(matrix, options.DescriptionRow, i);
                var descriptor = new ColumnDescriptor
                {
                    Index = i,
                    Key = key,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName.Trim(),
                    SourceColumn = ToColumnName(i),
                    TypeText = typeText,
                    Description = description
                };
                ResolveColumnKind(descriptor, matrix, dataStartRow, options, result.Report, sheet.Name);
                descriptors.Add(descriptor);

                var column = new ColumnDefinition
                {
                    Key = descriptor.Key,
                    DisplayName = descriptor.DisplayName,
                    SourceColumn = descriptor.SourceColumn,
                    ValueKind = descriptor.ValueKind
                };

                if (!string.IsNullOrWhiteSpace(description))
                {
                    column.Details["description"] = description.Trim();
                }

                if (!string.IsNullOrWhiteSpace(descriptor.EnumTypeName))
                {
                    column.Details["enumType"] = descriptor.EnumTypeName;
                }

                if (descriptor.AllowedEnumValues.Count > 0)
                {
                    column.Details["enumOptions"] = string.Join(",", descriptor.AllowedEnumValues.OrderBy(v => v, StringComparer.Ordinal));
                }

                sheet.Columns.Add(column);
            }

            var idColumn = descriptors.FirstOrDefault(h => string.Equals(h.Key, "id", StringComparison.OrdinalIgnoreCase) || string.Equals(h.Key, "key", StringComparison.OrdinalIgnoreCase)) ??
                           descriptors.FirstOrDefault();
            if (idColumn == null)
            {
                result.Report.Add(FindingSeverity.Error, "layout.no_columns", "字段名行没有可导入的列。", "$.sheets[" + sheet.Name + "].columns");
                return result;
            }

            for (var rowIndex = dataStartRow; rowIndex < matrix.Count; rowIndex++)
            {
                var values = matrix[rowIndex];
                if (values == null || values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var stableIdText = idColumn.Index < values.Count ? values[idColumn.Index] : "";
                var row = new RowDocument
                {
                    SourceIndex = rowIndex + 1,
                    StableId = string.IsNullOrWhiteSpace(stableIdText) ? "row-" + rowIndex.ToString("0000", CultureInfo.InvariantCulture) : stableIdText.Trim()
                };

                foreach (var descriptor in descriptors)
                {
                    var text = descriptor.Index < values.Count ? values[descriptor.Index] ?? "" : "";
                    row.Cells[descriptor.Key] = NormalizeCell(text, descriptor, result.Report, sheet.Name, row.SourceIndex);
                }

                sheet.Rows.Add(row);
            }

            return result;
        }

        public static CellValue NormalizeCell(string rawText, string valueKind)
        {
            var descriptor = new ColumnDescriptor { Key = "value", ValueKind = CanonicalKind(valueKind) };
            return NormalizeCell(rawText, descriptor, new ValidationReport(), "Sheet1", 1);
        }

        private static void ResolveColumnKind(ColumnDescriptor descriptor, IList<IList<string>> matrix, int dataStartRow, MatrixWorkbookImportOptions options, ValidationReport report, string sheetName)
        {
            var parsed = ParseTypeDeclaration(descriptor.TypeText, options.TreatUnknownTypesAsEnum);
            if (!string.IsNullOrWhiteSpace(parsed.ValueKind))
            {
                descriptor.ValueKind = parsed.ValueKind;
                descriptor.EnumTypeName = parsed.EnumTypeName;
                foreach (var value in parsed.EnumValues)
                {
                    descriptor.AllowedEnumValues.Add(value);
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(descriptor.TypeText))
            {
                var finding = report.Add(FindingSeverity.Warning, "column.type_unknown", "字段类型不在便携子集中，已按字符串处理。", "$.sheets[" + sheetName + "].columns[" + descriptor.Key + "]");
                finding.Details["typeText"] = descriptor.TypeText.Trim();
            }

            descriptor.ValueKind = InferKind(matrix, dataStartRow, descriptor.Index);
        }

        private static TypeDeclaration ParseTypeDeclaration(string typeText, bool treatUnknownAsEnum)
        {
            var declaration = new TypeDeclaration();
            if (string.IsNullOrWhiteSpace(typeText))
            {
                return declaration;
            }

            var trimmed = typeText.Trim();
            var lower = trimmed.ToLowerInvariant();
            if (lower.StartsWith("enum:", StringComparison.Ordinal))
            {
                declaration.ValueKind = "enum";
                AddEnumValues(declaration.EnumValues, trimmed.Substring("enum:".Length));
                return declaration;
            }

            if (lower.StartsWith("enum(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                declaration.ValueKind = "enum";
                AddEnumValues(declaration.EnumValues, trimmed.Substring(5, trimmed.Length - 6));
                return declaration;
            }

            if (lower.StartsWith("enum{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                declaration.ValueKind = "enum";
                AddEnumValues(declaration.EnumValues, trimmed.Substring(5, trimmed.Length - 6));
                return declaration;
            }

            var canonical = CanonicalKind(trimmed);
            if (!string.IsNullOrWhiteSpace(canonical))
            {
                declaration.ValueKind = canonical;
                return declaration;
            }

            if (treatUnknownAsEnum)
            {
                declaration.ValueKind = "enum";
                declaration.EnumTypeName = trimmed;
            }

            return declaration;
        }

        private static void AddEnumValues(ICollection<string> values, string text)
        {
            foreach (var value in text.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = value.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !values.Contains(trimmed))
                {
                    values.Add(trimmed);
                }
            }
        }

        private static string InferKind(IList<IList<string>> matrix, int dataStartRow, int columnIndex)
        {
            var values = new List<string>();
            for (var rowIndex = dataStartRow; rowIndex < matrix.Count; rowIndex++)
            {
                var value = GetCell(matrix, rowIndex, columnIndex);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            }

            if (values.Count == 0)
            {
                return "string";
            }

            if (values.All(IsInteger))
            {
                return "integer";
            }

            if (values.All(IsNumber))
            {
                return "number";
            }

            if (values.All(IsExplicitBooleanWord))
            {
                return "bool";
            }

            if (values.All(v => DateTimePattern.IsMatch(v) && TryNormalizeDateTime(v, out _)))
            {
                return "datetime";
            }

            if (values.All(v => DatePattern.IsMatch(v) && TryNormalizeDate(v, out _)))
            {
                return "date";
            }

            if (values.All(v => LooksLikeJsonContainer(v) && LooksLikeBalancedJson(v)))
            {
                return "json";
            }

            return "string";
        }

        private static CellValue NormalizeCell(string rawText, ColumnDescriptor descriptor, ValidationReport report, string sheetName, int sourceIndex)
        {
            var raw = rawText ?? "";
            var trimmed = raw.Trim();
            var cell = new CellValue
            {
                ValueKind = descriptor.ValueKind,
                RawText = raw,
                NormalizedText = trimmed
            };

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return cell;
            }

            var location = "$.sheets[" + sheetName + "].rows[" + sourceIndex + "].cells[" + descriptor.Key + "]";
            switch (descriptor.ValueKind)
            {
                case "number":
                    if (TryParseDecimal(trimmed, out var number))
                    {
                        cell.NormalizedText = number.ToString("G29", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        AddCellWarning(report, "cell.number_invalid", "数字字段的值无法识别，已保留原文。", location, trimmed);
                    }
                    break;
                case "integer":
                    if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    {
                        cell.NormalizedText = integer.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        AddCellWarning(report, "cell.integer_invalid", "整数字段的值无法识别，已保留原文。", location, trimmed);
                    }
                    break;
                case "bool":
                    if (TryNormalizeBoolean(trimmed, out var booleanText))
                    {
                        cell.NormalizedText = booleanText;
                    }
                    else
                    {
                        AddCellWarning(report, "cell.bool_invalid", "布尔字段的值无法识别，已保留原文。", location, trimmed);
                    }
                    break;
                case "date":
                    if (TryNormalizeDate(trimmed, out var dateText))
                    {
                        cell.NormalizedText = dateText;
                    }
                    else
                    {
                        AddCellWarning(report, "cell.date_invalid", "日期字段的值无法识别，已保留原文。", location, trimmed);
                    }
                    break;
                case "datetime":
                    if (TryNormalizeDateTime(trimmed, out var dateTimeText))
                    {
                        cell.NormalizedText = dateTimeText;
                    }
                    else
                    {
                        AddCellWarning(report, "cell.datetime_invalid", "日期时间字段的值无法识别，已保留原文。", location, trimmed);
                    }
                    break;
                case "json":
                    if (!LooksLikeJsonContainer(trimmed) || !LooksLikeBalancedJson(trimmed))
                    {
                        AddCellWarning(report, "cell.json_invalid", "JSON 字段的值不是对象或数组，已保留原文。", location, trimmed);
                    }
                    break;
                case "enum":
                    if (descriptor.AllowedEnumValues.Count > 0 && !descriptor.AllowedEnumValues.Contains(trimmed))
                    {
                        var finding = AddCellWarning(report, "cell.enum_option_drift", "枚举字段出现了未声明的选项。", location, trimmed);
                        finding.Details["allowed"] = string.Join(",", descriptor.AllowedEnumValues.OrderBy(v => v, StringComparer.Ordinal));
                    }
                    break;
            }

            return cell;
        }

        private static ValidationFinding AddCellWarning(ValidationReport report, string code, string message, string location, string raw)
        {
            var finding = report.Add(FindingSeverity.Warning, code, message, location);
            finding.Details["raw"] = raw;
            return finding;
        }

        private static string CanonicalKind(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "string":
                case "str":
                case "text":
                case "lang":
                case "language":
                case "rich":
                    return "string";
                case "number":
                case "float":
                case "double":
                case "decimal":
                    return "number";
                case "integer":
                case "int":
                case "int32":
                case "long":
                case "int64":
                    return "integer";
                case "bool":
                case "boolean":
                    return "bool";
                case "date":
                    return "date";
                case "datetime":
                case "date_time":
                case "timestamp":
                    return "datetime";
                case "json":
                    return "json";
                case "enum":
                    return "enum";
                default:
                    return "";
            }
        }

        private static bool TryNormalizeBoolean(string value, out string normalized)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "y":
                case "1":
                case "是":
                case "真":
                case "对":
                    normalized = "true";
                    return true;
                case "false":
                case "no":
                case "n":
                case "0":
                case "否":
                case "假":
                case "错":
                    normalized = "false";
                    return true;
                default:
                    normalized = "";
                    return false;
            }
        }

        private static bool IsExplicitBooleanWord(string value)
        {
            var lower = (value ?? "").Trim().ToLowerInvariant();
            return lower == "true" || lower == "false" || lower == "yes" || lower == "no" ||
                   lower == "y" || lower == "n" || lower == "是" || lower == "否" ||
                   lower == "真" || lower == "假" || lower == "对" || lower == "错";
        }

        private static bool TryNormalizeDate(string value, out string normalized)
        {
            var formats = new[] { "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d", "yyyy.MM.dd", "yyyy.M.d" };
            if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            }

            normalized = "";
            return false;
        }

        private static bool TryNormalizeDateTime(string value, out string normalized)
        {
            if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
            {
                normalized = dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                return true;
            }

            normalized = "";
            return false;
        }

        private static bool IsInteger(string value)
        {
            return long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        private static bool IsNumber(string value)
        {
            return TryParseDecimal(value, out _);
        }

        private static bool TryParseDecimal(string value, out decimal number)
        {
            return decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        private static bool LooksLikeJsonContainer(string value)
        {
            var trimmed = (value ?? "").Trim();
            return (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)) ||
                   (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal));
        }

        private static bool LooksLikeBalancedJson(string value)
        {
            var stack = new Stack<char>();
            var inString = false;
            var escaped = false;
            foreach (var c in value)
            {
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

                if (c == '{' || c == '[')
                {
                    stack.Push(c);
                }
                else if (c == '}' || c == ']')
                {
                    if (stack.Count == 0)
                    {
                        return false;
                    }

                    var open = stack.Pop();
                    if ((open == '{' && c != '}') || (open == '[' && c != ']'))
                    {
                        return false;
                    }
                }
            }

            return stack.Count == 0 && !inString && !escaped;
        }

        private static int ResolveDataStartRow(MatrixWorkbookImportOptions options, int fieldRow)
        {
            if (options.DataStartRow >= 0)
            {
                return options.DataStartRow;
            }

            var max = fieldRow;
            if (options.TypeRow >= 0)
            {
                max = Math.Max(max, options.TypeRow);
            }

            if (options.DescriptionRow >= 0)
            {
                max = Math.Max(max, options.DescriptionRow);
            }

            return max + 1;
        }

        private static int ClampRow(int row, int fallback)
        {
            return row < 0 ? fallback : row;
        }

        private static string GetCell(IList<IList<string>> matrix, int row, int column)
        {
            if (row < 0 || row >= matrix.Count || matrix[row] == null || column < 0 || column >= matrix[row].Count)
            {
                return "";
            }

            return matrix[row][column] ?? "";
        }

        private static string GetOptionalCell(IList<IList<string>> matrix, int row, int column)
        {
            return row < 0 ? "" : GetCell(matrix, row, column);
        }

        private static string MakeColumnKey(string displayName, int index)
        {
            var key = ColumnKeyPattern.Replace(displayName ?? "", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "column_" + (index + 1).ToString(CultureInfo.InvariantCulture);
            }

            if (char.IsDigit(key[0]))
            {
                key = "_" + key;
            }

            return key.ToLowerInvariant();
        }

        private static string ToColumnName(int zeroBasedIndex)
        {
            var dividend = zeroBasedIndex + 1;
            var columnName = new StringBuilder();
            while (dividend > 0)
            {
                var modulo = (dividend - 1) % 26;
                columnName.Insert(0, Convert.ToChar('A' + modulo));
                dividend = (dividend - modulo) / 26;
            }

            return columnName.ToString();
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

        private sealed class ColumnDescriptor
        {
            public int Index;
            public string Key = "";
            public string DisplayName = "";
            public string SourceColumn = "";
            public string TypeText = "";
            public string Description = "";
            public string ValueKind = "string";
            public string EnumTypeName = "";
            public List<string> AllowedEnumValues = new List<string>();
        }

        private sealed class TypeDeclaration
        {
            public string ValueKind = "";
            public string EnumTypeName = "";
            public List<string> EnumValues = new List<string>();
        }
    }
}
