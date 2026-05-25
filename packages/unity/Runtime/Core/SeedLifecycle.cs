using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConfigSheetForge.Core
{
    public sealed class SeedFromLocalXlsxContract
    {
        public string SourceXlsxPath { get; set; } = "";
        public string CacheDirectory { get; set; } = ".config-sheet-forge/cache";
        public string ExcelCacheDirectory { get; set; } = ".config-sheet-forge/excel-cache";
        public string ProjectConfigPath { get; set; } = "";
        public string WikiRootToken { get; set; } = "";
        public string WikiParentTitle { get; set; } = "项目配置表";
        public string BaselineStrategy { get; set; } = "pending";
        public bool PreferDriveImport { get; set; } = true;
        public bool ConfirmApply { get; set; }
        public bool ConfirmProjectConfigUpdate { get; set; }
        public bool ConfirmExcelToSoSettingsUpdate { get; set; }
        public bool CleanupDefaultRows { get; set; }
        public List<SeedTableContract> Tables { get; set; } = new List<SeedTableContract>();
    }

    public sealed class SeedTableContract
    {
        public string TableId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SourceXlsxPath { get; set; } = "";
        public string CacheXlsxPath { get; set; } = "";
        public string SemanticCachePath { get; set; } = "";
        public string HashCachePath { get; set; } = "";
        public string ProjectConfigPath { get; set; } = "";
        public string SpreadsheetToken { get; set; } = "";
        public string SpreadsheetUrl { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string SheetName { get; set; } = "";
        public string WikiRootToken { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public string OwnerRole { get; set; } = "";
        public string RegistryRecordId { get; set; } = "";
        public bool SchemaReviewRequired { get; set; } = true;
        public int FieldRow { get; set; } = 0;
        public int TypeRow { get; set; } = -1;
        public int DescriptionRow { get; set; } = -1;
        public int DataStartRow { get; set; } = -1;
        public bool TreatUnknownTypesAsEnum { get; set; }
        public List<ContractFieldSpec> Fields { get; set; } = new List<ContractFieldSpec>();
        public UnityExcelToSoContract UnityExcelToSo { get; set; } = new UnityExcelToSoContract();
    }

    public sealed class SeedTableLifecycleResult
    {
        public string TableId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = "";
        public string SourceXlsxPath { get; set; } = "";
        public string CacheXlsxPath { get; set; } = "";
        public string SemanticCachePath { get; set; } = "";
        public string HashCachePath { get; set; } = "";
        public string SemanticHash { get; set; } = "";
        public string SpreadsheetToken { get; set; } = "";
        public string SpreadsheetUrl { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public string RegistryRecordId { get; set; } = "";
        public string SchemaReviewId { get; set; } = "";
        public bool SchemaChangeDetected { get; set; }
        public string ImportMode { get; set; } = "";
        public string CapabilityDifference { get; set; } = "";
        public List<LifecycleActionResult> Actions { get; set; } = new List<LifecycleActionResult>();
        public List<ValidationFinding> PortableSubsetFindings { get; set; } = new List<ValidationFinding>();
        public List<string> HumanReadableFailures { get; set; } = new List<string>();
        public List<string> TriangulationDiffSummary { get; set; } = new List<string>();
    }

    public sealed class SeedLocalWorkbookResult
    {
        public WorkbookDocument Workbook { get; set; }
        public string SourceXlsxPath { get; set; } = "";
        public string SemanticHash { get; set; } = "";
        public List<ValidationFinding> Findings { get; set; } = new List<ValidationFinding>();
    }

    public sealed class SeedOnlineSheetResult
    {
        public string SpreadsheetToken { get; set; } = "";
        public string SpreadsheetUrl { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public bool Created { get; set; }
        public bool Reused { get; set; }
        public string ImportMode { get; set; } = "";
        public string CapabilityDifference { get; set; } = "";
        public List<ValidationFinding> Findings { get; set; } = new List<ValidationFinding>();
    }

    public sealed class SeedOnlineRoundTripResult
    {
        public WorkbookDocument OnlineWorkbook { get; set; }
        public WorkbookDocument ExportedXlsxWorkbook { get; set; }
        public string ExportedXlsxPath { get; set; } = "";
        public List<ValidationFinding> Findings { get; set; } = new List<ValidationFinding>();
    }

    public interface ISeedFromLocalXlsxPlatform
    {
        Task<SeedLocalWorkbookResult> ReadLocalXlsxAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, CancellationToken cancellationToken);
        Task<SeedOnlineSheetResult> EnsureOnlineSheetAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, WorkbookDocument localWorkbook, string semanticHash, CancellationToken cancellationToken);
        Task<SeedOnlineRoundTripResult> ReadAndExportOnlineSheetAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken);
        Task<LifecycleActionResult> WriteSeedCacheAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, WorkbookDocument localWorkbook, string semanticHash, string exportedXlsxPath, CancellationToken cancellationToken);
        Task<LifecycleActionResult> UpdateProjectConfigAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken);
        Task<LifecycleActionResult> UpsertSeedRegistryRecordAsync(RegistryContract registry, SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken);
        Task<LifecycleActionResult> UpsertSeedSchemaReviewAsync(RegistryContract registry, SeedFromLocalXlsxContract seed, SeedTableContract table, ContractGitSpec git, bool schemaChangeDetected, string reason, CancellationToken cancellationToken);
        Task<LifecycleActionResult> UpdateExcelToSoSettingsAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, CancellationToken cancellationToken);
    }

    public static class SeedFromLocalXlsxLifecycle
    {
        public static async Task ExecuteAsync(LifecycleContractRequest request, ILifecyclePlatform platform, LifecycleContractResult result, CancellationToken cancellationToken)
        {
            var seedPlatform = platform as ISeedFromLocalXlsxPlatform;
            if (seedPlatform == null)
            {
                result.AddFailure("seed-from-local-xlsx 需要支持本地 xlsx 读取和飞书写入的平台实现。CLI 支持该 operation；纯 preview platform 只能做 new-table 预览。");
                return;
            }

            var seed = request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract();
            var tables = ResolveTables(request);
            if (tables.Count == 0)
            {
                result.AddFailure("没有找到要 seed 的配表。请在 contract.seedFromLocalXlsx.tables 中声明表，或提供 request.table/sourceXlsxPath。");
                return;
            }

            if (!request.DryRun && !seed.ConfirmApply)
            {
                result.AddFailure("seed-from-local-xlsx 会创建/绑定在线 Sheet 并回填本地配置。apply 模式必须显式确认：CLI 传 --yes，或 contract.seedFromLocalXlsx.confirmApply=true。");
                return;
            }

            foreach (var table in tables)
            {
                await ExecuteTableAsync(request, seedPlatform, seed, table, result, cancellationToken).ConfigureAwait(false);
            }
        }

        public static IList<SeedTableContract> ResolveTables(LifecycleContractRequest request)
        {
            var seed = request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract();
            if (seed.Tables.Count > 0)
            {
                foreach (var table in seed.Tables)
                {
                    ApplySeedDefaults(seed, table, request.UnityExcelToSo);
                }

                return seed.Tables;
            }

            if (string.IsNullOrWhiteSpace(request.Table.TableId) && string.IsNullOrWhiteSpace(seed.SourceXlsxPath) && string.IsNullOrWhiteSpace(request.Table.SourceXlsxPath))
            {
                return new List<SeedTableContract>();
            }

            var single = FromContractTable(request.Table);
            single.SourceXlsxPath = FirstNonEmpty(single.SourceXlsxPath, seed.SourceXlsxPath, request.UnityExcelToSo.ExcelPath, request.Table.ExcelPath);
            single.ProjectConfigPath = FirstNonEmpty(single.ProjectConfigPath, seed.ProjectConfigPath);
            single.UnityExcelToSo = request.UnityExcelToSo ?? new UnityExcelToSoContract();
            ApplySeedDefaults(seed, single, request.UnityExcelToSo);
            return new List<SeedTableContract> { single };
        }

        private static async Task ExecuteTableAsync(LifecycleContractRequest request, ISeedFromLocalXlsxPlatform platform, SeedFromLocalXlsxContract seed, SeedTableContract table, LifecycleContractResult result, CancellationToken cancellationToken)
        {
            var tableResult = new SeedTableLifecycleResult
            {
                TableId = table.TableId,
                DisplayName = FirstNonEmpty(table.DisplayName, table.TableId),
                Status = "running",
                SourceXlsxPath = table.SourceXlsxPath
            };
            result.SeedTables.Add(tableResult);

            Add(tableResult, result, "seed.portable_preflight", request.DryRun ? "planned" : "running", "检查本地 xlsx 是否只包含可稳定迁移的便携内容。");

            var local = await platform.ReadLocalXlsxAsync(seed, table, cancellationToken).ConfigureAwait(false);
            AddFindings(tableResult, result, table, local.Findings);
            if (local.Workbook == null)
            {
                Fail(tableResult, result, "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "” 无法读取本地 xlsx。请关闭 Excel/Unity 占用，确认文件存在后重试。");
                tableResult.Status = "blocked";
                return;
            }

            var structure = PortableStructureValidator.Validate(local.Workbook);
            AddFindings(tableResult, result, table, structure.Findings);
            var portable = PortableSubsetValidator.Validate(local.Workbook);
            AddFindings(tableResult, result, table, portable.Findings);

            tableResult.SemanticHash = FirstNonEmpty(local.SemanticHash, SemanticHasher.ComputeHash(local.Workbook));
            tableResult.SchemaChangeDetected = DetectSchemaChange(local.Workbook, table.Fields);
            Add(tableResult, result, "seed.local_xlsx.normalize", structure.HasErrors || portable.HasErrors || HasErrors(local.Findings) ? "blocked" : "done", "已把本地 xlsx 归一化为 semantic workbook，hash=" + tableResult.SemanticHash + "。");

            if (HasErrors(local.Findings) || structure.HasErrors || portable.HasErrors)
            {
                Fail(tableResult, result, "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "” 没有通过本地 xlsx 便携子集检查。请按单元格位置修正后重试。");
                AddDryRunOrBlockedPlan(request, seed, table, tableResult, result);
                tableResult.Status = "blocked";
                return;
            }

            if (request.DryRun)
            {
                AddDryRunOrBlockedPlan(request, seed, table, tableResult, result);
                tableResult.Status = "planned";
                return;
            }

            if (NeedsExcelToSoUpdate(table) && !seed.ConfirmExcelToSoSettingsUpdate)
            {
                Fail(tableResult, result, "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "” 需要更新 ExcelToSO settings。apply 模式必须显式确认：CLI 传 --confirm-excel-to-so，或 contract.seedFromLocalXlsx.confirmExcelToSoSettingsUpdate=true。");
                tableResult.Status = "blocked";
                return;
            }

            var online = await platform.EnsureOnlineSheetAsync(seed, table, local.Workbook, tableResult.SemanticHash, cancellationToken).ConfigureAwait(false);
            AddFindings(tableResult, result, table, online.Findings);
            tableResult.SpreadsheetToken = online.SpreadsheetToken;
            tableResult.SpreadsheetUrl = online.SpreadsheetUrl;
            tableResult.SheetId = online.SheetId;
            tableResult.WikiNodeToken = online.WikiNodeToken;
            tableResult.ImportMode = online.ImportMode;
            tableResult.CapabilityDifference = online.CapabilityDifference;
            var createStatus = online.Reused ? "reused" : online.Created ? "done" : "ready";
            var createAction = Add(tableResult, result, "seed.sheet.import_or_create", createStatus, online.Reused ? "已找到现有在线 Sheet，将继续回填未完成步骤。" : "已创建或导入在线 Sheet。");
            createAction.Details["spreadsheetToken"] = online.SpreadsheetToken;
            createAction.Details["spreadsheetUrl"] = online.SpreadsheetUrl;
            createAction.Details["sheetId"] = online.SheetId;
            createAction.Details["wikiNodeToken"] = online.WikiNodeToken;
            createAction.Details["importMode"] = online.ImportMode;
            createAction.Details["capabilityDifference"] = online.CapabilityDifference;
            if (HasErrors(online.Findings))
            {
                Fail(tableResult, result, "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "” 创建/导入飞书在线 Sheet 失败。请检查 bot scope、目标目录权限和导入权限。");
                tableResult.Status = "failed";
                return;
            }

            Add(tableResult, result, "seed.wiki.link", string.IsNullOrWhiteSpace(online.WikiNodeToken) ? "ready" : "done", "在线 Sheet 已按配置挂接到 Wiki/root；若 provider 只返回 Sheet token，请在详情中核对位置。");

            var roundTrip = await platform.ReadAndExportOnlineSheetAsync(seed, table, online, cancellationToken).ConfigureAwait(false);
            AddFindings(tableResult, result, table, roundTrip.Findings);
            Add(tableResult, result, "seed.online_read", roundTrip.OnlineWorkbook == null ? "failed" : "done", "已从在线 Sheet 回读 semantic workbook。");
            Add(tableResult, result, "seed.export_xlsx", string.IsNullOrWhiteSpace(roundTrip.ExportedXlsxPath) ? "failed" : "done", "已从在线 Sheet 导出 xlsx 并做 semantic normalize。");
            if (roundTrip.OnlineWorkbook == null || roundTrip.ExportedXlsxWorkbook == null || HasErrors(roundTrip.Findings))
            {
                Fail(tableResult, result, "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "” 在线回读或导出失败。不会写 cache、ProjectSettings 或 Base。");
                tableResult.Status = "failed";
                return;
            }

            var triangulation = SemanticTriangulator.Compare(roundTrip.OnlineWorkbook, roundTrip.ExportedXlsxWorkbook, SemanticTriangulator.Normalize(local.Workbook));
            foreach (var diff in triangulation.DiffSummary)
            {
                tableResult.TriangulationDiffSummary.Add(diff);
            }

            Add(tableResult, result, "seed.triangulation_compare", triangulation.Passed ? "passed" : "failed", triangulation.Passed ? "三方一致：local xlsx、online-read、exported-xlsx 语义完全一致。" : "三方比较不一致，已阻断写入。");
            if (!triangulation.Passed)
            {
                foreach (var diff in triangulation.DiffSummary)
                {
                    Fail(tableResult, result, diff);
                }

                tableResult.Status = "failed";
                return;
            }

            var cache = await platform.WriteSeedCacheAsync(seed, table, local.Workbook, tableResult.SemanticHash, roundTrip.ExportedXlsxPath, cancellationToken).ConfigureAwait(false);
            tableResult.CacheXlsxPath = GetDetail(cache, "xlsxPath");
            tableResult.SemanticCachePath = GetDetail(cache, "semanticPath");
            tableResult.HashCachePath = GetDetail(cache, "shaPath");
            result.Actions.Add(cache);
            tableResult.Actions.Add(cache);
            if (IsFailedAction(cache, tableResult, result))
            {
                tableResult.Status = "failed";
                return;
            }

            var config = await platform.UpdateProjectConfigAsync(seed, table, online, cancellationToken).ConfigureAwait(false);
            result.Actions.Add(config);
            tableResult.Actions.Add(config);
            if (IsFailedAction(config, tableResult, result))
            {
                tableResult.Status = "failed";
                return;
            }

            var registry = await platform.UpsertSeedRegistryRecordAsync(request.Registry, seed, table, online, cancellationToken).ConfigureAwait(false);
            tableResult.RegistryRecordId = FirstNonEmpty(GetDetail(registry, "recordId"), table.RegistryRecordId);
            result.RegistryRecordId = FirstNonEmpty(result.RegistryRecordId, tableResult.RegistryRecordId);
            result.Actions.Add(registry);
            tableResult.Actions.Add(registry);
            if (IsFailedAction(registry, tableResult, result))
            {
                tableResult.Status = "failed";
                return;
            }

            var schemaReason = BuildSeedSchemaReviewReason(table, tableResult.SchemaChangeDetected, seed.BaselineStrategy);
            var schema = await platform.UpsertSeedSchemaReviewAsync(request.Registry, seed, table, request.Git, tableResult.SchemaChangeDetected, schemaReason, cancellationToken).ConfigureAwait(false);
            tableResult.SchemaReviewId = GetDetail(schema, "schemaReviewId");
            result.SchemaReviewId = FirstNonEmpty(result.SchemaReviewId, tableResult.SchemaReviewId);
            result.Actions.Add(schema);
            tableResult.Actions.Add(schema);
            if (IsFailedAction(schema, tableResult, result))
            {
                tableResult.Status = "failed";
                return;
            }

            if (NeedsExcelToSoUpdate(table))
            {
                var excelToSo = await platform.UpdateExcelToSoSettingsAsync(seed, table, cancellationToken).ConfigureAwait(false);
                result.Actions.Add(excelToSo);
                tableResult.Actions.Add(excelToSo);
                if (IsFailedAction(excelToSo, tableResult, result))
                {
                    tableResult.Status = "failed";
                    return;
                }
            }

            result.SpreadsheetToken = FirstNonEmpty(result.SpreadsheetToken, tableResult.SpreadsheetToken);
            result.SpreadsheetUrl = FirstNonEmpty(result.SpreadsheetUrl, tableResult.SpreadsheetUrl);
            result.SheetId = FirstNonEmpty(result.SheetId, tableResult.SheetId);
            result.WikiNodeToken = FirstNonEmpty(result.WikiNodeToken, tableResult.WikiNodeToken);
            result.OnlineSheetUrl = FirstNonEmpty(result.OnlineSheetUrl, tableResult.SpreadsheetUrl);
            tableResult.Status = "done";
        }

        private static void AddDryRunOrBlockedPlan(LifecycleContractRequest request, SeedFromLocalXlsxContract seed, SeedTableContract table, SeedTableLifecycleResult tableResult, LifecycleContractResult result)
        {
            Add(tableResult, result, "seed.sheet.import_or_create", "planned", "预览：创建或导入飞书在线 Sheet；优先 drive import xlsx，失败时可 fallback 到 sheets create + values write，并记录能力差异。");
            Add(tableResult, result, "seed.wiki.link", "planned", "预览：把在线 Sheet 放到指定 Wiki/root 下的 “" + FirstNonEmpty(seed.WikiParentTitle, "项目配置表") + "”。");
            Add(tableResult, result, "seed.online_read", "planned", "预览：导入后必须在线回读 Sheet。");
            Add(tableResult, result, "seed.export_xlsx", "planned", "预览：导出在线 Sheet 为 xlsx，供三方比较使用。");
            Add(tableResult, result, "seed.triangulation_compare", "planned", "预览：比较 local xlsx semantic、online-read semantic、exported-xlsx semantic；任一不一致都会阻断写入。");
            Add(tableResult, result, "seed.cache.write_preview", "planned", "预览：三方一致后才写 .config-sheet-forge/excel-cache、semantic.json 和 sha256；hash 相同不改 mtime。");
            Add(tableResult, result, "seed.project_config.preview", "planned", "预览：回填 ProjectSettings/*ConfigSheetForge*.json 中的 spreadsheetToken、sheetId 和 url。");
            Add(tableResult, result, "seed.registry.config_sheets.preview", "planned", "预览：按 TableId upsert Base ConfigSheets，忽略空白默认行，不依赖行顺序。");
            Add(tableResult, result, "seed.registry.schema_reviews.preview", "planned", "预览：创建 baseline/pending SchemaReviews 记录；schemaChangeDetected=" + tableResult.SchemaChangeDetected.ToString().ToLowerInvariant() + "。");
            Add(tableResult, result, "seed.unity.excel_to_so.preview", "planned", "预览：只追加/更新目标表的 ExcelToSO JSON/YAML settings；apply 需要显式确认。");
        }

        private static SeedTableContract FromContractTable(ContractTableSpec table)
        {
            var seedTable = new SeedTableContract
            {
                TableId = table.TableId,
                DisplayName = table.DisplayName,
                SourceXlsxPath = FirstNonEmpty(table.SourceXlsxPath, table.ExcelPath),
                CacheXlsxPath = FirstNonEmpty(table.CacheXlsxPath, table.LocalCachePath),
                SemanticCachePath = table.SemanticCachePath,
                HashCachePath = table.HashCachePath,
                ProjectConfigPath = table.ProjectConfigPath,
                SpreadsheetToken = table.SpreadsheetToken,
                SpreadsheetUrl = table.SpreadsheetUrl,
                SheetId = table.SheetId,
                SheetName = table.SheetName,
                WikiRootToken = table.WikiRootToken,
                WikiNodeToken = table.WikiNodeToken,
                OwnerRole = table.OwnerRole,
                RegistryRecordId = table.RegistryRecordId,
                SchemaReviewRequired = table.SchemaReviewRequired,
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            };

            foreach (var field in table.Fields)
            {
                seedTable.Fields.Add(field);
            }

            return seedTable;
        }

        private static void ApplySeedDefaults(SeedFromLocalXlsxContract seed, SeedTableContract table, UnityExcelToSoContract defaultUnity)
        {
            table.SourceXlsxPath = FirstNonEmpty(table.SourceXlsxPath, seed.SourceXlsxPath);
            table.ProjectConfigPath = FirstNonEmpty(table.ProjectConfigPath, seed.ProjectConfigPath);
            table.WikiRootToken = FirstNonEmpty(table.WikiRootToken, seed.WikiRootToken);
            if (table.UnityExcelToSo == null)
            {
                table.UnityExcelToSo = new UnityExcelToSoContract();
            }

            if (defaultUnity != null)
            {
                table.UnityExcelToSo.SettingsPath = FirstNonEmpty(table.UnityExcelToSo.SettingsPath, defaultUnity.SettingsPath);
                table.UnityExcelToSo.TableId = FirstNonEmpty(table.UnityExcelToSo.TableId, defaultUnity.TableId, table.TableId);
                table.UnityExcelToSo.ExcelPath = FirstNonEmpty(table.UnityExcelToSo.ExcelPath, defaultUnity.ExcelPath, table.CacheXlsxPath);
                table.UnityExcelToSo.ScriptableObjectType = FirstNonEmpty(table.UnityExcelToSo.ScriptableObjectType, defaultUnity.ScriptableObjectType);
                table.UnityExcelToSo.AssetPath = FirstNonEmpty(table.UnityExcelToSo.AssetPath, defaultUnity.AssetPath);
                foreach (var pair in defaultUnity.ExtraFields)
                {
                    if (!table.UnityExcelToSo.ExtraFields.ContainsKey(pair.Key))
                    {
                        table.UnityExcelToSo.ExtraFields[pair.Key] = pair.Value;
                    }
                }
            }
        }

        private static bool DetectSchemaChange(WorkbookDocument workbook, IList<ContractFieldSpec> expectedFields)
        {
            if (workbook == null || expectedFields == null || expectedFields.Count == 0)
            {
                return false;
            }

            var actual = workbook.Sheets.SelectMany(s => s.Columns)
                .Select(c => (c.Key ?? "").Trim().ToLowerInvariant() + ":" + (c.ValueKind ?? "").Trim().ToLowerInvariant())
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToArray();
            var expected = expectedFields
                .Select(f => FirstNonEmpty(f.Key, f.DisplayName).Trim().ToLowerInvariant() + ":" + FirstNonEmpty(f.ValueKind, "string").Trim().ToLowerInvariant())
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToArray();
            return actual.Length != expected.Length || !actual.SequenceEqual(expected);
        }

        private static string BuildSeedSchemaReviewReason(SeedTableContract table, bool schemaChangeDetected, string baselineStrategy)
        {
            var status = schemaChangeDetected ? "检测到 schema 与 contract/ExcelToSO 预期不一致" : "初始 seed baseline";
            return "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "” 从本地 xlsx 初始化为在线 Sheet。策略：" + FirstNonEmpty(baselineStrategy, "pending") + "；" + status + "。";
        }

        private static bool NeedsExcelToSoUpdate(SeedTableContract table)
        {
            return table != null && table.UnityExcelToSo != null && !string.IsNullOrWhiteSpace(table.UnityExcelToSo.SettingsPath);
        }

        private static void AddFindings(SeedTableLifecycleResult tableResult, LifecycleContractResult result, SeedTableContract table, IEnumerable<ValidationFinding> findings)
        {
            foreach (var finding in findings)
            {
                if (!finding.Details.ContainsKey("tableId"))
                {
                    finding.Details["tableId"] = table.TableId;
                }

                tableResult.PortableSubsetFindings.Add(finding);
                if (finding.Severity == FindingSeverity.Error)
                {
                    var message = "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "”：" + finding.Message;
                    tableResult.HumanReadableFailures.Add(message);
                    result.AddFailure(message);
                }
            }
        }

        private static LifecycleActionResult Add(SeedTableLifecycleResult tableResult, LifecycleContractResult result, string action, string status, string message)
        {
            var lifecycleAction = new LifecycleActionResult
            {
                Action = action,
                Status = status,
                Message = message
            };
            lifecycleAction.Details["tableId"] = tableResult.TableId;
            if (!string.IsNullOrWhiteSpace(tableResult.DisplayName))
            {
                lifecycleAction.Details["displayName"] = tableResult.DisplayName;
            }

            tableResult.Actions.Add(lifecycleAction);
            result.Actions.Add(lifecycleAction);
            return lifecycleAction;
        }

        private static void Fail(SeedTableLifecycleResult tableResult, LifecycleContractResult result, string message)
        {
            tableResult.HumanReadableFailures.Add(message);
            result.AddFailure(message);
        }

        private static bool HasErrors(IEnumerable<ValidationFinding> findings)
        {
            return findings != null && findings.Any(f => f.Severity == FindingSeverity.Error);
        }

        private static bool IsFailedAction(LifecycleActionResult action, SeedTableLifecycleResult tableResult, LifecycleContractResult result)
        {
            if (action == null || !string.Equals(action.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Fail(tableResult, result, action.Message);
            return true;
        }

        private static string GetDetail(LifecycleActionResult action, string key)
        {
            return action != null && action.Details.TryGetValue(key, out var value) ? value : "";
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
