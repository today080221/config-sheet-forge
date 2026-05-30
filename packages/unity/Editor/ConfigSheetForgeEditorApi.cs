using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ConfigSheetForge.Core;
using UnityEditor;
using UnityEngine;

namespace ConfigSheetForge.Unity.Editor
{
    [Serializable]
    internal sealed class UnityImportBridgeSummary
    {
        public int importedCount;
        public int importItemCount;
        public int tableCount;
        public int failedCount;
        public int skippedCount;
        public string profileId = ConfigSheetForgeExcelToSoImporter.SourceOfTruthProfileId;
        public string nextStep = "";
    }

    [Serializable]
    internal sealed class UnityImportBridgeItem
    {
        public bool success;
        public string tableId = "";
        public string excelPath = "";
        public string assetPath = "";
        public string[] errors = new string[0];
        public string[] warnings = new string[0];
    }

    [Serializable]
    internal sealed class UnityImportBridgeResult
    {
        public string operation = "unity-import-assets";
        public bool success;
        public string summary = "";
        public string nextAction = "";
        public string[] humanReadableFailures = new string[0];
        public string[] debugPreflight = new string[0];
        public UnityImportBridgeSummary unityImportSummary = new UnityImportBridgeSummary();
        public UnityImportBridgeItem[] items = new UnityImportBridgeItem[0];

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }
    }

    internal static class ConfigSheetForgeEditorImportRunner
    {
        public static UnityImportBridgeResult ImportUnityAssetsFromSourceOfTruthCache()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            return ImportUnityAssetsFromSourceOfTruthCache(projectRoot);
        }

        public static UnityImportBridgeResult ImportUnityAssetsFromSourceOfTruthCache(string projectRoot)
        {
            var failures = new List<string>();
            projectRoot = string.IsNullOrWhiteSpace(projectRoot)
                ? Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory()
                : projectRoot;

            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            {
                return Blocked("Unity 未连接到有效项目目录。请从 Unity thin bridge 打开 Desktop，或回 Unity 执行导入。");
            }

            if (!IsSyncCacheReady(projectRoot, out var syncReason))
            {
                return Blocked(syncReason);
            }

            var backend = ConfigSheetForgeExcelToSoImporter.Probe();
            if (!backend.Available)
            {
                return Blocked("ExcelToSO 未安装或版本过旧：" + backend.Message);
            }

            var projectConfig = ConfigSheetForgeEditorUtility.LoadProjectConfigSummary(projectRoot);
            var tableCount = (projectConfig.Tables ?? new List<ProjectConfigTableSummary>()).Select(table => table.TableId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count();
            var importItems = BuildImportItems(projectRoot, projectConfig);
            if (importItems.Count == 0)
            {
                return Blocked("没有找到可导入的配表 cache。请确认 ProjectSettings/*ConfigSheetForge*.json 中声明了 tables。");
            }

            var missingCache = importItems.Where(item => !File.Exists(item.CacheXlsxPath)).Select(item => item.TableId).ToList();
            if (missingCache.Count > 0)
            {
                return Blocked("Source of Truth cache xlsx 不完整，缺少：" + string.Join(", ", missingCache) + "。请先在 Desktop 写入本地 cache。");
            }

            var settingsPath = Path.Combine(projectRoot, "ProjectSettings", "ExcelToScriptableObjectSettings.asset");
            if (!File.Exists(settingsPath))
            {
                return Blocked("缺少 ExcelToSO settings。请先在 Unity 点击“安装/更新 SourceOfTruthCache profile”。");
            }

            var settingsText = File.ReadAllText(settingsPath);
            if (!HasSourceOfTruthProfile(settingsText))
            {
                return Blocked("SourceOfTruthCache profile 缺失。请先安装/更新 Source of Truth 导入 profile；它不会改变本地 Excel profile。");
            }

            var fieldRow = Math.Max(0, ExtractInt(settingsText, "field_row") ?? 0);
            var typeRow = Math.Max(0, ExtractInt(settingsText, "type_row") ?? 1);
            var dataStartRow = Math.Max(0, ExtractInt(settingsText, "data_from_row") ?? (typeRow + 2));
            var debugPreflight = ConfigSheetForgeExcelToSoImporter.BuildCacheImportPreflightDetails(
                importItems,
                fieldRow,
                typeRow,
                dataStartRow,
                settingsPath,
                ConfigSheetForgeExcelToSoImporter.SourceOfTruthProfileId);
            var cacheTypePreflight = ConfigSheetForgeExcelToSoImporter.InspectCacheTypes(importItems, typeRow, dataStartRow, fieldRow);
            if (!cacheTypePreflight.Ready)
            {
                var blocked = Blocked(cacheTypePreflight.Message);
                blocked.debugPreflight = debugPreflight;
                return blocked;
            }

            var imported = ConfigSheetForgeExcelToSoImporter.ImportSourceOfTruthProfile();
            var result = new UnityImportBridgeResult
            {
                success = imported.All(item => item.Success),
                nextAction = imported.All(item => item.Success) ? "run-pr-gate" : "fix-import",
                debugPreflight = debugPreflight,
                items = imported.Select(ToBridgeItem).ToArray()
            };
            result.unityImportSummary.importedCount = imported.Count(item => item.Success);
            result.unityImportSummary.importItemCount = imported.Count;
            result.unityImportSummary.tableCount = tableCount > 0 ? tableCount : importItems.Select(item => item.TableId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count();
            result.unityImportSummary.failedCount = imported.Count(item => !item.Success);
            result.unityImportSummary.skippedCount = 0;
            result.unityImportSummary.nextStep = result.success ? "运行 PR 检查。" : "修复失败表后重新导入。";
            if (!result.success)
            {
                failures.AddRange(imported.Where(item => !item.Success).SelectMany(item => item.Errors.Select(error => FirstNonEmpty(item.TableId, item.ExcelPath, "未知表") + ": " + error)));
            }

            result.humanReadableFailures = failures.ToArray();
            result.summary = result.success
                ? "导入成功：" + result.unityImportSummary.importItemCount + " 个 Unity 导入项，失败 0 个。对应在线表：" + result.unityImportSummary.tableCount + " 张。下一步：运行 PR 检查。"
                : "导入完成，但失败 " + result.unityImportSummary.failedCount + " 个 Unity 导入项。请查看失败表。";
            return result;
        }

        public static void ImportUnityAssetsFromSourceOfTruthCacheMenu()
        {
            var result = ImportUnityAssetsFromSourceOfTruthCache();
            EditorUtility.DisplayDialog(
                result.success ? "导入 Unity 配表资产完成" : "导入 Unity 配表资产未完成",
                result.summary + (result.humanReadableFailures.Length > 0 ? "\n\n" + string.Join("\n", result.humanReadableFailures.Take(8)) : ""),
                "知道了");
        }

        private static UnityImportBridgeResult Blocked(string reason)
        {
            return new UnityImportBridgeResult
            {
                success = false,
                summary = reason,
                nextAction = "fix-import",
                humanReadableFailures = new[] { reason },
                unityImportSummary = new UnityImportBridgeSummary
                {
                    importedCount = 0,
                    failedCount = 0,
                    skippedCount = 0,
                    nextStep = reason
                }
            };
        }

        private static bool IsSyncCacheReady(string projectRoot, out string reason)
        {
            var resultPath = FindLatestSyncCacheResultPath(projectRoot);
            if (!File.Exists(resultPath))
            {
                reason = "还没有找到最近一次 Desktop 同步预览结果。请先运行完整同步预览；cacheStatus=upToDate 后才能导入 Unity asset。";
                return false;
            }

            var json = StripBom(File.ReadAllText(resultPath));
            var success = ExtractBool(json, "success");
            if (success.HasValue && !success.Value)
            {
                reason = "最近一次 sync-cache 失败。请先修复同步预检问题，再导入 Unity asset。";
                return false;
            }

            var cacheStatus = FirstNonEmpty(ExtractString(json, "cacheStatus"), ExtractNestedString(json, "syncCacheSummary", "cacheStatus"));
            if (!string.Equals(cacheStatus, "upToDate", StringComparison.OrdinalIgnoreCase))
            {
                reason = "cache 还不是最新状态，当前为：" + FirstNonEmpty(cacheStatus, "未知") + "。请先在 Desktop 完成同步预览和写入本地 cache。";
                return false;
            }

            var blockedTables = ExtractStringArray(json, "blockedTables");
            if (blockedTables.Count > 0)
            {
                reason = "同步预检仍有阻断表：" + string.Join(", ", blockedTables) + "。请先修复后重新预览同步。";
                return false;
            }

            reason = "最近一次 sync-cache 成功，cacheStatus=upToDate。";
            return true;
        }

        private static string FindLatestSyncCacheResultPath(string projectRoot)
        {
            var resultDir = Path.Combine(projectRoot, "Temp", "ConfigSheetForge", "desktop");
            var candidates = new[]
            {
                Path.Combine(resultDir, "sync-cache-apply.result.json"),
                Path.Combine(resultDir, "sync-cache.result.json")
            };
            return candidates
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? candidates[1];
        }

        private static List<ExcelToSoImportItem> BuildImportItems(string projectRoot, ProjectConfigSummary projectConfig)
        {
            var tables = projectConfig.CurrentBranchTables != null && projectConfig.CurrentBranchTables.Count > 0
                ? projectConfig.CurrentBranchTables
                : projectConfig.Tables;
            var items = new List<ExcelToSoImportItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables ?? new List<ProjectConfigTableSummary>())
            {
                if (table == null || string.IsNullOrWhiteSpace(table.TableId) || !seen.Add(table.TableId))
                {
                    continue;
                }

                items.Add(new ExcelToSoImportItem
                {
                    TableId = table.TableId,
                    DisplayName = FirstNonEmpty(table.DisplayName, table.TableId),
                    CacheXlsxPath = ResolveCacheXlsxPath(projectRoot, table),
                    OldExcelPath = ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, table.OldExcelPath),
                    LocalExcelPath = ResolveLocalExcelPath(projectRoot, table),
                    ScriptDirectory = FirstNonEmpty(table.ScriptDirectory, projectConfig.UnityExcelToSoScriptDirectory),
                    AssetDirectory = FirstNonEmpty(table.AssetDirectory, projectConfig.UnityExcelToSoAssetDirectory),
                    Namespace = FirstNonEmpty(table.Namespace, projectConfig.UnityExcelToSoNamespace)
                });
            }

            return items;
        }

        private static UnityImportBridgeItem ToBridgeItem(ExcelToSoSingleImportResult item)
        {
            return new UnityImportBridgeItem
            {
                success = item.Success,
                tableId = item.TableId,
                excelPath = item.ExcelPath,
                assetPath = item.AssetPath,
                errors = item.Errors.ToArray(),
                warnings = item.Warnings.ToArray()
            };
        }

        private static bool HasSourceOfTruthProfile(string settingsText)
        {
            return settingsText.IndexOf(ConfigSheetForgeExcelToSoImporter.SourceOfTruthProfileId, StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (settingsText.IndexOf(".config-sheet-forge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    settingsText.IndexOf("excel-cache", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string ResolveCacheXlsxPath(string projectRoot, ProjectConfigTableSummary table)
        {
            if (table != null && !string.IsNullOrWhiteSpace(table.CacheXlsxPath))
            {
                return ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, table.CacheXlsxPath);
            }

            return Path.Combine(projectRoot, ".config-sheet-forge", "excel-cache", (table == null ? "" : table.TableId) + ".xlsx");
        }

        private static string ResolveLocalExcelPath(string projectRoot, ProjectConfigTableSummary table)
        {
            if (table != null && !string.IsNullOrWhiteSpace(table.OldExcelPath))
            {
                return ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, table.OldExcelPath);
            }

            return table != null && !string.IsNullOrWhiteSpace(table.ExcelPath) && table.ExcelPath.IndexOf(".config-sheet-forge", StringComparison.OrdinalIgnoreCase) < 0
                ? ConfigSheetForgeEditorUtility.ResolveProjectPath(projectRoot, table.ExcelPath)
                : Path.Combine(projectRoot, "Excel", (table == null ? "" : table.TableId) + ".xlsx");
        }

        private static string StripBom(string value)
        {
            return !string.IsNullOrEmpty(value) && value[0] == '\uFEFF' ? value.Substring(1) : value;
        }

        private static bool? ExtractBool(string json, string key)
        {
            var token = ExtractLiteral(json, key);
            if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        private static int? ExtractInt(string json, string key)
        {
            var token = ExtractLiteral(json, key);
            return int.TryParse(token, out var value) ? value : (int?)null;
        }

        private static string ExtractNestedString(string json, string objectKey, string key)
        {
            var objectIndex = json.IndexOf("\"" + objectKey + "\"", StringComparison.OrdinalIgnoreCase);
            return objectIndex < 0 ? "" : ExtractString(json.Substring(objectIndex), key);
        }

        private static string ExtractString(string json, string key)
        {
            var searchFrom = 0;
            var marker = "\"" + key + "\"";
            while (searchFrom < json.Length)
            {
                var index = json.IndexOf(marker, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return "";
                var colon = json.IndexOf(':', index + marker.Length);
                if (colon < 0) return "";
                var cursor = colon + 1;
                while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
                if (cursor < json.Length && json[cursor] == '"')
                {
                    return ReadJsonString(json, cursor);
                }

                searchFrom = cursor + 1;
            }

            return "";
        }

        private static string ExtractLiteral(string json, string key)
        {
            var marker = "\"" + key + "\"";
            var index = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return "";
            var colon = json.IndexOf(':', index + marker.Length);
            if (colon < 0) return "";
            var cursor = colon + 1;
            while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
            var start = cursor;
            while (cursor < json.Length && (char.IsLetterOrDigit(json[cursor]) || json[cursor] == '-' || json[cursor] == '_')) cursor++;
            return cursor > start ? json.Substring(start, cursor - start) : "";
        }

        private static List<string> ExtractStringArray(string json, string key)
        {
            var result = new List<string>();
            var marker = "\"" + key + "\"";
            var index = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return result;
            var start = json.IndexOf('[', index + marker.Length);
            var end = start < 0 ? -1 : json.IndexOf(']', start + 1);
            if (start < 0 || end < 0) return result;
            var cursor = start + 1;
            while (cursor < end)
            {
                var quote = json.IndexOf('"', cursor);
                if (quote < 0 || quote > end) break;
                result.Add(ReadJsonString(json, quote));
                var close = quote + 1;
                while (close < end)
                {
                    if (json[close] == '"' && json[close - 1] != '\\')
                    {
                        break;
                    }

                    close++;
                }

                cursor = close + 1;
            }

            return result;
        }

        private static string ReadJsonString(string json, int quote)
        {
            var builder = new StringBuilder();
            for (var i = quote + 1; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"') break;
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    builder.Append(json[i]);
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
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
