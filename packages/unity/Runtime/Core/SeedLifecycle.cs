using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        public bool ConfirmCreateOnlineSheets { get; set; }
        public bool ConfirmRegistryUpsert { get; set; }
        public bool ConfirmSchemaReviews { get; set; }
        public bool ConfirmWriteLocalCache { get; set; }
        public bool ConfirmWriteProjectConfig { get; set; }
        public bool ConfirmExcelToSoSettings { get; set; }
        public bool ConfirmProjectConfigUpdate { get; set; }
        public bool ConfirmExcelToSoSettingsUpdate { get; set; }
        public bool CleanupDefaultRows { get; set; }
        public string TargetGitBranch { get; set; } = "";
        public string TargetFeishuProfile { get; set; } = "";
        public string TargetBranchWikiNodeTitle { get; set; } = "";
        public string SourceMode { get; set; } = "local-xlsx";
        public List<string> TableIds { get; set; } = new List<string>();
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
        public string SemanticHash { get; set; } = "";
        public string ProjectConfigPath { get; set; } = "";
        public string SpreadsheetToken { get; set; } = "";
        public string SpreadsheetUrl { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string SheetName { get; set; } = "";
        public string WikiRootToken { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public string WikiNodeUrl { get; set; } = "";
        public string Branch { get; set; } = "";
        public string Profile { get; set; } = "";
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
        public string WikiNodeUrl { get; set; } = "";
        public string Branch { get; set; } = "";
        public string Profile { get; set; } = "";
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
        public int UsedRowCount { get; set; }
        public int UsedColumnCount { get; set; }
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

    public sealed class TargetBranchBootstrapInputSummary
    {
        public string Fingerprint { get; set; } = "";
        public string TargetGitBranch { get; set; } = "";
        public string TargetFeishuProfile { get; set; } = "";
        public string TargetBranchWikiNodeTitle { get; set; } = "";
        public string SourceMode { get; set; } = "local-xlsx";
        public List<string> TableIds { get; set; } = new List<string>();
        public string TableIdsText { get; set; } = "";
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

    public interface ITargetBranchBootstrapPostflightPlatform
    {
        Task<LifecycleActionResult> ValidateTargetBranchBootstrapPostflightAsync(LifecycleContractRequest request, BranchWorkspaceResolution branchWorkspace, IList<SeedTableLifecycleResult> seedTables, CancellationToken cancellationToken);
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
            var tables = ResolveTables(request).ToList();
            if (tables.Count == 0)
            {
                result.AddFailure("没有找到要 seed 的配表。请在 contract.seedFromLocalXlsx.tables 中声明表，或提供 request.table/sourceXlsxPath。");
                return;
            }

            if (!ApplyTargetBranchBootstrapOverrides(request, seed, tables, result))
            {
                return;
            }

            seed.Tables = tables;

            if (IsTargetBranchBootstrap(request))
            {
                var inputSummary = BuildTargetBranchBootstrapInputSummary(request);
                ApplyTargetBootstrapRequestSummary(result, inputSummary, request, seed);
            }

            if (!request.DryRun && !IsTargetBranchBootstrap(request) && !seed.ConfirmApply)
            {
                result.AddFailure("seed-from-local-xlsx 会创建/绑定在线 Sheet 并回填本地配置。apply 模式必须显式确认：CLI 传 --yes，或 contract.seedFromLocalXlsx.confirmApply=true。");
                return;
            }

            if (!request.DryRun && IsTargetBranchBootstrap(request))
            {
                if (!CanCreateOnlineSheets(request, seed))
                {
                    result.AddFailure("初始化目标分支会创建/复用在线工作区和 Sheet。apply 前必须显式确认 confirmCreateOnlineSheets=true。");
                    return;
                }

                if (!CanRegistryUpsert(request, seed))
                {
                    result.AddFailure("初始化目标分支需要登记 BranchBindings / ConfigSheets。apply 前必须显式确认 confirmRegistryUpsert=true。");
                    return;
                }

                if (!CanSchemaReviews(request, seed))
                {
                    result.AddFailure("初始化目标分支需要登记 SchemaReviews baseline。apply 前必须显式确认 confirmSchemaReviews=true。");
                    return;
                }
            }

            var branchWorkspace = BranchWorkspaceResolver.Resolve(request);
            result.BranchWorkspace = branchWorkspace;
            result.Branch = branchWorkspace.GitBranch;
            BranchWorkspaceResolver.ValidateOneToOne(request, branchWorkspace, result);
            if (!result.Success)
            {
                return;
            }

            if (request.DryRun)
            {
                result.Actions.Add(BranchWorkspaceResolver.BuildAction(branchWorkspace, "planned", "预览：seed 会先使用/创建分支工作区节点，再把在线 Sheet 挂到该节点下。"));
                AddTargetBootstrapPlanAction(request, seed, tables, result, branchWorkspace);
            }
            else
            {
                var branchPlatform = platform as IBranchWorkspacePlatform;
                if (branchPlatform == null)
                {
                    result.AddFailure("seed-from-local-xlsx apply 需要平台支持 Branch Workspace Resolver，避免把在线 Sheet 直接挂到 Wiki 根节点。");
                    return;
                }

                branchWorkspace = await branchPlatform.EnsureBranchWorkspaceAsync(BranchWorkspaceResolver.NormalizeContract(request), branchWorkspace, cancellationToken).ConfigureAwait(false);
                result.BranchWorkspace = branchWorkspace;
                result.Actions.Add(BranchWorkspaceResolver.BuildAction(branchWorkspace, FirstNonEmpty(branchWorkspace.Status, "done"), "已解析/创建分支工作区节点，后续 Sheet 会挂到该节点下。"));
                if (string.IsNullOrWhiteSpace(branchWorkspace.WikiNodeToken))
                {
                    result.AddFailure("无法定位或创建分支工作区节点 “" + branchWorkspace.NodeTitle + "”。请检查 bot 的 wiki:node:create / wiki:node:retrieve 权限，以及根节点 “" + branchWorkspace.RootWikiTitle + "” 的共享权限。");
                    return;
                }

                if (!CanRegistryUpsert(request, seed))
                {
                    result.AddFailure("已定位目标工作区，但没有确认写 BranchBindings。请确认 confirmRegistryUpsert=true 后重试。");
                    return;
                }

                var binding = await branchPlatform.UpsertBranchBindingAsync(request.Registry, branchWorkspace, cancellationToken).ConfigureAwait(false);
                result.Actions.Add(binding);
                if (IsTargetBranchBootstrap(request) && string.Equals(binding.Status, "skipped", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddFailure("初始化目标分支需要写 BranchBindings，但当前 registry Base 未配置或被跳过。请检查 ProjectSettings 的 live registry/baseToken。");
                    return;
                }

                if (binding.Status == "failed")
                {
                    result.AddFailure(binding.Message);
                    return;
                }
            }

            foreach (var table in tables)
            {
                ApplyBranchWorkspace(seed, table, branchWorkspace);
                await ExecuteTableAsync(request, seedPlatform, seed, table, result, cancellationToken).ConfigureAwait(false);
            }

            if (IsTargetBranchBootstrap(request))
            {
                AddTargetBootstrapSummaryAction(request, seed, result, branchWorkspace);
                if (!request.DryRun && result.Success)
                {
                    await RunTargetBootstrapPostflightAsync(request, platform, result, branchWorkspace, cancellationToken).ConfigureAwait(false);
                }
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
            tableResult.Branch = table.Branch;
            tableResult.Profile = table.Profile;
            tableResult.WikiNodeUrl = table.WikiNodeUrl;
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
            table.SemanticHash = tableResult.SemanticHash;
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

            if (!CanCreateOnlineSheets(request, seed))
            {
                Fail(tableResult, result, "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "” 需要创建/复用在线 Sheet。请先确认 confirmCreateOnlineSheets=true。");
                tableResult.Status = "blocked";
                return;
            }

            if (!IsTargetBranchBootstrap(request) && NeedsExcelToSoUpdate(table) && !CanExcelToSoSettings(request, seed))
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
            tableResult.WikiNodeUrl = table.WikiNodeUrl;
            tableResult.ImportMode = online.ImportMode;
            tableResult.CapabilityDifference = online.CapabilityDifference;
            var createStatus = online.Reused ? "reused" : online.Created ? "done" : "ready";
            var createAction = Add(tableResult, result, "seed.sheet.import_or_create", createStatus, online.Reused ? "已找到现有在线 Sheet，将继续回填未完成步骤。" : "已创建或导入在线 Sheet。");
            createAction.Details["spreadsheetToken"] = online.SpreadsheetToken;
            createAction.Details["spreadsheetUrl"] = online.SpreadsheetUrl;
            createAction.Details["sheetId"] = online.SheetId;
            createAction.Details["wikiNodeToken"] = online.WikiNodeToken;
            createAction.Details["wikiNodeUrl"] = table.WikiNodeUrl;
            createAction.Details["importMode"] = online.ImportMode;
            createAction.Details["capabilityDifference"] = online.CapabilityDifference;
            createAction.Details["created"] = online.Created.ToString().ToLowerInvariant();
            createAction.Details["reused"] = online.Reused.ToString().ToLowerInvariant();
            if (HasErrors(online.Findings))
            {
                Fail(tableResult, result, "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "” 创建/导入飞书在线 Sheet 失败。请检查 bot scope、目标目录权限和导入权限。");
                tableResult.Status = "failed";
                return;
            }

            var linkAction = Add(tableResult, result, "seed.wiki.link", string.IsNullOrWhiteSpace(online.WikiNodeToken) ? "ready" : "done", "在线 Sheet 已按配置挂接到分支工作区节点 “" + FirstNonEmpty(result.BranchWorkspace.NodeTitle, seed.WikiParentTitle, "项目配置表") + "”。");
            linkAction.Details["branchNodeTitle"] = result.BranchWorkspace.NodeTitle;
            linkAction.Details["branchWikiNodeToken"] = result.BranchWorkspace.WikiNodeToken;
            linkAction.Details["branchWikiNodeUrl"] = result.BranchWorkspace.WikiNodeUrl;

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

            if (CanWriteLocalCache(request, seed))
            {
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
            }
            else
            {
                Add(tableResult, result, "seed.cache.write", "skipped", "未确认写本地 cache，已跳过 .config-sheet-forge/cache 和 excel-cache 写入。");
            }

            if (CanWriteProjectConfig(request, seed))
            {
                var config = await platform.UpdateProjectConfigAsync(seed, table, online, cancellationToken).ConfigureAwait(false);
                result.Actions.Add(config);
                tableResult.Actions.Add(config);
                if (IsFailedAction(config, tableResult, result))
                {
                    tableResult.Status = "failed";
                    return;
                }
            }
            else
            {
                Add(tableResult, result, "seed.project_config.update", "skipped", "未确认写 ProjectSettings，已跳过项目配置回填。");
            }

            if (!CanRegistryUpsert(request, seed))
            {
                Fail(tableResult, result, "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId) + "” 需要登记 ConfigSheets。请确认 confirmRegistryUpsert=true 后重试。");
                tableResult.Status = "blocked";
                return;
            }

            var registry = await platform.UpsertSeedRegistryRecordAsync(request.Registry, seed, table, online, cancellationToken).ConfigureAwait(false);
            tableResult.RegistryRecordId = FirstNonEmpty(GetDetail(registry, "recordId"), table.RegistryRecordId);
            result.RegistryRecordId = FirstNonEmpty(result.RegistryRecordId, tableResult.RegistryRecordId);
            result.Actions.Add(registry);
            tableResult.Actions.Add(registry);
            if (IsTargetBranchBootstrap(request) && string.Equals(registry.Status, "skipped", StringComparison.OrdinalIgnoreCase))
            {
                Fail(tableResult, result, "初始化目标分支需要写 ConfigSheets，但当前 registry Base 未配置或被跳过。请检查 ProjectSettings 的 live registry/baseToken。");
                tableResult.Status = "blocked";
                return;
            }

            if (IsFailedAction(registry, tableResult, result))
            {
                tableResult.Status = "failed";
                return;
            }

            if (CanSchemaReviews(request, seed))
            {
                var schemaReason = BuildSeedSchemaReviewReason(table, tableResult.SchemaChangeDetected, seed.BaselineStrategy);
                var schema = await platform.UpsertSeedSchemaReviewAsync(request.Registry, seed, table, request.Git, tableResult.SchemaChangeDetected, schemaReason, cancellationToken).ConfigureAwait(false);
                tableResult.SchemaReviewId = GetDetail(schema, "schemaReviewId");
                result.SchemaReviewId = FirstNonEmpty(result.SchemaReviewId, tableResult.SchemaReviewId);
                result.Actions.Add(schema);
                tableResult.Actions.Add(schema);
                if (IsTargetBranchBootstrap(request) && string.Equals(schema.Status, "skipped", StringComparison.OrdinalIgnoreCase))
                {
                    Fail(tableResult, result, "初始化目标分支需要写 SchemaReviews，但当前 registry Base 未配置、或该表被配置为不需要 SchemaReviews。请检查目标分支初始化策略。");
                    tableResult.Status = "blocked";
                    return;
                }

                if (IsFailedAction(schema, tableResult, result))
                {
                    tableResult.Status = "failed";
                    return;
                }
            }
            else
            {
                Add(tableResult, result, "seed.registry.schema_reviews.upsert", "skipped", "未确认登记 SchemaReviews，已跳过 schema baseline 回填。");
            }

            if (NeedsExcelToSoUpdate(table) && CanExcelToSoSettings(request, seed))
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
            else if (NeedsExcelToSoUpdate(table))
            {
                Add(tableResult, result, "seed.unity.excel_to_so.upsert", "skipped", "未确认更新 ExcelToSO settings，已跳过该写入。旧 Excel 源路径不会被改动。");
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
            var sheet = Add(tableResult, result, "seed.sheet.import_or_create", "planned", "预览：创建或复用飞书在线 Sheet；优先 drive import xlsx，失败时可 fallback 到 sheets create + values write，并记录能力差异。");
            sheet.Details["sourceXlsxPath"] = FirstNonEmpty(table.SourceXlsxPath, seed.SourceXlsxPath);
            sheet.Details["targetSheetTitle"] = FirstNonEmpty(table.DisplayName, table.TableId);
            sheet.Details["targetBranch"] = table.Branch;
            sheet.Details["targetProfile"] = table.Profile;
            sheet.Details["willCreateOrReuseOnlineSheet"] = "true";
            var wiki = Add(tableResult, result, "seed.wiki.link", "planned", "预览：把在线 Sheet 放到分支工作区 “" + FirstNonEmpty(result.BranchWorkspace.RootWikiTitle, seed.WikiParentTitle, "项目配置表") + "/" + FirstNonEmpty(result.BranchWorkspace.NodeTitle, "main") + "”。");
            wiki.Details["branchNodeTitle"] = result.BranchWorkspace.NodeTitle;
            wiki.Details["branchWikiNodeToken"] = result.BranchWorkspace.WikiNodeToken;
            wiki.Details["branchWikiNodeUrl"] = result.BranchWorkspace.WikiNodeUrl;
            wiki.Details["gitBranch"] = result.BranchWorkspace.GitBranch;
            wiki.Details["profile"] = result.BranchWorkspace.Profile;
            Add(tableResult, result, "seed.online_read", "planned", "预览：导入后必须在线回读 Sheet。");
            Add(tableResult, result, "seed.export_xlsx", "planned", "预览：导出在线 Sheet 为 xlsx，供三方比较使用。");
            Add(tableResult, result, "seed.triangulation_compare", "planned", "预览：比较 local xlsx semantic、online-read semantic、exported-xlsx semantic；任一不一致都会阻断写入。");
            var cache = Add(tableResult, result, "seed.cache.write_preview", "planned", "预览：三方一致后才可能写 .config-sheet-forge/excel-cache、semantic.json 和 sha256；hash 相同不改 mtime。");
            cache.Details["willWriteLocalCache"] = CanWriteLocalCache(request, seed).ToString().ToLowerInvariant();
            cache.Details["cacheXlsxPath"] = FirstNonEmpty(table.CacheXlsxPath, seed.ExcelCacheDirectory + "/" + table.TableId + ".xlsx");
            var project = Add(tableResult, result, "seed.project_config.preview", "planned", "预览：可回填 ProjectSettings/*ConfigSheetForge*.json 中目标 branch/profile 对应的 spreadsheetToken、sheetId、url、wikiNodeToken。");
            project.Details["willWriteProjectConfig"] = CanWriteProjectConfig(request, seed).ToString().ToLowerInvariant();
            project.Details["projectConfigPath"] = FirstNonEmpty(table.ProjectConfigPath, seed.ProjectConfigPath);
            var registry = Add(tableResult, result, "seed.registry.config_sheets.preview", "planned", "预览：按 TableId + Branch/Profile upsert Base ConfigSheets，忽略空白默认行，不依赖行顺序。");
            registry.Details["willUpsertRegistry"] = CanRegistryUpsert(request, seed).ToString().ToLowerInvariant();
            registry.Details["tableId"] = table.TableId;
            registry.Details["branch"] = table.Branch;
            registry.Details["profile"] = table.Profile;
            var schema = Add(tableResult, result, "seed.registry.schema_reviews.preview", "planned", "预览：创建 baseline/pending SchemaReviews 记录；schemaChangeDetected=" + tableResult.SchemaChangeDetected.ToString().ToLowerInvariant() + "。");
            schema.Details["willUpsertSchemaReviews"] = CanSchemaReviews(request, seed).ToString().ToLowerInvariant();
            var excelToSo = Add(tableResult, result, "seed.unity.excel_to_so.preview", "planned", "预览：只有显式确认后才追加/更新目标表的 ExcelToSO JSON/YAML settings。");
            excelToSo.Details["willUpdateExcelToSoSettings"] = CanExcelToSoSettings(request, seed).ToString().ToLowerInvariant();
            excelToSo.Details["settingsPath"] = table.UnityExcelToSo != null ? table.UnityExcelToSo.SettingsPath : "";
        }

        private static bool ApplyTargetBranchBootstrapOverrides(LifecycleContractRequest request, SeedFromLocalXlsxContract seed, List<SeedTableContract> tables, LifecycleContractResult result)
        {
            if (!IsTargetBranchBootstrap(request))
            {
                return true;
            }

            var bootstrap = request.TargetBranchBootstrap ?? new TargetBranchBootstrapContract();
            var sourceMode = FirstNonEmpty(bootstrap.SourceMode, seed.SourceMode, "local-xlsx");
            if (!string.Equals(sourceMode, "local-xlsx", StringComparison.OrdinalIgnoreCase))
            {
                result.AddFailure("初始化目标分支当前只支持 sourceMode=local-xlsx。请先选择本地 xlsx 源，或等后续版本支持 current-branch-cache/existing-profile。");
                return false;
            }

            request.Git ??= new ContractGitSpec();
            request.MergeInputs ??= new MergeInputsContract();
            request.BranchWorkspace ??= new BranchWorkspaceContract();
            request.TargetBranchBootstrap ??= bootstrap;

            var targetBranch = FirstNonEmpty(bootstrap.TargetGitBranch, seed.TargetGitBranch, request.MergeInputs.TargetBranch, request.BranchWorkspace.MainGitBranch, "main");
            var mainBranch = FirstNonEmpty(request.BranchWorkspace.MainGitBranch, "main");
            var targetProfile = FirstNonEmpty(bootstrap.TargetFeishuProfile, seed.TargetFeishuProfile, request.MergeInputs.TargetFeishuProfile, string.Equals(targetBranch, mainBranch, StringComparison.OrdinalIgnoreCase) ? FirstNonEmpty(request.BranchWorkspace.MainFeishuBranch, "main") : targetBranch);
            var targetTitle = FirstNonEmpty(bootstrap.TargetBranchWikiNodeTitle, seed.TargetBranchWikiNodeTitle, request.MergeInputs.TargetBranchWikiNodeTitle, string.Equals(targetBranch, mainBranch, StringComparison.OrdinalIgnoreCase) ? FirstNonEmpty(request.BranchWorkspace.MainNodeTitle, targetProfile, "main") : targetProfile);

            request.Git.Branch = targetBranch;
            request.Git.FeishuBranch = targetProfile;
            request.Git.Profile = targetProfile;
            request.BranchWorkspace.GitBranch = targetBranch;
            request.BranchWorkspace.FeishuBranch = targetProfile;
            request.BranchWorkspace.Profile = targetProfile;
            request.BranchWorkspace.MainFeishuBranch = string.Equals(targetBranch, mainBranch, StringComparison.OrdinalIgnoreCase) ? targetProfile : request.BranchWorkspace.MainFeishuBranch;
            if (string.Equals(targetBranch, mainBranch, StringComparison.OrdinalIgnoreCase))
            {
                request.BranchWorkspace.MainNodeTitle = targetTitle;
            }
            else if (!string.IsNullOrWhiteSpace(targetTitle))
            {
                request.BranchWorkspace.BranchNodeTitleTemplate = targetTitle;
            }

            request.MergeInputs.TargetBranch = targetBranch;
            request.MergeInputs.TargetFeishuProfile = targetProfile;
            request.MergeInputs.TargetBranchWikiNodeTitle = targetTitle;
            bootstrap.TargetGitBranch = targetBranch;
            bootstrap.TargetFeishuProfile = targetProfile;
            bootstrap.TargetBranchWikiNodeTitle = targetTitle;
            seed.TargetGitBranch = targetBranch;
            seed.TargetFeishuProfile = targetProfile;
            seed.TargetBranchWikiNodeTitle = targetTitle;
            seed.SourceMode = sourceMode;

            var selected = BuildSelectedTableSet(bootstrap.TableIds, seed.TableIds);
            if (selected.Count > 0)
            {
                tables.RemoveAll(t => !selected.Contains(t.TableId ?? ""));
                if (tables.Count == 0)
                {
                    result.AddFailure("初始化目标分支没有找到 tableIds 指定的配表：" + string.Join(", ", selected) + "。请确认项目配置 tables 中存在这些 TableId。");
                    return false;
                }
            }

            foreach (var table in tables)
            {
                var originalProfile = FirstNonEmpty(table.Profile, table.Branch);
                var originalBranch = table.Branch;
                var wasDifferentTarget = !string.IsNullOrWhiteSpace(originalProfile) && !string.Equals(originalProfile, targetProfile, StringComparison.OrdinalIgnoreCase);
                table.Branch = targetProfile;
                table.Profile = targetProfile;
                if (wasDifferentTarget || (!string.IsNullOrWhiteSpace(originalBranch) && !string.Equals(originalBranch, targetProfile, StringComparison.OrdinalIgnoreCase)))
                {
                    table.SpreadsheetToken = "";
                    table.SpreadsheetUrl = "";
                    table.SheetId = "";
                    table.WikiNodeToken = "";
                    table.WikiNodeUrl = "";
                    table.RegistryRecordId = "";
                }
            }

            return true;
        }

        private static void AddTargetBootstrapPlanAction(LifecycleContractRequest request, SeedFromLocalXlsxContract seed, List<SeedTableContract> tables, LifecycleContractResult result, BranchWorkspaceResolution branchWorkspace)
        {
            if (!IsTargetBranchBootstrap(request))
            {
                return;
            }

            var action = result.AddAction("target_branch.bootstrap.plan", "planned", "预览：初始化目标分支 “" + branchWorkspace.GitBranch + "” 的在线工作区和表定位；dry-run 不写飞书、不改本地文件。");
            action.Details["requestFingerprint"] = result.RequestFingerprint;
            action.Details["targetGitBranch"] = branchWorkspace.GitBranch;
            action.Details["targetFeishuProfile"] = FirstNonEmpty(branchWorkspace.Profile, branchWorkspace.FeishuBranch);
            action.Details["targetBranchWikiNodeTitle"] = branchWorkspace.NodeTitle;
            action.Details["targetBranchWikiNodeUrl"] = branchWorkspace.WikiNodeUrl;
            action.Details["sourceMode"] = FirstNonEmpty(request.TargetBranchBootstrap != null ? request.TargetBranchBootstrap.SourceMode : "", seed.SourceMode, "local-xlsx");
            action.Details["tableCount"] = tables.Count.ToString(CultureInfo.InvariantCulture);
            action.Details["tableIds"] = string.Join(", ", tables.Select(t => t.TableId));
            action.Details["confirmCreateOnlineSheets"] = CanCreateOnlineSheets(request, seed).ToString().ToLowerInvariant();
            action.Details["confirmRegistryUpsert"] = CanRegistryUpsert(request, seed).ToString().ToLowerInvariant();
            action.Details["confirmSchemaReviews"] = CanSchemaReviews(request, seed).ToString().ToLowerInvariant();
            action.Details["confirmWriteLocalCache"] = CanWriteLocalCache(request, seed).ToString().ToLowerInvariant();
            action.Details["confirmWriteProjectConfig"] = CanWriteProjectConfig(request, seed).ToString().ToLowerInvariant();
            action.Details["confirmExcelToSoSettings"] = CanExcelToSoSettings(request, seed).ToString().ToLowerInvariant();
        }

        private static HashSet<string> BuildSelectedTableSet(IEnumerable<string> first, IEnumerable<string> second)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in first ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value.Trim());
                }
            }

            foreach (var value in second ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value.Trim());
                }
            }

            return result;
        }

        public static TargetBranchBootstrapInputSummary BuildTargetBranchBootstrapInputSummary(LifecycleContractRequest request)
        {
            request = request ?? new LifecycleContractRequest();
            var seed = request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract();
            var bootstrap = request.TargetBranchBootstrap ?? new TargetBranchBootstrapContract();
            var workspace = request.BranchWorkspace ?? new BranchWorkspaceContract();
            var merge = request.MergeInputs ?? new MergeInputsContract();
            var targetBranch = FirstNonEmpty(bootstrap.TargetGitBranch, seed.TargetGitBranch, merge.TargetBranch, workspace.MainGitBranch, "main");
            var mainBranch = FirstNonEmpty(workspace.MainGitBranch, "main");
            var targetProfile = FirstNonEmpty(bootstrap.TargetFeishuProfile, seed.TargetFeishuProfile, merge.TargetFeishuProfile, string.Equals(targetBranch, mainBranch, StringComparison.OrdinalIgnoreCase) ? FirstNonEmpty(workspace.MainFeishuBranch, "main") : targetBranch);
            var targetTitle = FirstNonEmpty(bootstrap.TargetBranchWikiNodeTitle, seed.TargetBranchWikiNodeTitle, merge.TargetBranchWikiNodeTitle, string.Equals(targetBranch, mainBranch, StringComparison.OrdinalIgnoreCase) ? FirstNonEmpty(workspace.MainNodeTitle, targetProfile, "main") : targetProfile);
            var sourceMode = FirstNonEmpty(bootstrap.SourceMode, seed.SourceMode, "local-xlsx");
            var selected = BuildSelectedTableSet(bootstrap.TableIds, seed.TableIds);
            var tableLines = new List<string>();
            var tableIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceTables = seed.Tables != null && seed.Tables.Count > 0
                ? seed.Tables
                : BuildSingleTableList(request);
            foreach (var table in sourceTables)
            {
                if (table == null || string.IsNullOrWhiteSpace(table.TableId))
                {
                    continue;
                }

                if (selected.Count > 0 && !selected.Contains(table.TableId))
                {
                    continue;
                }

                tableIds.Add(table.TableId);
                tableLines.Add(string.Join("|", new[]
                {
                    NormalizeFingerprintValue(table.TableId),
                    NormalizeFingerprintValue(table.DisplayName),
                    NormalizeFingerprintValue(FirstNonEmpty(table.SourceXlsxPath, seed.SourceXlsxPath)),
                    NormalizeFingerprintValue(table.SheetName),
                    table.FieldRow.ToString(CultureInfo.InvariantCulture),
                    table.TypeRow.ToString(CultureInfo.InvariantCulture),
                    table.DescriptionRow.ToString(CultureInfo.InvariantCulture),
                    table.DataStartRow.ToString(CultureInfo.InvariantCulture),
                    NormalizeFingerprintValue(table.TreatUnknownTypesAsEnum.ToString().ToLowerInvariant())
                }));
            }

            tableLines.Sort(StringComparer.OrdinalIgnoreCase);
            var basis = new StringBuilder();
            basis.AppendLine("operation=bootstrap-target-branch-from-local-xlsx");
            basis.AppendLine("targetGitBranch=" + NormalizeFingerprintValue(targetBranch));
            basis.AppendLine("targetFeishuProfile=" + NormalizeFingerprintValue(targetProfile));
            basis.AppendLine("targetBranchWikiNodeTitle=" + NormalizeFingerprintValue(targetTitle));
            basis.AppendLine("sourceMode=" + NormalizeFingerprintValue(sourceMode));
            basis.AppendLine("tables=" + string.Join(",", tableLines));

            return new TargetBranchBootstrapInputSummary
            {
                Fingerprint = Sha256Hex(basis.ToString()),
                TargetGitBranch = targetBranch,
                TargetFeishuProfile = targetProfile,
                TargetBranchWikiNodeTitle = targetTitle,
                SourceMode = sourceMode,
                TableIds = tableIds.ToList(),
                TableIdsText = string.Join(", ", tableIds)
            };
        }

        private static List<SeedTableContract> BuildSingleTableList(LifecycleContractRequest request)
        {
            var result = new List<SeedTableContract>();
            if (request == null || request.Table == null || string.IsNullOrWhiteSpace(request.Table.TableId))
            {
                return result;
            }

            result.Add(new SeedTableContract
            {
                TableId = request.Table.TableId,
                DisplayName = request.Table.DisplayName,
                SourceXlsxPath = FirstNonEmpty(request.Table.SourceXlsxPath, request.Table.ExcelPath),
                SheetName = request.Table.SheetName,
                FieldRow = request.Table.FieldRow,
                TypeRow = request.Table.TypeRow,
                DescriptionRow = request.Table.DescriptionRow,
                DataStartRow = request.Table.DataStartRow,
                TreatUnknownTypesAsEnum = request.Table.TreatUnknownTypesAsEnum
            });
            return result;
        }

        private static void ApplyTargetBootstrapRequestSummary(LifecycleContractResult result, TargetBranchBootstrapInputSummary inputSummary, LifecycleContractRequest request, SeedFromLocalXlsxContract seed)
        {
            result.RequestFingerprint = inputSummary.Fingerprint;
            result.RequestSummary["targetGitBranch"] = inputSummary.TargetGitBranch;
            result.RequestSummary["targetFeishuProfile"] = inputSummary.TargetFeishuProfile;
            result.RequestSummary["targetBranchWikiNodeTitle"] = inputSummary.TargetBranchWikiNodeTitle;
            result.RequestSummary["sourceMode"] = inputSummary.SourceMode;
            result.RequestSummary["tableIds"] = inputSummary.TableIdsText;
            result.RequestSummary["confirmCreateOnlineSheets"] = CanCreateOnlineSheets(request, seed).ToString().ToLowerInvariant();
            result.RequestSummary["confirmRegistryUpsert"] = CanRegistryUpsert(request, seed).ToString().ToLowerInvariant();
            result.RequestSummary["confirmSchemaReviews"] = CanSchemaReviews(request, seed).ToString().ToLowerInvariant();
            result.RequestSummary["confirmWriteLocalCache"] = CanWriteLocalCache(request, seed).ToString().ToLowerInvariant();
            result.RequestSummary["confirmWriteProjectConfig"] = CanWriteProjectConfig(request, seed).ToString().ToLowerInvariant();
            result.RequestSummary["confirmExcelToSoSettings"] = CanExcelToSoSettings(request, seed).ToString().ToLowerInvariant();
        }

        private static void AddTargetBootstrapSummaryAction(LifecycleContractRequest request, SeedFromLocalXlsxContract seed, LifecycleContractResult result, BranchWorkspaceResolution branchWorkspace)
        {
            var created = result.SeedTables.Count(t => t.Actions.Any(a => a.Action == "seed.sheet.import_or_create" && string.Equals(GetDetail(a, "created"), "true", StringComparison.OrdinalIgnoreCase)));
            var reused = result.SeedTables.Count(t => t.Actions.Any(a => a.Action == "seed.sheet.import_or_create" && string.Equals(GetDetail(a, "reused"), "true", StringComparison.OrdinalIgnoreCase)));
            var action = result.AddAction("target_branch.bootstrap.summary", result.Success ? (request.DryRun ? "planned" : "done") : "blocked", result.Success ? "目标分支初始化摘要已生成。" : "目标分支初始化未完成，请先处理失败原因。");
            action.Details["requestFingerprint"] = result.RequestFingerprint;
            action.Details["targetBranch"] = branchWorkspace.GitBranch;
            action.Details["targetProfile"] = FirstNonEmpty(branchWorkspace.Profile, branchWorkspace.FeishuBranch);
            action.Details["branchNode"] = FirstNonEmpty(branchWorkspace.RootWikiTitle, seed.WikiParentTitle, "项目配置表") + "/" + branchWorkspace.NodeTitle;
            action.Details["branchWikiNodeToken"] = branchWorkspace.WikiNodeToken;
            action.Details["branchWikiNodeUrl"] = branchWorkspace.WikiNodeUrl;
            action.Details["tableCount"] = result.SeedTables.Count.ToString(CultureInfo.InvariantCulture);
            action.Details["createdTables"] = created.ToString(CultureInfo.InvariantCulture);
            action.Details["reusedTables"] = reused.ToString(CultureInfo.InvariantCulture);
            action.Details["tableIds"] = string.Join(", ", result.SeedTables.Select(t => t.TableId));
            action.Details["localCacheWrite"] = CanWriteLocalCache(request, seed) ? "confirmed" : "skipped";
            action.Details["projectConfigWrite"] = CanWriteProjectConfig(request, seed) ? "confirmed" : "skipped";
            action.Details["excelToSoWrite"] = CanExcelToSoSettings(request, seed) ? "confirmed" : "skipped";
        }

        private static async Task RunTargetBootstrapPostflightAsync(LifecycleContractRequest request, ILifecyclePlatform platform, LifecycleContractResult result, BranchWorkspaceResolution branchWorkspace, CancellationToken cancellationToken)
        {
            var postflightPlatform = platform as ITargetBranchBootstrapPostflightPlatform;
            if (postflightPlatform == null)
            {
                var skipped = result.AddAction("target_branch.bootstrap.postflight", "skipped", "当前平台没有实现 postflight；CLI apply 会重新读取注册中心做校验。");
                skipped.Details["postflightPassed"] = "unknown";
                return;
            }

            var action = await postflightPlatform.ValidateTargetBranchBootstrapPostflightAsync(request, branchWorkspace, result.SeedTables, cancellationToken).ConfigureAwait(false);
            result.Actions.Add(action);
            if (string.Equals(action.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                result.AddFailure(action.Message);
            }
        }

        private static string NormalizeFingerprintValue(string value)
        {
            return (value ?? "").Replace("\\", "/").Trim();
        }

        private static string Sha256Hex(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static bool IsTargetBranchBootstrap(LifecycleContractRequest request)
        {
            return string.Equals(request.Operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanCreateOnlineSheets(LifecycleContractRequest request, SeedFromLocalXlsxContract seed)
        {
            return IsTargetBranchBootstrap(request)
                ? (request.TargetBranchBootstrap != null && request.TargetBranchBootstrap.ConfirmCreateOnlineSheets) || seed.ConfirmCreateOnlineSheets
                : seed.ConfirmApply || seed.ConfirmCreateOnlineSheets;
        }

        private static bool CanRegistryUpsert(LifecycleContractRequest request, SeedFromLocalXlsxContract seed)
        {
            return IsTargetBranchBootstrap(request)
                ? (request.TargetBranchBootstrap != null && request.TargetBranchBootstrap.ConfirmRegistryUpsert) || seed.ConfirmRegistryUpsert
                : seed.ConfirmApply || seed.ConfirmRegistryUpsert;
        }

        private static bool CanSchemaReviews(LifecycleContractRequest request, SeedFromLocalXlsxContract seed)
        {
            return IsTargetBranchBootstrap(request)
                ? (request.TargetBranchBootstrap != null && request.TargetBranchBootstrap.ConfirmSchemaReviews) || seed.ConfirmSchemaReviews
                : seed.ConfirmApply || seed.ConfirmSchemaReviews;
        }

        private static bool CanWriteLocalCache(LifecycleContractRequest request, SeedFromLocalXlsxContract seed)
        {
            return IsTargetBranchBootstrap(request)
                ? (request.TargetBranchBootstrap != null && request.TargetBranchBootstrap.ConfirmWriteLocalCache) || seed.ConfirmWriteLocalCache
                : seed.ConfirmApply || seed.ConfirmWriteLocalCache;
        }

        private static bool CanWriteProjectConfig(LifecycleContractRequest request, SeedFromLocalXlsxContract seed)
        {
            return IsTargetBranchBootstrap(request)
                ? (request.TargetBranchBootstrap != null && request.TargetBranchBootstrap.ConfirmWriteProjectConfig) || seed.ConfirmWriteProjectConfig || seed.ConfirmProjectConfigUpdate
                : seed.ConfirmApply || seed.ConfirmWriteProjectConfig || seed.ConfirmProjectConfigUpdate;
        }

        private static bool CanExcelToSoSettings(LifecycleContractRequest request, SeedFromLocalXlsxContract seed)
        {
            return IsTargetBranchBootstrap(request)
                ? (request.TargetBranchBootstrap != null && request.TargetBranchBootstrap.ConfirmExcelToSoSettings) || seed.ConfirmExcelToSoSettings || seed.ConfirmExcelToSoSettingsUpdate
                : seed.ConfirmExcelToSoSettings || seed.ConfirmExcelToSoSettingsUpdate;
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
                SemanticHash = table.SemanticHash,
                ProjectConfigPath = table.ProjectConfigPath,
                SpreadsheetToken = table.SpreadsheetToken,
                SpreadsheetUrl = table.SpreadsheetUrl,
                SheetId = table.SheetId,
                SheetName = table.SheetName,
                WikiRootToken = table.WikiRootToken,
                WikiNodeToken = table.WikiNodeToken,
                WikiNodeUrl = table.WikiNodeUrl,
                Branch = table.Branch,
                Profile = table.Profile,
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

        private static void ApplyBranchWorkspace(SeedFromLocalXlsxContract seed, SeedTableContract table, BranchWorkspaceResolution branchWorkspace)
        {
            if (branchWorkspace == null)
            {
                return;
            }

            seed.WikiRootToken = FirstNonEmpty(branchWorkspace.WikiNodeToken, seed.WikiRootToken);
            seed.WikiParentTitle = FirstNonEmpty(branchWorkspace.NodeTitle, seed.WikiParentTitle);
            table.WikiRootToken = FirstNonEmpty(branchWorkspace.WikiNodeToken, table.WikiRootToken, seed.WikiRootToken);
            table.WikiNodeUrl = FirstNonEmpty(branchWorkspace.WikiNodeUrl, table.WikiNodeUrl);
            table.Branch = FirstNonEmpty(branchWorkspace.FeishuBranch, table.Branch);
            table.Profile = FirstNonEmpty(branchWorkspace.Profile, table.Profile);
            table.OwnerRole = FirstNonEmpty(table.OwnerRole, branchWorkspace.OwnerRole);
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
