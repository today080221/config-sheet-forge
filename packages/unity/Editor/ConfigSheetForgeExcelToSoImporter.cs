using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

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
        public string AssetDirectory = "";
        public string Namespace = "";
    }

    internal static class ConfigSheetForgeExcelToSoImporter
    {
        private const string ApiTypeFullName = "GreatClock.Common.ExcelToSO.ExcelToScriptableObjectApi";
        private static Type _cachedApiType;
        private static MethodInfo _cachedImportExcelPaths;

        public static ExcelToSoImportBackendStatus Probe()
        {
            var type = FindApiType();
            if (type == null)
            {
                return ExcelToSoImportBackendStatus.Missing("未安装 ExcelToSO 包，或版本低于 v1.0.4。请在 Packages/manifest.json 安装 com.greatclock.exceltoscriptableobject，并 pin 到 today080221/excel_to_scriptableobject#v1.0.4 或更新版本。");
            }

            var method = FindImportMethod(type);
            if (method == null)
            {
                return ExcelToSoImportBackendStatus.Missing("已发现 ExcelToSO，但没有找到 ExcelToScriptableObjectApi.ImportExcelPaths。请升级 ExcelToSO 到 v1.0.4 或更新版本。");
            }

            _cachedApiType = type;
            _cachedImportExcelPaths = method;
            return ExcelToSoImportBackendStatus.Found(type.FullName);
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
                resultObject = _cachedImportExcelPaths.Invoke(null, new object[] { new[] { excelPath }, null });
            }
            catch (TargetInvocationException ex)
            {
                var failed = new ExcelToSoSingleImportResult { Success = false, TableId = tableId ?? "", ExcelPath = excelPath ?? "" };
                failed.Errors.Add(ex.InnerException == null ? ex.ToString() : ex.InnerException.ToString());
                return failed;
            }
            catch (Exception ex)
            {
                var failed = new ExcelToSoSingleImportResult { Success = false, TableId = tableId ?? "", ExcelPath = excelPath ?? "" };
                failed.Errors.Add(ex.ToString());
                return failed;
            }

            return ConvertResult(tableId, excelPath, resultObject);
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
