using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace ConfigSheetForge.Unity.Editor
{
    internal sealed class ExcelToSoImportBackendStatus
    {
        public bool Available;
        public string Message = "";
        public string ApiTypeName = "";

        public static ExcelToSoImportBackendStatus Missing(string message)
        {
            return new ExcelToSoImportBackendStatus { Available = false, Message = message ?? "" };
        }

        public static ExcelToSoImportBackendStatus Found(string apiTypeName)
        {
            return new ExcelToSoImportBackendStatus { Available = true, Message = "ExcelToSO public API 可用。", ApiTypeName = apiTypeName ?? "" };
        }
    }

    internal sealed class ExcelToSoSingleImportResult
    {
        public bool Success;
        public string TableId = "";
        public string ExcelPath = "";
        public string AssetPath = "";
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    internal sealed class ExcelToSoImportItem
    {
        public string TableId = "";
        public string DisplayName = "";
        public string CacheXlsxPath = "";
        public string OldExcelPath = "";
        public string LocalExcelPath = "";
        public string ScriptDirectory = "";
        public string AssetDirectory = "";
        public string Namespace = "";
    }

    internal sealed class ExcelToSoCacheTypePreflight
    {
        public bool Ready;
        public string ShortStatus = "";
        public string Message = "";
        public readonly List<string> BlockingCells = new List<string>();
    }

    internal static class ConfigSheetForgeExcelToSoImporter
    {
        private const string ApiTypeFullName = "GreatClock.Common.ExcelToSO.ExcelToScriptableObjectApi";
        public const string SourceOfTruthProfileId = "SourceOfTruthCache";
        private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static Type _cachedApiType;
        private static MethodInfo _cachedImportExcelPaths;
        private static MethodInfo _cachedImportByProfile;

        public static ExcelToSoImportBackendStatus Probe()
        {
            var type = FindApiType();
            if (type == null)
            {
                return ExcelToSoImportBackendStatus.Missing("未安装 ExcelToSO 包，或版本低于 v1.0.6。请在 Packages/manifest.json 安装 com.greatclock.exceltoscriptableobject，并 pin 到 today080221/excel_to_scriptableobject#v1.0.6 或更新版本。");
            }

            var method = FindImportByProfileMethod(type);
            if (method == null)
            {
                return ExcelToSoImportBackendStatus.Missing("已发现 ExcelToSO，但没有找到 ExcelToScriptableObjectApi.ImportByProfile。请升级 ExcelToSO 到 v1.0.6 或更新版本。");
            }

            _cachedApiType = type;
            _cachedImportByProfile = method;
            return ExcelToSoImportBackendStatus.Found(type.FullName);
        }

        public static List<ExcelToSoSingleImportResult> ImportSourceOfTruthProfile()
        {
            var status = Probe();
            if (!status.Available)
            {
                var missing = new ExcelToSoSingleImportResult { Success = false, TableId = SourceOfTruthProfileId };
                missing.Errors.Add(status.Message);
                return new List<ExcelToSoSingleImportResult> { missing };
            }

            object resultObject;
            try
            {
                resultObject = _cachedImportByProfile.Invoke(null, new object[] { SourceOfTruthProfileId, null });
            }
            catch (TargetInvocationException ex)
            {
                var failed = new ExcelToSoSingleImportResult { Success = false, TableId = SourceOfTruthProfileId };
                failed.Errors.Add(FormatImportException(ex.InnerException ?? ex));
                return new List<ExcelToSoSingleImportResult> { failed };
            }
            catch (Exception ex)
            {
                var failed = new ExcelToSoSingleImportResult { Success = false, TableId = SourceOfTruthProfileId };
                failed.Errors.Add(FormatImportException(ex));
                return new List<ExcelToSoSingleImportResult> { failed };
            }

            return ConvertResults(resultObject);
        }

        public static ExcelToSoSingleImportResult ImportExcelPath(string tableId, string excelPath)
        {
            var status = Probe();
            if (!status.Available)
            {
                var missing = new ExcelToSoSingleImportResult { Success = false, TableId = tableId ?? "", ExcelPath = excelPath ?? "" };
                missing.Errors.Add(status.Message);
                return missing;
            }

            object resultObject;
            try
            {
                var method = FindImportMethod(_cachedApiType);
                if (method == null)
                {
                    var failed = new ExcelToSoSingleImportResult { Success = false, TableId = tableId ?? "", ExcelPath = excelPath ?? "" };
                    failed.Errors.Add("已发现 ExcelToSO，但没有找到 ExcelToScriptableObjectApi.ImportExcelPaths。");
                    return failed;
                }

                resultObject = method.Invoke(null, new object[] { new[] { excelPath }, null });
            }
            catch (TargetInvocationException ex)
            {
                var failed = new ExcelToSoSingleImportResult { Success = false, TableId = tableId ?? "", ExcelPath = excelPath ?? "" };
                failed.Errors.Add(FormatImportException(ex.InnerException ?? ex));
                return failed;
            }
            catch (Exception ex)
            {
                var failed = new ExcelToSoSingleImportResult { Success = false, TableId = tableId ?? "", ExcelPath = excelPath ?? "" };
                failed.Errors.Add(FormatImportException(ex));
                return failed;
            }

            return ConvertResult(tableId, excelPath, resultObject);
        }

        public static ExcelToSoCacheTypePreflight InspectCacheTypes(IEnumerable<ExcelToSoImportItem> items, int typeRow)
        {
            var preflight = new ExcelToSoCacheTypePreflight { Ready = true, ShortStatus = "cache 类型可导入", Message = "cache xlsx 类型行可被 ExcelToSO 导入。" };
            if (items == null)
            {
                return preflight;
            }

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.CacheXlsxPath) || !File.Exists(item.CacheXlsxPath))
                {
                    continue;
                }

                List<string> row;
                try
                {
                    var compatibilityError = InspectOpenXmlCompatibility(item.CacheXlsxPath);
                    if (!string.IsNullOrWhiteSpace(compatibilityError))
                    {
                        preflight.Ready = false;
                        preflight.BlockingCells.Add(FirstNonEmpty(item.TableId, Path.GetFileNameWithoutExtension(item.CacheXlsxPath)) + ": " + compatibilityError);
                        continue;
                    }

                    row = ReadTypeRow(item.CacheXlsxPath, Math.Max(0, typeRow));
                }
                catch (Exception ex)
                {
                    preflight.Ready = false;
                    preflight.BlockingCells.Add(FirstNonEmpty(item.TableId, Path.GetFileNameWithoutExtension(item.CacheXlsxPath)) + ": 无法读取类型行 - " + ex.Message);
                    continue;
                }

                for (var i = 0; i < row.Count; i++)
                {
                    var token = (row[i] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(token) || IsExcelToSoReadableType(token))
                    {
                        continue;
                    }

                    var cell = ToColumnName(i) + (Math.Max(0, typeRow) + 1).ToString(CultureInfo.InvariantCulture);
                    preflight.Ready = false;
                    preflight.BlockingCells.Add(FirstNonEmpty(item.TableId, Path.GetFileNameWithoutExtension(item.CacheXlsxPath)) + "!" + cell + "=" + token + SuggestExcelToSoType(token));
                }
            }

            if (!preflight.Ready)
            {
                preflight.ShortStatus = "cache 类型需要处理";
                preflight.Message = "当前 cache 类型行不适合 ExcelToSO 导入：" + string.Join(", ", preflight.BlockingCells.Take(12)) +
                                    (preflight.BlockingCells.Count > 12 ? " ..." : "") +
                                    "\n请先在 Config Sheet Forge Desktop 点击“修复 cache 类型行”。它会根据 project config/adapter schema 的 excelToSoType、seed/import 保存的 originalType，或旧 Excel 类型行，把 json 还原为 int[]、float[]、string[] 或 string。";
            }

            return preflight;
        }

        private static string InspectOpenXmlCompatibility(string xlsxPath)
        {
            using (var archive = ZipFile.OpenRead(xlsxPath))
            {
                var missing = new List<string>();
                if (archive.GetEntry("xl/sharedStrings.xml") == null)
                {
                    missing.Add("xl/sharedStrings.xml");
                }

                if (archive.GetEntry("xl/styles.xml") == null)
                {
                    missing.Add("xl/styles.xml");
                }

                if (missing.Count == 0)
                {
                    return "";
                }

                return "cache xlsx 缺少 ExcelToSO 读取所需的 OpenXML 部件：" + string.Join(", ", missing) +
                       "。请先在 Config Sheet Forge Desktop 点击“修复 cache 类型行”，重新生成 ExcelToSO 兼容 cache。";
            }
        }

        private static string SuggestExcelToSoType(string token)
        {
            var normalized = (token ?? "").Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "json":
                    return "（建议：先用 Desktop 修复为 int[] / float[] / string[] / string）";
                case "date":
                case "datetime":
                case "date_time":
                case "timestamp":
                case "enum":
                    return "（建议：在 schema 中明确 ExcelToSO 可导入类型，例如 string）";
                default:
                    return "（建议：修复为 ExcelToSO 支持的类型）";
            }
        }

        private static string FormatImportException(Exception ex)
        {
            var message = ex == null ? "未知异常" : ex.GetType().Name + ": " + ex.Message;
            if (ex is NullReferenceException)
            {
                return "ExcelToSO 读取 cache xlsx 时失败：" + message +
                       "。这通常表示 xlsx OpenXML 结构不兼容 ExcelToSO。请先在 Config Sheet Forge Desktop 执行“修复 cache 类型行”，然后重试导入。";
            }

            return "ExcelToSO 导入失败：" + message;
        }

        private static Type FindApiType()
        {
            if (_cachedApiType != null)
            {
                return _cachedApiType;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(ApiTypeFullName, throwOnError: false);
                }
                catch
                {
                    // Some editor assemblies can throw while reflecting; ignore and keep scanning.
                }

                if (type != null)
                {
                    _cachedApiType = type;
                    return type;
                }
            }

            return null;
        }

        private static bool IsExcelToSoReadableType(string token)
        {
            var normalized = (token ?? "").Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "json":
                case "date":
                case "datetime":
                case "date_time":
                case "timestamp":
                case "enum":
                    return false;
                case "bool":
                case "boolean":
                case "int":
                case "int32":
                case "integer":
                case "ints":
                case "int[]":
                case "[int]":
                case "int32s":
                case "int32[]":
                case "[int32]":
                case "integers":
                case "integer[]":
                case "[integer]":
                case "float":
                case "double":
                case "decimal":
                case "number":
                case "floats":
                case "float[]":
                case "[float]":
                case "numbers":
                case "number[]":
                case "[number]":
                case "long":
                case "int64":
                case "longs":
                case "long[]":
                case "[long]":
                case "int64s":
                case "int64[]":
                case "[int64]":
                case "vector2":
                case "vector3":
                case "vector4":
                case "rect":
                case "rectangle":
                case "color":
                case "colour":
                case "string":
                case "str":
                case "text":
                case "strings":
                case "string[]":
                case "[string]":
                case "texts":
                case "text[]":
                case "[text]":
                case "lang":
                case "language":
                case "langs":
                case "lang[]":
                case "[lang]":
                case "rich":
                case "richs":
                case "riches":
                case "rich[]":
                case "[rich]":
                    return true;
                default:
                    return true;
            }
        }

        private static List<string> ReadTypeRow(string xlsxPath, int typeRow)
        {
            using (var archive = ZipFile.OpenRead(xlsxPath))
            {
                var sharedStrings = ReadSharedStrings(archive);
                var sheetPath = ReadWorkbookSheetPath(archive);
                var matrix = ReadWorksheetMatrix(archive, sheetPath, sharedStrings);
                return typeRow >= 0 && typeRow < matrix.Count ? matrix[typeRow] : new List<string>();
            }
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var values = new List<string>();
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return values;
            }

            var document = ReadXml(entry);
            foreach (var item in document.Descendants(SpreadsheetNs + "si"))
            {
                values.Add(string.Concat(item.Descendants(SpreadsheetNs + "t").Select(t => t.Value)));
            }

            return values;
        }

        private static string ReadWorkbookSheetPath(ZipArchive archive)
        {
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry == null)
            {
                return "xl/worksheets/sheet1.xml";
            }

            var rels = ReadRelationships(archive, "xl/_rels/workbook.xml.rels");
            var workbook = ReadXml(workbookEntry);
            var sheet = workbook.Descendants(SpreadsheetNs + "sheet").FirstOrDefault();
            if (sheet == null)
            {
                return "xl/worksheets/sheet1.xml";
            }

            var relationId = (string)sheet.Attribute(RelationshipNs + "id") ?? "";
            return rels.TryGetValue(relationId, out var target) ? NormalizeWorkbookTarget(target) : "xl/worksheets/sheet1.xml";
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

        private static List<List<string>> ReadWorksheetMatrix(ZipArchive archive, string sheetPath, IList<string> sharedStrings)
        {
            var entry = archive.GetEntry(sheetPath);
            if (entry == null)
            {
                return new List<List<string>>();
            }

            var rows = new SortedDictionary<int, SortedDictionary<int, string>>();
            foreach (var cell in ReadXml(entry).Descendants(SpreadsheetNs + "c"))
            {
                var reference = (string)cell.Attribute("r") ?? "";
                if (!TryParseA1(reference, out var rowIndex, out var columnIndex))
                {
                    continue;
                }

                if (!rows.TryGetValue(rowIndex, out var row))
                {
                    row = new SortedDictionary<int, string>();
                    rows[rowIndex] = row;
                }

                row[columnIndex] = ReadCellValue(cell, sharedStrings);
            }

            var matrix = new List<List<string>>();
            if (rows.Count == 0)
            {
                return matrix;
            }

            var maxRow = rows.Keys.Max();
            var maxColumn = rows.Values.SelectMany(row => row.Keys).DefaultIfEmpty(0).Max();
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

        private static string ReadCellValue(XElement cell, IList<string> sharedStrings)
        {
            var type = (string)cell.Attribute("t") ?? "";
            if (type == "inlineStr")
            {
                return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(t => t.Value));
            }

            var raw = (string)cell.Element(SpreadsheetNs + "v") ?? "";
            if (type == "s" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sharedStrings.Count)
            {
                return sharedStrings[index];
            }

            return type == "b" ? raw == "1" ? "true" : "false" : raw;
        }

        private static XDocument ReadXml(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            {
                return XDocument.Load(stream, LoadOptions.None);
            }
        }

        private static bool TryParseA1(string reference, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            var columnNumber = 0;
            var index = 0;
            while (index < reference.Length && char.IsLetter(reference[index]))
            {
                columnNumber = columnNumber * 26 + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
                index++;
            }

            if (columnNumber <= 0 || !int.TryParse(reference.Substring(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber) || rowNumber <= 0)
            {
                return false;
            }

            row = rowNumber - 1;
            column = columnNumber - 1;
            return true;
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

        private static string ToColumnName(int index)
        {
            var columnNumber = index + 1;
            var name = "";
            while (columnNumber > 0)
            {
                var modulo = (columnNumber - 1) % 26;
                name = Convert.ToChar('A' + modulo) + name;
                columnNumber = (columnNumber - modulo) / 26;
            }

            return name;
        }

        private static MethodInfo FindImportMethod(Type apiType)
        {
            if (_cachedImportExcelPaths != null)
            {
                return _cachedImportExcelPaths;
            }

            if (apiType == null)
            {
                return null;
            }

            foreach (var method in apiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "ImportExcelPaths", StringComparison.Ordinal))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length == 2)
                {
                    _cachedImportExcelPaths = method;
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo FindImportByProfileMethod(Type apiType)
        {
            if (_cachedImportByProfile != null)
            {
                return _cachedImportByProfile;
            }

            if (apiType == null)
            {
                return null;
            }

            foreach (var method in apiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "ImportByProfile", StringComparison.Ordinal))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string))
                {
                    _cachedImportByProfile = method;
                    return method;
                }
            }

            return null;
        }

        private static List<ExcelToSoSingleImportResult> ConvertResults(object resultObject)
        {
            var results = new List<ExcelToSoSingleImportResult>();
            if (resultObject == null)
            {
                var failed = new ExcelToSoSingleImportResult { Success = false, TableId = SourceOfTruthProfileId };
                failed.Errors.Add("ExcelToSO ImportByProfile 返回空结果。");
                results.Add(failed);
                return results;
            }

            var items = ReadValue(resultObject, "items") as IEnumerable;
            if (items == null)
            {
                var single = new ExcelToSoSingleImportResult { Success = ReadBool(resultObject, "success"), TableId = SourceOfTruthProfileId };
                if (!single.Success)
                {
                    single.Errors.Add("ExcelToSO 导入失败，但结果里没有具体表项。");
                }
                results.Add(single);
                return results;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                var converted = new ExcelToSoSingleImportResult();
                converted.Success = true;
                converted.TableId = ReadString(item, "tableId");
                converted.ExcelPath = ReadString(item, "excelPath");
                converted.AssetPath = ReadString(item, "assetPath");
                converted.Errors.AddRange(ReadStringEnumerable(item, "errors"));
                converted.Warnings.AddRange(ReadStringEnumerable(item, "warnings"));
                var status = ReadValue(item, "status");
                if (status != null && !string.Equals(status.ToString(), "Imported", StringComparison.OrdinalIgnoreCase))
                {
                    converted.Success = false;
                }
                if (!converted.Success && converted.Errors.Count == 0)
                {
                    converted.Errors.Add("ExcelToSO 导入失败。请在程序视图展开详细日志查看 ExcelToSO 返回结果。");
                }
                results.Add(converted);
            }

            if (results.Count == 0)
            {
                var empty = new ExcelToSoSingleImportResult { Success = false, TableId = SourceOfTruthProfileId };
                empty.Errors.Add("ExcelToSO SourceOfTruthCache profile 没有返回任何导入表项。");
                results.Add(empty);
            }

            return results;
        }

        private static ExcelToSoSingleImportResult ConvertResult(string tableId, string excelPath, object resultObject)
        {
            var converted = new ExcelToSoSingleImportResult { TableId = tableId ?? "", ExcelPath = excelPath ?? "" };
            if (resultObject == null)
            {
                converted.Errors.Add("ExcelToSO ImportExcelPaths 返回空结果。");
                return converted;
            }

            converted.Success = ReadBool(resultObject, "success");
            var items = ReadValue(resultObject, "items") as IEnumerable;
            if (items == null)
            {
                if (!converted.Success)
                {
                    converted.Errors.Add("ExcelToSO 导入失败，但结果里没有具体表项。");
                }

                return converted;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                converted.TableId = FirstNonEmpty(ReadString(item, "tableId"), converted.TableId);
                converted.ExcelPath = FirstNonEmpty(ReadString(item, "excelPath"), converted.ExcelPath);
                converted.AssetPath = FirstNonEmpty(ReadString(item, "assetPath"), converted.AssetPath);
                converted.Errors.AddRange(ReadStringEnumerable(item, "errors"));
                converted.Warnings.AddRange(ReadStringEnumerable(item, "warnings"));
                var status = ReadValue(item, "status");
                if (status != null && !string.Equals(status.ToString(), "Imported", StringComparison.OrdinalIgnoreCase))
                {
                    converted.Success = false;
                }
            }

            if (!converted.Success && converted.Errors.Count == 0)
            {
                converted.Errors.Add("ExcelToSO 导入失败。请在程序视图展开详细日志查看 ExcelToSO 返回结果。");
            }

            return converted;
        }

        private static object ReadValue(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property != null)
            {
                return property.GetValue(target, null);
            }

            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return field == null ? null : field.GetValue(target);
        }

        private static bool ReadBool(object target, string name)
        {
            var value = ReadValue(target, name);
            if (value is bool direct)
            {
                return direct;
            }

            bool parsed;
            return bool.TryParse(value == null ? "" : value.ToString(), out parsed) && parsed;
        }

        private static string ReadString(object target, string name)
        {
            var value = ReadValue(target, name);
            return value == null ? "" : value.ToString();
        }

        private static IEnumerable<string> ReadStringEnumerable(object target, string name)
        {
            var value = ReadValue(target, name) as IEnumerable;
            if (value == null)
            {
                yield break;
            }

            foreach (var item in value)
            {
                if (item != null)
                {
                    yield return item.ToString();
                }
            }
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
}
