using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConfigSheetForge.Core
{
    public sealed class LifecycleContractRequest
    {
        public string Operation { get; set; } = "";
        public string Locale { get; set; } = "zh-Hans";
        public bool DryRun { get; set; }
        public RegistryContract Registry { get; set; } = new RegistryContract();
        public ContractTableSpec Table { get; set; } = new ContractTableSpec();
        public ContractGitSpec Git { get; set; } = new ContractGitSpec();
        public UnityExcelToSoContract UnityExcelToSo { get; set; } = new UnityExcelToSoContract();
        public MergePolicyContract MergePolicy { get; set; } = new MergePolicyContract();
        public PrGateReport GateReport { get; set; } = new PrGateReport();
        public SeedFromLocalXlsxContract SeedFromLocalXlsx { get; set; } = new SeedFromLocalXlsxContract();
        public SyncCacheContract SyncCache { get; set; } = new SyncCacheContract();
        public BranchWorkspaceContract BranchWorkspace { get; set; } = new BranchWorkspaceContract();
        public string GateReportPath { get; set; } = "";
        public string ReportPath { get; set; } = "";
        public List<BranchBindingContract> BranchBindings { get; set; } = new List<BranchBindingContract>();
        public Dictionary<string, string> DocumentationLinks { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class RegistryContract
    {
        public string BaseToken { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string RegistryRecordId { get; set; } = "";
        public Dictionary<string, string> TableIds { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> DisplayNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> FieldDisplayNames { get; set; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ContractTableSpec
    {
        public string TableId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ExcelPath { get; set; } = "";
        public string SourceXlsxPath { get; set; } = "";
        public string CacheXlsxPath { get; set; } = "";
        public string SemanticCachePath { get; set; } = "";
        public string HashCachePath { get; set; } = "";
        public string ProjectConfigPath { get; set; } = "";
        public string RegistryRecordId { get; set; } = "";
        public string LocalCachePath { get; set; } = "";
        public string SpreadsheetToken { get; set; } = "";
        public string SpreadsheetUrl { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string SheetName { get; set; } = "";
        public string WikiRootToken { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public string WikiNodeUrl { get; set; } = "";
        public string Branch { get; set; } = "";
        public string Profile { get; set; } = "";
        public string SemanticHash { get; set; } = "";
        public string OwnerRole { get; set; } = "";
        public bool SchemaReviewRequired { get; set; } = true;
        public string OnlineSheetUrl { get; set; } = "";
        public string Status { get; set; } = "";
        public int FieldRow { get; set; } = 0;
        public int TypeRow { get; set; } = -1;
        public int DescriptionRow { get; set; } = -1;
        public int DataStartRow { get; set; } = -1;
        public bool TreatUnknownTypesAsEnum { get; set; }
        public List<ContractFieldSpec> Fields { get; set; } = new List<ContractFieldSpec>();
    }

    public sealed class ContractFieldSpec
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ValueKind { get; set; } = "string";
        public string Description { get; set; } = "";
    }

    public sealed class ContractGitSpec
    {
        public string Branch { get; set; } = "";
        public string Head { get; set; } = "";
        public string FeishuBranch { get; set; } = "";
        public string Profile { get; set; } = "";
    }

    public sealed class UnityExcelToSoContract
    {
        public string SettingsPath { get; set; } = "";
        public string ExcelPath { get; set; } = "";
        public string TableId { get; set; } = "";
        public string ScriptableObjectType { get; set; } = "";
        public string AssetPath { get; set; } = "";
        public Dictionary<string, string> ExtraFields { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class MergePolicyContract
    {
        public bool LowRisk { get; set; }
        public bool ConfirmWriteMain { get; set; }
        public string ApprovalRecordId { get; set; } = "";
        public string ApprovedByRole { get; set; } = "";
    }

    public sealed class BranchBindingContract
    {
        public string GitBranch { get; set; } = "";
        public string FeishuBranch { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Slug { get; set; } = "";
        public string NodeTitle { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public string WikiNodeUrl { get; set; } = "";
        public string Status { get; set; } = "";
        public string OwnerRole { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }

    public sealed class SyncCacheContract
    {
        public string TableId { get; set; } = "";
        public string CacheDirectory { get; set; } = ".config-sheet-forge/cache";
        public string ExcelCacheDirectory { get; set; } = ".config-sheet-forge/excel-cache";
        public bool ConfirmApply { get; set; }
    }

    public sealed class LifecycleContractResult
    {
        public string Operation { get; set; } = "";
        public bool DryRun { get; set; }
        public bool Success { get; set; } = true;
        public string GitHead { get; set; } = "";
        public string Branch { get; set; } = "";
        public string SpreadsheetToken { get; set; } = "";
        public string SpreadsheetUrl { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public string RegistryRecordId { get; set; } = "";
        public string SchemaReviewId { get; set; } = "";
        public string SchemaReviewReason { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string OnlineSheetUrl { get; set; } = "";
        public BranchWorkspaceResolution BranchWorkspace { get; set; } = new BranchWorkspaceResolution();
        public RegistryDisplayMapping DisplayNameMapping { get; set; } = RegistryLocalization.Default("zh-Hans");
        public List<LifecycleActionResult> Actions { get; set; } = new List<LifecycleActionResult>();
        public List<string> HumanReadableFailures { get; set; } = new List<string>();
        public Dictionary<string, string> DocumentationTargets { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public PrGateReport PrGateReport { get; set; } = new PrGateReport();
        public string GateReportPath { get; set; } = "";
        public List<SeedTableLifecycleResult> SeedTables { get; set; } = new List<SeedTableLifecycleResult>();

        public void AddFailure(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Success = false;
                HumanReadableFailures.Add(message);
            }
        }

        public LifecycleActionResult AddAction(string action, string status, string message)
        {
            var result = new LifecycleActionResult
            {
                Action = action ?? "",
                Status = status ?? "",
                Message = message ?? ""
            };
            Actions.Add(result);
            return result;
        }
    }

    public sealed class LifecycleActionResult
    {
        public string Action { get; set; } = "";
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class RegistryDisplayMapping
    {
        public Dictionary<string, string> Tables { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class RegistryLocalization
    {
        public static RegistryDisplayMapping Default(string locale)
        {
            var mapping = new RegistryDisplayMapping();
            if (!string.Equals(locale, "zh-Hans", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(locale, "zh-CN", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(locale))
            {
                AddEnglishFallback(mapping);
                return mapping;
            }

            mapping.Tables["ConfigSheets"] = "配表清单";
            mapping.Tables["BranchBindings"] = "分支绑定";
            mapping.Tables["MergeReviews"] = "合并审查";
            mapping.Tables["Waivers"] = "同步豁免";
            mapping.Tables["SchemaReviews"] = "Schema 审查";

            mapping.Fields["TableId"] = "配表ID";
            mapping.Fields["DisplayName"] = "显示名称";
            mapping.Fields["ExcelPath"] = "本地Excel缓存路径";
            mapping.Fields["SpreadsheetToken"] = "在线表Token";
            mapping.Fields["SheetId"] = "工作表ID";
            mapping.Fields["Branch"] = "Feishu分支";
            mapping.Fields["FeishuBranch"] = "飞书分支";
            mapping.Fields["Profile"] = "配置Profile";
            mapping.Fields["WikiNodeToken"] = "Wiki节点Token";
            mapping.Fields["WikiNodeUrl"] = "Wiki节点链接";
            mapping.Fields["SemanticHash"] = "语义Hash";
            mapping.Fields["OwnerRole"] = "负责人角色";
            mapping.Fields["SchemaReviewRequired"] = "需要Schema审查";
            mapping.Fields["OnlineSheetUrl"] = "在线表链接";
            mapping.Fields["Status"] = "状态";
            mapping.Fields["GitBranch"] = "Git分支";
            mapping.Fields["CreatedBy"] = "创建人";
            mapping.Fields["CreatedAt"] = "创建时间";
            mapping.Fields["UpdatedAt"] = "更新时间";
            mapping.Fields["ApprovedByRole"] = "批准角色";
            mapping.Fields["ExpiresAt"] = "过期时间";
            mapping.Fields["ReviewId"] = "审查ID";
            return mapping;
        }

        public static string TableDisplayName(string machineKey, string locale)
        {
            var mapping = Default(locale);
            return mapping.Tables.TryGetValue(machineKey, out var displayName) ? displayName : machineKey;
        }

        public static string FieldDisplayName(string machineKey, string locale)
        {
            var mapping = Default(locale);
            return mapping.Fields.TryGetValue(machineKey, out var displayName) ? displayName : machineKey;
        }

        private static void AddEnglishFallback(RegistryDisplayMapping mapping)
        {
            foreach (var table in new[] { "ConfigSheets", "BranchBindings", "MergeReviews", "Waivers", "SchemaReviews" })
            {
                mapping.Tables[table] = table;
            }

            foreach (var field in new[] { "TableId", "DisplayName", "ExcelPath", "SpreadsheetToken", "SheetId", "Branch", "FeishuBranch", "Profile", "WikiNodeToken", "WikiNodeUrl", "SemanticHash", "OwnerRole", "SchemaReviewRequired", "OnlineSheetUrl", "Status", "GitBranch", "CreatedBy", "CreatedAt", "UpdatedAt", "ApprovedByRole", "ExpiresAt", "ReviewId" })
            {
                mapping.Fields[field] = field;
            }
        }
    }

    public sealed class RegistryMigrationOptions
    {
        public string Locale { get; set; } = "zh-Hans";
        public bool CleanupDefaultRows { get; set; }
        public bool CleanupDefaultFields { get; set; }
    }

    public sealed class RegistrySnapshot
    {
        public List<RegistryTableSnapshot> Tables { get; set; } = new List<RegistryTableSnapshot>();
    }

    public sealed class RegistryTableSnapshot
    {
        public string MachineKey { get; set; } = "";
        public string TableId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<RegistryFieldSnapshot> Fields { get; set; } = new List<RegistryFieldSnapshot>();
        public List<RegistryRecordSnapshot> Records { get; set; } = new List<RegistryRecordSnapshot>();
    }

    public sealed class RegistryFieldSnapshot
    {
        public string MachineKey { get; set; } = "";
        public string FieldId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsDefaultField { get; set; }
    }

    public sealed class RegistryRecordSnapshot
    {
        public string RecordId { get; set; } = "";
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool IsEmpty
        {
            get { return Values.Count == 0 || Values.Values.All(string.IsNullOrWhiteSpace); }
        }
    }

    public sealed class RegistryMigrationPlan
    {
        public RegistryDisplayMapping DisplayNameMapping { get; set; } = RegistryLocalization.Default("zh-Hans");
        public List<LifecycleActionResult> Actions { get; set; } = new List<LifecycleActionResult>();
    }

    public static class RegistryMigrator
    {
        private static readonly ISet<string> DefaultFieldNames = new HashSet<string>(new[] { "Text", "Single option", "Date", "Attachment" }, StringComparer.OrdinalIgnoreCase);

        public static RegistryMigrationPlan Plan(RegistrySnapshot snapshot, RegistryMigrationOptions options)
        {
            if (snapshot == null)
            {
                snapshot = new RegistrySnapshot();
            }

            if (options == null)
            {
                options = new RegistryMigrationOptions();
            }

            var plan = new RegistryMigrationPlan { DisplayNameMapping = RegistryLocalization.Default(options.Locale) };
            foreach (var tableMapping in plan.DisplayNameMapping.Tables)
            {
                var existing = snapshot.Tables.FirstOrDefault(t => string.Equals(t.MachineKey, tableMapping.Key, StringComparison.OrdinalIgnoreCase) ||
                                                                   string.Equals(t.DisplayName, tableMapping.Key, StringComparison.OrdinalIgnoreCase) ||
                                                                   string.Equals(t.DisplayName, tableMapping.Value, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    Add(plan, "registry.table.ensure", "planned", "确保注册中心表存在：" + tableMapping.Value).Details["machineKey"] = tableMapping.Key;
                    plan.Actions[plan.Actions.Count - 1].Details["displayName"] = tableMapping.Value;
                }
                else if (!string.Equals(existing.DisplayName, tableMapping.Value, StringComparison.Ordinal))
                {
                    var action = Add(plan, "registry.table.rename", "planned", "把注册中心表 “" + existing.DisplayName + "” 显示为 “" + tableMapping.Value + "”。");
                    action.Details["machineKey"] = tableMapping.Key;
                    action.Details["tableId"] = existing.TableId;
                    action.Details["displayName"] = tableMapping.Value;
                }
            }

            foreach (var table in snapshot.Tables)
            {
                foreach (var fieldMapping in plan.DisplayNameMapping.Fields)
                {
                    var field = table.Fields.FirstOrDefault(f => string.Equals(f.MachineKey, fieldMapping.Key, StringComparison.OrdinalIgnoreCase) ||
                                                                 string.Equals(f.DisplayName, fieldMapping.Key, StringComparison.OrdinalIgnoreCase) ||
                                                                 string.Equals(f.DisplayName, fieldMapping.Value, StringComparison.OrdinalIgnoreCase));
                    if (field == null)
                    {
                        var action = Add(plan, "registry.field.ensure", "planned", "确保字段显示为 “" + fieldMapping.Value + "”。");
                        action.Details["table"] = FirstNonEmpty(table.MachineKey, table.DisplayName, table.TableId);
                        action.Details["machineKey"] = fieldMapping.Key;
                        action.Details["displayName"] = fieldMapping.Value;
                    }
                    else if (!string.Equals(field.DisplayName, fieldMapping.Value, StringComparison.Ordinal))
                    {
                        var action = Add(plan, "registry.field.rename", "planned", "把字段 “" + field.DisplayName + "” 显示为 “" + fieldMapping.Value + "”。");
                        action.Details["table"] = FirstNonEmpty(table.MachineKey, table.DisplayName, table.TableId);
                        action.Details["tableId"] = table.TableId;
                        action.Details["machineKey"] = fieldMapping.Key;
                        action.Details["fieldId"] = field.FieldId;
                        action.Details["fieldType"] = field.Type;
                        action.Details["displayName"] = fieldMapping.Value;
                    }
                }

                if (options.CleanupDefaultRows)
                {
                    foreach (var record in table.Records.Where(r => r.IsEmpty))
                    {
                        var action = Add(plan, "registry.record.delete_empty", "planned", "删除注册中心自动生成的空白记录。");
                        action.Details["table"] = FirstNonEmpty(table.MachineKey, table.DisplayName, table.TableId);
                        action.Details["tableId"] = table.TableId;
                        action.Details["recordId"] = record.RecordId;
                    }
                }

                if (options.CleanupDefaultFields)
                {
                    foreach (var field in table.Fields.Where(f => f.IsDefaultField || DefaultFieldNames.Contains(f.DisplayName)))
                    {
                        var action = Add(plan, "registry.field.delete_default", "planned", "删除注册中心自动生成的默认字段：“" + field.DisplayName + "”。");
                        action.Details["table"] = FirstNonEmpty(table.MachineKey, table.DisplayName, table.TableId);
                        action.Details["tableId"] = table.TableId;
                        action.Details["fieldId"] = field.FieldId;
                        action.Details["fieldType"] = field.Type;
                    }
                }
            }

            return plan;
        }

        private static LifecycleActionResult Add(RegistryMigrationPlan plan, string actionName, string status, string message)
        {
            var action = new LifecycleActionResult { Action = actionName, Status = status, Message = message };
            plan.Actions.Add(action);
            return action;
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

    public interface ILifecyclePlatform
    {
        Task<RegistrySnapshot> GetRegistrySnapshotAsync(RegistryContract registry, CancellationToken cancellationToken);
        Task<LifecycleActionResult> EnsureRegistryAsync(RegistryContract registry, RegistryDisplayMapping mapping, CancellationToken cancellationToken);
        Task<SheetCreationResult> CreateOnlineSheetAsync(ContractTableSpec table, CancellationToken cancellationToken);
        Task<LifecycleActionResult> WriteSheetTemplateAsync(SheetCreationResult sheet, IList<IList<string>> templateRows, CancellationToken cancellationToken);
        Task<LifecycleActionResult> UpsertRegistryRecordAsync(RegistryContract registry, ContractTableSpec table, SheetCreationResult sheet, CancellationToken cancellationToken);
        Task<LifecycleActionResult> UpsertSchemaReviewAsync(RegistryContract registry, ContractTableSpec table, ContractGitSpec git, string reason, CancellationToken cancellationToken);
        Task<LifecycleActionResult> ApplyRegistryMigrationAsync(RegistryContract registry, RegistryMigrationPlan plan, CancellationToken cancellationToken);
    }

    public sealed class SheetCreationResult
    {
        public string SpreadsheetToken { get; set; } = "";
        public string SpreadsheetUrl { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
    }

    public sealed class PreviewLifecyclePlatform : ILifecyclePlatform, IBranchWorkspacePlatform
    {
        public Task<RegistrySnapshot> GetRegistrySnapshotAsync(RegistryContract registry, CancellationToken cancellationToken)
        {
            return Task.FromResult(new RegistrySnapshot());
        }

        public Task<LifecycleActionResult> EnsureRegistryAsync(RegistryContract registry, RegistryDisplayMapping mapping, CancellationToken cancellationToken)
        {
            return Task.FromResult(Action("registry.ensure", "planned", "预览：确保 Base 注册中心结构存在。"));
        }

        public Task<SheetCreationResult> CreateOnlineSheetAsync(ContractTableSpec table, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SheetCreationResult
            {
                SpreadsheetToken = FirstNonEmpty(table.SpreadsheetToken, "preview-spreadsheet-token"),
                SpreadsheetUrl = FirstNonEmpty(table.OnlineSheetUrl, table.SpreadsheetUrl, "preview://online-sheet/" + FirstNonEmpty(table.TableId, table.DisplayName, "table")),
                SheetId = FirstNonEmpty(table.SheetId, "preview-sheet-id"),
                WikiNodeToken = FirstNonEmpty(table.WikiNodeToken, "preview-wiki-node-token")
            });
        }

        public Task<LifecycleActionResult> WriteSheetTemplateAsync(SheetCreationResult sheet, IList<IList<string>> templateRows, CancellationToken cancellationToken)
        {
            var action = Action("sheet.template.write", "planned", "预览：写入 ExcelToSO 模板三行和数据起始区。");
            action.Details["rows"] = templateRows.Count.ToString(CultureInfo.InvariantCulture);
            return Task.FromResult(action);
        }

        public Task<LifecycleActionResult> UpsertRegistryRecordAsync(RegistryContract registry, ContractTableSpec table, SheetCreationResult sheet, CancellationToken cancellationToken)
        {
            var action = Action("registry.config_sheets.upsert", "planned", "预览：登记配表清单。");
            action.Details["recordId"] = FirstNonEmpty(registry.RegistryRecordId, "preview-registry-record-id");
            return Task.FromResult(action);
        }

        public Task<LifecycleActionResult> UpsertSchemaReviewAsync(RegistryContract registry, ContractTableSpec table, ContractGitSpec git, string reason, CancellationToken cancellationToken)
        {
            var action = Action("registry.schema_reviews.upsert", "planned", "预览：创建 pending Schema 审查记录。");
            action.Details["schemaReviewId"] = "preview-schema-review-id";
            action.Details["reason"] = reason;
            return Task.FromResult(action);
        }

        public Task<LifecycleActionResult> ApplyRegistryMigrationAsync(RegistryContract registry, RegistryMigrationPlan plan, CancellationToken cancellationToken)
        {
            return Task.FromResult(Action("registry.migration.apply", "planned", "预览：应用注册中心本地化和默认数据清理。"));
        }

        public Task<BranchWorkspaceResolution> EnsureBranchWorkspaceAsync(BranchWorkspaceContract workspace, BranchWorkspaceResolution planned, CancellationToken cancellationToken)
        {
            planned.Status = "planned";
            if (string.IsNullOrWhiteSpace(planned.WikiNodeToken))
            {
                planned.WikiNodeToken = "preview-branch-wiki-node-token";
            }

            if (string.IsNullOrWhiteSpace(planned.WikiNodeUrl))
            {
                planned.WikiNodeUrl = "preview://wiki/" + FirstNonEmpty(planned.NodeTitle, planned.Slug, "branch");
            }

            return Task.FromResult(planned);
        }

        public Task<LifecycleActionResult> UpsertBranchBindingAsync(RegistryContract registry, BranchWorkspaceResolution resolution, CancellationToken cancellationToken)
        {
            var action = Action("registry.branch_bindings.upsert", "planned", "预览：按 GitBranch + Profile 登记 BranchBindings。");
            action.Details["recordId"] = "preview-branch-binding-record-id";
            action.Details["gitBranch"] = resolution.GitBranch;
            action.Details["profile"] = resolution.Profile;
            action.Details["wikiNodeToken"] = resolution.WikiNodeToken;
            return Task.FromResult(action);
        }

        private static LifecycleActionResult Action(string action, string status, string message)
        {
            return new LifecycleActionResult { Action = action, Status = status, Message = message };
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

    public static class LifecycleExecutor
    {
        public static async Task<LifecycleContractResult> ExecuteAsync(LifecycleContractRequest request, ILifecyclePlatform platform, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (platform == null)
            {
                platform = new PreviewLifecyclePlatform();
            }

            var operation = (request.Operation ?? "").Trim().ToLowerInvariant();
            var result = new LifecycleContractResult
            {
                Operation = operation,
                DryRun = request.DryRun,
                GitHead = request.Git.Head ?? "",
                Branch = FirstNonEmpty(request.Git.Branch, request.Git.FeishuBranch, request.Git.Profile),
                DisplayNameMapping = MergeDisplayNames(RegistryLocalization.Default(request.Locale), request.Registry)
            };

            foreach (var pair in request.DocumentationLinks)
            {
                result.DocumentationTargets[pair.Key] = pair.Value;
            }

            switch (operation)
            {
                case "bootstrap-registry":
                    await BootstrapRegistryAsync(request, platform, result, cancellationToken).ConfigureAwait(false);
                    break;
                case "new-table":
                    await NewTableAsync(request, platform, result, cancellationToken).ConfigureAwait(false);
                    break;
                case "seed-from-local-xlsx":
                case "bootstrap-from-local-xlsx":
                    await SeedFromLocalXlsxLifecycle.ExecuteAsync(request, platform, result, cancellationToken).ConfigureAwait(false);
                    break;
                case "sync-cache":
                case "sync-from-online-sheet":
                    ApplySyncCachePlan(request, result);
                    break;
                case "compare-merge":
                    ApplyMergePolicy(request, result);
                    break;
                case "pr-gate-report":
                    result.PrGateReport = PrGateReportEvaluator.Evaluate(request.GateReport);
                    result.Success = result.PrGateReport.Passed;
                    foreach (var failure in result.PrGateReport.HumanReadableFailures)
                    {
                        result.HumanReadableFailures.Add(failure);
                    }
                    result.AddAction("pr-gate-report.write", "ready", "PR gate report 已生成。");
                    break;
                default:
                    result.AddFailure("未知 lifecycle operation：“" + request.Operation + "”。请检查 contract JSON。");
                    break;
            }

            return result;
        }

        public static IList<IList<string>> BuildExcelToSoTemplateRows(ContractTableSpec table)
        {
            var fields = table.Fields.Count == 0
                ? new List<ContractFieldSpec> { new ContractFieldSpec { Key = "id", DisplayName = "ID", ValueKind = "string", Description = "稳定ID" } }
                : table.Fields;

            return new List<IList<string>>
            {
                fields.Select(f => FirstNonEmpty(f.Key, f.DisplayName)).Cast<string>().ToList(),
                fields.Select(f => FirstNonEmpty(f.ValueKind, "string")).Cast<string>().ToList(),
                fields.Select(f => f.Description ?? "").Cast<string>().ToList()
            };
        }

        private static async Task BootstrapRegistryAsync(LifecycleContractRequest request, ILifecyclePlatform platform, LifecycleContractResult result, CancellationToken cancellationToken)
        {
            var action = request.DryRun
                ? await new PreviewLifecyclePlatform().EnsureRegistryAsync(request.Registry, result.DisplayNameMapping, cancellationToken).ConfigureAwait(false)
                : await platform.EnsureRegistryAsync(request.Registry, result.DisplayNameMapping, cancellationToken).ConfigureAwait(false);
            result.Actions.Add(action);

            var snapshot = request.DryRun ? new RegistrySnapshot() : await platform.GetRegistrySnapshotAsync(request.Registry, cancellationToken).ConfigureAwait(false);
            var plan = RegistryMigrator.Plan(snapshot, new RegistryMigrationOptions { Locale = request.Locale, CleanupDefaultRows = true, CleanupDefaultFields = true });
            foreach (var planned in plan.Actions)
            {
                result.Actions.Add(planned);
            }
        }

        private static async Task NewTableAsync(LifecycleContractRequest request, ILifecyclePlatform platform, LifecycleContractResult result, CancellationToken cancellationToken)
        {
            var branchWorkspace = BranchWorkspaceResolver.Resolve(request);
            result.BranchWorkspace = branchWorkspace;
            BranchWorkspaceResolver.ValidateOneToOne(request, branchWorkspace, result);
            result.Actions.Add(BranchWorkspaceResolver.BuildAction(branchWorkspace, request.DryRun ? "planned" : "ready", request.DryRun ? "预览：解析当前 Git 分支对应的 Feishu branch/profile 工作区。" : "已解析当前 Git 分支对应的 Feishu branch/profile 工作区。"));
            if (!result.Success)
            {
                return;
            }

            if (!request.DryRun)
            {
                var branchPlatform = platform as IBranchWorkspacePlatform;
                if (branchPlatform != null)
                {
                    branchWorkspace = await branchPlatform.EnsureBranchWorkspaceAsync(BranchWorkspaceResolver.NormalizeContract(request), branchWorkspace, cancellationToken).ConfigureAwait(false);
                    result.BranchWorkspace = branchWorkspace;
                    result.Actions.Add(BranchWorkspaceResolver.BuildAction(branchWorkspace, FirstNonEmpty(branchWorkspace.Status, "done"), "已解析/创建分支工作区节点，新建配表会挂到该节点下。"));
                    if (!string.IsNullOrWhiteSpace(branchWorkspace.WikiNodeToken))
                    {
                        request.Table.WikiRootToken = branchWorkspace.WikiNodeToken;
                        request.Table.Branch = FirstNonEmpty(branchWorkspace.FeishuBranch, request.Table.Branch);
                        request.Table.Profile = FirstNonEmpty(branchWorkspace.Profile, request.Table.Profile);
                        request.Table.WikiNodeUrl = FirstNonEmpty(branchWorkspace.WikiNodeUrl, request.Table.WikiNodeUrl);
                        var binding = await branchPlatform.UpsertBranchBindingAsync(request.Registry, branchWorkspace, cancellationToken).ConfigureAwait(false);
                        result.Actions.Add(binding);
                    }
                }
            }

            var templateRows = BuildExcelToSoTemplateRows(request.Table);
            var effectivePlatform = request.DryRun ? new PreviewLifecyclePlatform() : platform;
            var sheet = await effectivePlatform.CreateOnlineSheetAsync(request.Table, cancellationToken).ConfigureAwait(false);
            result.SpreadsheetToken = sheet.SpreadsheetToken;
            result.SpreadsheetUrl = sheet.SpreadsheetUrl;
            result.SheetId = sheet.SheetId;
            result.WikiNodeToken = sheet.WikiNodeToken;
            result.OnlineSheetUrl = FirstNonEmpty(sheet.SpreadsheetUrl, request.Table.OnlineSheetUrl);
            result.DocumentationTargets["onlineSheetUrl"] = result.OnlineSheetUrl;
            result.DocumentationTargets["baseUrl"] = request.Registry.BaseUrl;

            result.Actions.Add(new LifecycleActionResult { Action = "sheet.create", Status = request.DryRun ? "planned" : "done", Message = (request.DryRun ? "预览：" : "") + "创建在线 Sheet。" });
            result.Actions.Add(await effectivePlatform.WriteSheetTemplateAsync(sheet, templateRows, cancellationToken).ConfigureAwait(false));
            var registryRecord = await effectivePlatform.UpsertRegistryRecordAsync(request.Registry, request.Table, sheet, cancellationToken).ConfigureAwait(false);
            result.Actions.Add(registryRecord);
            result.RegistryRecordId = FirstNonEmpty(GetDetail(registryRecord, "recordId"), request.Registry.RegistryRecordId);

            result.SchemaReviewReason = UnityExcelToSoSettingsUpdater.BuildSchemaReviewReason(request.Table);
            var schemaReview = await effectivePlatform.UpsertSchemaReviewAsync(request.Registry, request.Table, request.Git, result.SchemaReviewReason, cancellationToken).ConfigureAwait(false);
            result.Actions.Add(schemaReview);
            result.SchemaReviewId = FirstNonEmpty(GetDetail(schemaReview, "schemaReviewId"), "pending");
            result.DocumentationTargets["schemaReviewId"] = result.SchemaReviewId;

            if (!string.IsNullOrWhiteSpace(request.UnityExcelToSo.SettingsPath))
            {
                if (request.DryRun)
                {
                    result.AddAction("unity.excel_to_so.preview", "planned", "预览：更新 Unity ExcelToScriptableObjectSettings.asset，不写入本地文件。");
                }
                else
                {
                    var update = UnityExcelToSoSettingsUpdater.UpsertFile(request.UnityExcelToSo.SettingsPath, new UnityExcelToSoEntry
                    {
                        TableId = FirstNonEmpty(request.UnityExcelToSo.TableId, request.Table.TableId),
                        ExcelPath = FirstNonEmpty(request.UnityExcelToSo.ExcelPath, request.Table.ExcelPath, request.Table.LocalCachePath),
                        ScriptableObjectType = request.UnityExcelToSo.ScriptableObjectType,
                        AssetPath = request.UnityExcelToSo.AssetPath,
                        ExtraFields = request.UnityExcelToSo.ExtraFields
                    });
                    result.Actions.Add(new LifecycleActionResult
                    {
                        Action = "unity.excel_to_so.upsert",
                        Status = update.Changed ? "done" : "unchanged",
                        Message = update.Message
                    });
                }
            }
        }

        private static void ApplyMergePolicy(LifecycleContractRequest request, LifecycleContractResult result)
        {
            var branchWorkspace = BranchWorkspaceResolver.Resolve(request);
            result.BranchWorkspace = branchWorkspace;
            BranchWorkspaceResolver.ValidateOneToOne(request, branchWorkspace, result);
            result.Actions.Add(BranchWorkspaceResolver.BuildAction(branchWorkspace, "ready", "compare-merge 将按 branch/profile 定位 main/base 与当前分支在线 Sheet。"));
            if (!result.Success)
            {
                return;
            }

            if (request.MergePolicy.LowRisk && !request.MergePolicy.ConfirmWriteMain)
            {
                result.AddAction("merge.preview", "planned", "低风险合并默认只生成预览。写回 main 前必须显式确认。");
                return;
            }

            if (request.MergePolicy.ConfirmWriteMain && !string.Equals(request.MergePolicy.ApprovedByRole, "configOwner", StringComparison.OrdinalIgnoreCase))
            {
                result.AddFailure("写回 main 需要配置负责人批准。请让配置负责人完成审批后再重试。");
                return;
            }

            result.AddAction("merge.write_main", request.MergePolicy.ConfirmWriteMain ? "ready" : "planned", request.MergePolicy.ConfirmWriteMain ? "已确认写回 main。" : "仅生成合并预览。");
        }

        private static void ApplySyncCachePlan(LifecycleContractRequest request, LifecycleContractResult result)
        {
            var branchWorkspace = BranchWorkspaceResolver.Resolve(request);
            result.BranchWorkspace = branchWorkspace;
            BranchWorkspaceResolver.ValidateOneToOne(request, branchWorkspace, result);
            result.Actions.Add(BranchWorkspaceResolver.BuildAction(branchWorkspace, request.DryRun ? "planned" : "ready", request.DryRun ? "预览：从 BranchBindings 解析当前分支工作区，不写飞书或本地 cache。" : "已解析当前分支工作区。"));
            if (!result.Success)
            {
                return;
            }

            if (request.BranchBindings.Count == 0 && string.IsNullOrWhiteSpace(branchWorkspace.WikiNodeToken))
            {
                result.AddFailure("sync-cache 需要先从 BranchBindings 确认当前 Git 分支 “" + branchWorkspace.GitBranch + "” 对应的 Feishu profile 和 Wiki 节点。请先运行 seed dry-run/apply 建立分支绑定，或在 contract.branchBindings 中提供当前分支记录。");
                return;
            }

            if (string.IsNullOrWhiteSpace(branchWorkspace.WikiNodeToken) && request.BranchBindings.Count > 0)
            {
                result.AddFailure("当前 Git 分支 “" + branchWorkspace.GitBranch + "” 没有可用 BranchBindings Wiki 节点。请先运行 seed dry-run/apply 创建分支工作区，或修正 BranchBindings。");
                return;
            }

            var sync = request.SyncCache ?? new SyncCacheContract();
            var tableId = FirstNonEmpty(sync.TableId, request.Table.TableId);
            var scope = string.IsNullOrWhiteSpace(tableId) ? "当前 branch/profile 下所有已登记配表" : "配表 “" + tableId + "”";
            result.AddAction("sync-cache.online_read", request.DryRun ? "planned" : "ready", (request.DryRun ? "预览：" : "") + "从 ConfigSheets 按 TableId + Branch/Profile 找到在线 Sheet，并回读 " + scope + "。");
            result.AddAction("sync-cache.export_xlsx", request.DryRun ? "planned" : "ready", (request.DryRun ? "预览：" : "") + "导出在线 Sheet 为 xlsx。");
            result.AddAction("sync-cache.triangulation_compare", request.DryRun ? "planned" : "ready", (request.DryRun ? "预览：" : "") + "比较 online-read semantic、exported-xlsx semantic 与最新 semantic cache；不一致会阻断。");
            result.AddAction("sync-cache.cache_hash_gate", request.DryRun ? "planned" : "ready", (request.DryRun ? "预览：" : "") + "semantic hash 无变化时不重写 xlsx/semantic/sha256，保持 mtime。");
        }

        private static void ValidateBranchBindings(LifecycleContractRequest request, LifecycleContractResult result)
        {
            var branch = FirstNonEmpty(request.Git.Branch, "main");
            var feishu = FirstNonEmpty(request.Git.FeishuBranch, request.Git.Profile, branch);
            if (string.Equals(branch, "main", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(request.Git.FeishuBranch) && string.IsNullOrWhiteSpace(request.Git.Profile))
            {
                feishu = "main";
            }

            var bindings = request.BranchBindings;
            var branchMatches = bindings.Where(b => string.Equals(b.GitBranch, branch, StringComparison.OrdinalIgnoreCase)).ToList();
            var profileMatches = bindings.Where(b => string.Equals(FirstNonEmpty(b.FeishuBranch, b.Profile), feishu, StringComparison.OrdinalIgnoreCase)).ToList();
            if (branchMatches.Count > 1)
            {
                result.AddFailure("当前 Git 分支 “" + branch + "” 绑定了多个 Feishu profile。请先清理分支绑定。");
            }

            if (profileMatches.Select(b => b.GitBranch).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                result.AddFailure("Feishu profile “" + feishu + "” 被多个 Git 分支使用。请先清理分支绑定。");
            }

            if (bindings.Count > 0 && branchMatches.Count == 0)
            {
                result.AddAction("branch_binding.pending", "planned", "当前 Git 分支还没有 Feishu 分支绑定，将创建 pending binding。");
            }
        }

        private static RegistryDisplayMapping MergeDisplayNames(RegistryDisplayMapping defaults, RegistryContract registry)
        {
            if (registry == null)
            {
                return defaults;
            }

            foreach (var pair in registry.DisplayNames)
            {
                defaults.Tables[pair.Key] = pair.Value;
            }

            foreach (var pair in registry.FieldDisplayNames)
            {
                foreach (var field in pair.Value)
                {
                    defaults.Fields[field.Key] = field.Value;
                }
            }

            return defaults;
        }

        private static string GetDetail(LifecycleActionResult action, string key)
        {
            return action.Details.TryGetValue(key, out var value) ? value : "";
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

    public sealed class UnityExcelToSoEntry
    {
        public string TableId { get; set; } = "";
        public string ExcelPath { get; set; } = "";
        public string ScriptableObjectType { get; set; } = "";
        public string AssetPath { get; set; } = "";
        public Dictionary<string, string> ExtraFields { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class UnitySettingsUpdateResult
    {
        public bool Changed { get; set; }
        public string Message { get; set; } = "";
    }

    public static class UnityExcelToSoSettingsUpdater
    {
        public static UnitySettingsUpdateResult UpsertFile(string path, UnityExcelToSoEntry entry)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new UnitySettingsUpdateResult { Message = "未配置 Unity ExcelToSO 设置路径。" };
            }

            var hasBom = false;
            var existing = "%YAML 1.1\n--- !u!114 &1\nExcelToScriptableObjectSettings:\n  configs:\n";
            if (File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                hasBom = HasUtf8Bom(bytes);
                existing = Encoding.UTF8.GetString(hasBom ? bytes.Skip(3).ToArray() : bytes);
            }

            var newline = DetectNewline(existing);
            var updated = NormalizeNewlineStyle(UpsertText(existing, entry), newline);
            if (string.Equals(existing, updated, StringComparison.Ordinal))
            {
                return new UnitySettingsUpdateResult { Changed = false, Message = "Unity ExcelToSO 设置已包含目标表，未改动。" };
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var bodyBytes = encoding.GetBytes(updated);
            File.WriteAllBytes(path, hasBom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bodyBytes).ToArray() : bodyBytes);
            return new UnitySettingsUpdateResult { Changed = true, Message = "已更新 Unity ExcelToScriptableObjectSettings.asset。" };
        }

        public static string UpsertText(string yaml, UnityExcelToSoEntry entry)
        {
            yaml = yaml ?? "";
            entry = entry ?? new UnityExcelToSoEntry();
            if (string.IsNullOrWhiteSpace(entry.ExcelPath) && string.IsNullOrWhiteSpace(entry.TableId))
            {
                return yaml;
            }

            var normalizedExcel = NormalizePath(entry.ExcelPath);
            var normalizedTable = entry.TableId ?? "";
            var trimmed = yaml.TrimStart();
            if ((!string.IsNullOrWhiteSpace(normalizedExcel) && yaml.IndexOf(normalizedExcel, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(normalizedTable) && yaml.IndexOf("tableId: " + normalizedTable, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return yaml;
            }

            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return UpsertJsonLikeText(yaml, entry, normalizedTable, normalizedExcel);
            }

            var builder = new System.Text.StringBuilder();
            builder.Append(yaml);
            if (!yaml.EndsWith("\n", StringComparison.Ordinal))
            {
                builder.AppendLine();
            }

            builder.AppendLine("  - tableId: " + EscapeYaml(normalizedTable));
            builder.AppendLine("    excelPath: " + EscapeYaml(normalizedExcel));
            if (!string.IsNullOrWhiteSpace(entry.ScriptableObjectType))
            {
                builder.AppendLine("    scriptableObjectType: " + EscapeYaml(entry.ScriptableObjectType));
            }

            if (!string.IsNullOrWhiteSpace(entry.AssetPath))
            {
                builder.AppendLine("    assetPath: " + EscapeYaml(NormalizePath(entry.AssetPath)));
            }

            foreach (var pair in entry.ExtraFields.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine("    " + pair.Key + ": " + EscapeYaml(pair.Value));
            }

            return builder.ToString();
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        private static string DetectNewline(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Environment.NewLine;
            }

            var crlf = text.IndexOf("\r\n", StringComparison.Ordinal);
            var lf = text.IndexOf('\n');
            return crlf >= 0 || lf < 0 ? "\r\n" : "\n";
        }

        private static string NormalizeNewlineStyle(string text, string newline)
        {
            newline = string.IsNullOrEmpty(newline) ? Environment.NewLine : newline;
            return (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", newline);
        }

        private static string UpsertJsonLikeText(string json, UnityExcelToSoEntry entry, string tableId, string excelPath)
        {
            var existingAnchor = FindExistingJsonEntryAnchor(json, tableId, excelPath);
            if (existingAnchor >= 0)
            {
                return UpdateExistingJsonEntry(json, entry, tableId, excelPath, existingAnchor);
            }

            var objectText = BuildJsonEntry(entry, tableId, excelPath, "    ");
            var arrayProperty = "";
            entry.ExtraFields.TryGetValue("jsonArrayProperty", out arrayProperty);
            var insertAt = string.IsNullOrWhiteSpace(arrayProperty) ? -1 : FindArrayEnd(json, "\"" + arrayProperty + "\"");
            if (insertAt < 0)
            {
                foreach (var property in new[] { "\"configs\"", "\"entries\"", "\"tables\"", "\"excelToScriptableObjectSettings\"" })
                {
                    insertAt = FindArrayEnd(json, property);
                    if (insertAt >= 0)
                    {
                        break;
                    }
                }
            }

            if (insertAt >= 0)
            {
                var before = json.Substring(0, insertAt).TrimEnd();
                var after = json.Substring(insertAt);
                var needsComma = !before.EndsWith("[", StringComparison.Ordinal);
                return before + (needsComma ? "," : "") + Environment.NewLine + objectText + Environment.NewLine + after.TrimStart();
            }

            var rootArrayEnd = FindRootArrayEnd(json);
            if (rootArrayEnd >= 0)
            {
                var before = json.Substring(0, rootArrayEnd).TrimEnd();
                var after = json.Substring(rootArrayEnd);
                var needsComma = !before.EndsWith("[", StringComparison.Ordinal);
                return before + (needsComma ? "," : "") + Environment.NewLine + objectText + Environment.NewLine + after.TrimStart();
            }

            var objectEnd = json.LastIndexOf('}');
            if (objectEnd >= 0)
            {
                var before = json.Substring(0, objectEnd).TrimEnd();
                var after = json.Substring(objectEnd);
                var needsComma = !before.EndsWith("{", StringComparison.Ordinal);
                return before + (needsComma ? "," : "") + Environment.NewLine +
                       "  \"configSheetForgeEntries\": [" + Environment.NewLine +
                       objectText + Environment.NewLine +
                       "  ]" + Environment.NewLine +
                       after;
            }

            return json;
        }

        private static string UpdateExistingJsonEntry(string json, UnityExcelToSoEntry entry, string tableId, string excelPath)
        {
            var keyIndex = FindExistingJsonEntryAnchor(json, tableId, excelPath);
            return keyIndex < 0 ? json : UpdateExistingJsonEntry(json, entry, tableId, excelPath, keyIndex);
        }

        private static string UpdateExistingJsonEntry(string json, UnityExcelToSoEntry entry, string tableId, string excelPath, int keyIndex)
        {
            if (keyIndex < 0)
            {
                return json;
            }

            var objectStart = FindObjectStart(json, keyIndex);
            var objectEnd = objectStart < 0 ? -1 : FindMatchingBracket(json, objectStart, '{', '}');
            if (objectStart < 0 || objectEnd < 0)
            {
                return json;
            }

            var before = json.Substring(0, objectStart);
            var body = json.Substring(objectStart, objectEnd - objectStart + 1);
            var after = json.Substring(objectEnd + 1);
            body = UpsertJsonStringProperty(body, FirstNonEmpty(FindJsonPathPropertyName(body, tableId, excelPath), "excelPath"), excelPath);
            if (!string.IsNullOrWhiteSpace(entry.ScriptableObjectType))
            {
                var scriptableKey = FindJsonPropertyName(body, "scriptableObjectType", "scriptableObject", "targetType", "soType");
                if (!string.IsNullOrWhiteSpace(scriptableKey))
                {
                    body = UpsertJsonStringProperty(body, scriptableKey, entry.ScriptableObjectType);
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.AssetPath))
            {
                var assetKey = FindJsonPropertyName(body, "assetPath", "outputPath", "asset");
                if (!string.IsNullOrWhiteSpace(assetKey))
                {
                    body = UpsertJsonStringProperty(body, assetKey, NormalizePath(entry.AssetPath));
                }
            }

            foreach (var pair in entry.ExtraFields.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.Equals(pair.Key, "jsonArrayProperty", StringComparison.OrdinalIgnoreCase) &&
                    FindJsonProperty(body, pair.Key) >= 0)
                {
                    body = UpsertJsonStringProperty(body, pair.Key, pair.Value);
                }
            }

            return before + body + after;
        }

        private static int FindExistingJsonEntryAnchor(string json, string tableId, string excelPath)
        {
            if (!string.IsNullOrWhiteSpace(tableId))
            {
                var keyIndex = FindJsonEntryKey(json, "tableId", tableId);
                if (keyIndex >= 0)
                {
                    return keyIndex;
                }

                keyIndex = FindJsonEntryKey(json, "id", tableId);
                if (keyIndex >= 0)
                {
                    return keyIndex;
                }
            }

            var anchors = new List<string>();
            AddAnchor(anchors, tableId);
            AddAnchor(anchors, Path.GetFileNameWithoutExtension(excelPath));
            AddAnchor(anchors, Path.GetFileName(excelPath));
            foreach (var anchor in anchors)
            {
                var searchFrom = 0;
                while (searchFrom < json.Length)
                {
                    var index = json.IndexOf(anchor, searchFrom, StringComparison.OrdinalIgnoreCase);
                    if (index < 0)
                    {
                        break;
                    }

                    var objectStart = FindObjectStart(json, index);
                    var objectEnd = objectStart < 0 ? -1 : FindMatchingBracket(json, objectStart, '{', '}');
                    if (objectStart >= 0 && objectEnd > objectStart)
                    {
                        var body = json.Substring(objectStart, objectEnd - objectStart + 1);
                        if (LooksLikeExcelToSoJsonEntry(body, tableId, excelPath))
                        {
                            return index;
                        }
                    }

                    searchFrom = index + anchor.Length;
                }
            }

            return -1;
        }

        private static void AddAnchor(ICollection<string> anchors, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !anchors.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                anchors.Add(value);
            }
        }

        private static bool LooksLikeExcelToSoJsonEntry(string objectText, string tableId, string excelPath)
        {
            return objectText.IndexOf(".xlsx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (!string.IsNullOrWhiteSpace(tableId) && objectText.IndexOf(tableId, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (!string.IsNullOrWhiteSpace(excelPath) && objectText.IndexOf(Path.GetFileNameWithoutExtension(excelPath), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FindJsonPathPropertyName(string objectText, string tableId, string excelPath)
        {
            var preferred = FindJsonPropertyName(objectText, "excelPath", "ExcelPath", "xlsxPath", "excelFilePath", "excelFile", "sourcePath", "path", "Path");
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }

            return FindStringPropertyNameByValue(objectText, value =>
                value.IndexOf(".xlsx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (!string.IsNullOrWhiteSpace(tableId) && value.IndexOf(tableId, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(excelPath) && value.IndexOf(Path.GetFileNameWithoutExtension(excelPath), StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static string FindJsonPropertyName(string objectText, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var propertyIndex = FindJsonProperty(objectText, candidate);
                if (propertyIndex >= 0)
                {
                    int keyEnd;
                    return ParseJsonString(objectText, propertyIndex, out keyEnd);
                }
            }

            return "";
        }

        private static string FindStringPropertyNameByValue(string objectText, Func<string, bool> predicate)
        {
            var searchFrom = 0;
            while (searchFrom < objectText.Length)
            {
                var keyStart = objectText.IndexOf('"', searchFrom);
                if (keyStart < 0)
                {
                    return "";
                }

                int keyEnd;
                var key = ParseJsonString(objectText, keyStart, out keyEnd);
                var colon = objectText.IndexOf(':', keyEnd + 1);
                if (colon < 0)
                {
                    return "";
                }

                var valueStart = SkipWhitespace(objectText, colon + 1);
                if (valueStart < objectText.Length && objectText[valueStart] == '"')
                {
                    int valueEnd;
                    var value = ParseJsonString(objectText, valueStart, out valueEnd);
                    if (predicate(value))
                    {
                        return key;
                    }

                    searchFrom = valueEnd + 1;
                }
                else
                {
                    searchFrom = colon + 1;
                }
            }

            return "";
        }

        private static int FindJsonEntryKey(string json, string key, string value)
        {
            var needle = "\"" + key + "\"";
            var searchFrom = 0;
            while (searchFrom < json.Length)
            {
                var keyIndex = json.IndexOf(needle, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                {
                    return -1;
                }

                var colon = json.IndexOf(':', keyIndex + needle.Length);
                if (colon < 0)
                {
                    return -1;
                }

                var valueStart = SkipWhitespace(json, colon + 1);
                if (valueStart < json.Length && json[valueStart] == '"')
                {
                    int valueEnd;
                    var parsed = ParseJsonString(json, valueStart, out valueEnd);
                    if (string.Equals(parsed, value, StringComparison.OrdinalIgnoreCase))
                    {
                        return keyIndex;
                    }

                    searchFrom = valueEnd + 1;
                    continue;
                }

                searchFrom = colon + 1;
            }

            return -1;
        }

        private static string UpsertJsonStringProperty(string objectText, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return objectText;
            }

            var propertyIndex = FindJsonProperty(objectText, key);
            if (propertyIndex >= 0)
            {
                var colon = objectText.IndexOf(':', propertyIndex);
                var valueStart = colon < 0 ? -1 : SkipWhitespace(objectText, colon + 1);
                if (valueStart >= 0 && valueStart < objectText.Length && objectText[valueStart] == '"')
                {
                    int valueEnd;
                    ParseJsonString(objectText, valueStart, out valueEnd);
                    return objectText.Substring(0, valueStart) + "\"" + EscapeJson(value) + "\"" + objectText.Substring(valueEnd + 1);
                }
            }

            var insertAt = objectText.LastIndexOf('}');
            if (insertAt < 0)
            {
                return objectText;
            }

            var before = objectText.Substring(0, insertAt).TrimEnd();
            var after = objectText.Substring(insertAt);
            var needsComma = !before.EndsWith("{", StringComparison.Ordinal);
            return before + (needsComma ? "," : "") + Environment.NewLine +
                   "    \"" + EscapeJson(key) + "\": \"" + EscapeJson(value) + "\"" + Environment.NewLine +
                   after;
        }

        private static int FindJsonProperty(string json, string key)
        {
            var needle = "\"" + key + "\"";
            return json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        private static int FindObjectStart(string json, int from)
        {
            for (var i = from; i >= 0; i--)
            {
                if (json[i] == '{')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int SkipWhitespace(string text, int start)
        {
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }

            return start;
        }

        private static string ParseJsonString(string json, int start, out int end)
        {
            var builder = new System.Text.StringBuilder();
            var escaped = false;
            for (var i = start + 1; i < json.Length; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    builder.Append(c);
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    end = i;
                    return builder.ToString();
                }

                builder.Append(c);
            }

            end = json.Length - 1;
            return builder.ToString();
        }

        private static string BuildJsonEntry(UnityExcelToSoEntry entry, string tableId, string excelPath, string indent)
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("tableId", tableId),
                new KeyValuePair<string, string>("excelPath", excelPath)
            };
            if (!string.IsNullOrWhiteSpace(entry.ScriptableObjectType))
            {
                fields.Add(new KeyValuePair<string, string>("scriptableObjectType", entry.ScriptableObjectType));
            }

            if (!string.IsNullOrWhiteSpace(entry.AssetPath))
            {
                fields.Add(new KeyValuePair<string, string>("assetPath", NormalizePath(entry.AssetPath)));
            }

            foreach (var pair in entry.ExtraFields.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(pair.Key, "jsonArrayProperty", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fields.Add(new KeyValuePair<string, string>(pair.Key, pair.Value));
            }

            var builder = new System.Text.StringBuilder();
            builder.Append(indent).AppendLine("{");
            for (var i = 0; i < fields.Count; i++)
            {
                builder.Append(indent).Append("  \"").Append(EscapeJson(fields[i].Key)).Append("\": \"").Append(EscapeJson(fields[i].Value)).Append("\"");
                if (i + 1 < fields.Count)
                {
                    builder.Append(',');
                }

                builder.AppendLine();
            }

            builder.Append(indent).Append("}");
            return builder.ToString();
        }

        private static int FindArrayEnd(string json, string propertyName)
        {
            var propertyIndex = json.IndexOf(propertyName, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return -1;
            }

            var arrayStart = json.IndexOf('[', propertyIndex);
            return arrayStart < 0 ? -1 : FindMatchingBracket(json, arrayStart, '[', ']');
        }

        private static int FindRootArrayEnd(string json)
        {
            var first = json.TakeWhile(char.IsWhiteSpace).Count();
            return first < json.Length && json[first] == '[' ? FindMatchingBracket(json, first, '[', ']') : -1;
        }

        private static int FindMatchingBracket(string text, int start, char open, char close)
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
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

                if (c == open)
                {
                    depth++;
                }
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        public static string BuildSchemaReviewReason(ContractTableSpec table)
        {
            if (table == null)
            {
                return "新增或更新配表 schema，需要独立审查。";
            }

            var columns = table.Fields.Count == 0
                ? "未提供字段模板"
                : string.Join(", ", table.Fields.Select(f => FirstNonEmpty(f.Key, f.DisplayName) + ":" + FirstNonEmpty(f.ValueKind, "string")).ToArray());
            return "配表 “" + FirstNonEmpty(table.DisplayName, table.TableId, table.SheetName) + "” 的 schema 变更待审查。字段：" + columns;
        }

        private static string NormalizePath(string value)
        {
            return (value ?? "").Replace('\\', '/');
        }

        private static string EscapeYaml(string value)
        {
            value = value ?? "";
            if (value.IndexOfAny(new[] { ':', '#', '\'', '"', '\r', '\n' }) >= 0)
            {
                return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }

            return value;
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

    public static class PowerShellJsonSafety
    {
        public static bool ShouldAvoidInlineJson(string shellName, IEnumerable<string> args)
        {
            if (string.IsNullOrWhiteSpace(shellName) ||
                shellName.IndexOf("powershell", StringComparison.OrdinalIgnoreCase) < 0 &&
                shellName.IndexOf("pwsh", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            var items = args == null ? new List<string>() : args.ToList();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i] ?? "";
                if (!IsJsonOption(item))
                {
                    continue;
                }

                var value = "";
                var split = item.Split(new[] { '=' }, 2);
                if (split.Length == 2)
                {
                    value = split[1];
                }
                else if (i + 1 < items.Count)
                {
                    value = items[i + 1] ?? "";
                }

                if (LooksLikeJson(value))
                {
                    return true;
                }
            }

            return false;
        }

        public static string Recommendation()
        {
            return "PowerShell 中不要把 JSON 直接塞进 --params/--data/--json；请改用 request 文件、临时 .cmd wrapper，或由 ProcessStartInfo.ArgumentList 传参。";
        }

        private static bool IsJsonOption(string value)
        {
            var name = (value ?? "").Split(new[] { '=' }, 2)[0];
            return string.Equals(name, "--params", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "--data", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "--json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "--values", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeJson(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            return (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)) ||
                   (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal));
        }
    }

    public sealed class PrGateReport
    {
        public bool Passed { get; set; } = true;
        public string GitHead { get; set; } = "";
        public string Branch { get; set; } = "";
        public GatePermissions Permissions { get; set; } = new GatePermissions();
        public GateReviewState BranchBinding { get; set; } = new GateReviewState();
        public GateReviewState MergeReview { get; set; } = new GateReviewState();
        public GateCheckState PortableSubset { get; set; } = new GateCheckState();
        public GateCheckState Triangulation { get; set; } = new GateCheckState();
        public bool SchemaChangeDetected { get; set; }
        public GateReviewState SchemaReview { get; set; } = new GateReviewState();
        public GateWaiverState Waiver { get; set; } = new GateWaiverState();
        public List<string> ChangedTables { get; set; } = new List<string>();
        public Dictionary<string, string> CacheHashes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> HumanReadableFailures { get; set; } = new List<string>();
    }

    public sealed class GatePermissions
    {
        public bool CanReadRegistry { get; set; } = true;
        public bool CanReadSheets { get; set; } = true;
        public string RegistryMessage { get; set; } = "";
        public string SheetsMessage { get; set; } = "";
    }

    public sealed class GateReviewState
    {
        public string Status { get; set; } = "";
        public string RecordId { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public sealed class GateCheckState
    {
        public bool Passed { get; set; } = true;
        public List<ValidationFinding> Findings { get; set; } = new List<ValidationFinding>();
        public string DiffSummary { get; set; } = "";
    }

    public sealed class GateWaiverState
    {
        public bool Approved { get; set; }
        public string ApprovedByRole { get; set; } = "";
        public string ExpiresAt { get; set; } = "";
        public string Branch { get; set; } = "";
        public string RecordId { get; set; } = "";
    }

    public static class PrGateReportEvaluator
    {
        public static PrGateReport Evaluate(PrGateReport report)
        {
            if (report == null)
            {
                report = new PrGateReport();
            }

            report.HumanReadableFailures.Clear();
            AddMissingFieldFailures(report);

            if (!report.Permissions.CanReadRegistry)
            {
                Add(report, FirstNonEmpty(report.Permissions.RegistryMessage, "当前账号或应用无权读取 Base 注册中心。请确认应用权限、Base 分享范围，或请管理员授权。"));
            }

            if (!report.Permissions.CanReadSheets)
            {
                Add(report, FirstNonEmpty(report.Permissions.SheetsMessage, "当前账号或应用无权读取在线 Sheet。请确认表格权限后重新同步。"));
            }

            if (!string.IsNullOrWhiteSpace(report.BranchBinding.Status) && !ReviewPassed(report.BranchBinding.Status))
            {
                Add(report, FirstNonEmpty(report.BranchBinding.Message, "当前 Git 分支还没有有效的 BranchBindings 记录，或绑定存在冲突。请先运行 seed/sync dry-run 查看分支工作区，并在 BranchBindings 中修正 GitBranch 与 Profile 的一对一关系。"));
            }

            if (!ReviewPassed(report.MergeReview.Status))
            {
                Add(report, "合并审查还没有完成。请先完成 MergeReviews 里的审查记录，再重新跑 gate。");
            }

            if (!report.PortableSubset.Passed)
            {
                Add(report, "配表里包含 Unity ExcelToSO 无法稳定导出的内容。请按 portable subset 检查结果修正后重新同步。");
            }

            if (!report.Triangulation.Passed)
            {
                Add(report, "三方比较不一致：在线读取、导出的 xlsx、语义归一化结果没有完全一致。请重新同步，或检查表格中是否有公式/图片/合并单元格等风险内容。");
            }

            if (report.SchemaChangeDetected && !ReviewPassed(report.SchemaReview.Status))
            {
                Add(report, "检测到 schema 变化，但 Schema 审查还没有完成。请让审查人批准 SchemaReviews 记录。");
            }

            if (report.Waiver.Approved || !string.IsNullOrWhiteSpace(report.Waiver.RecordId))
            {
                ValidateWaiver(report);
            }

            if (report.ChangedTables.Count > 0 && report.CacheHashes.Count == 0)
            {
                Add(report, "缺少同步报告。请先运行 sync，生成 semantic cache 和 sha256 后再跑 gate。");
            }

            report.Passed = report.HumanReadableFailures.Count == 0;
            return report;
        }

        private static void AddMissingFieldFailures(PrGateReport report)
        {
            if (string.IsNullOrWhiteSpace(report.GitHead))
            {
                Add(report, "PR gate report 缺少 gitHead，无法确认这次检查对应的提交。");
            }

            if (string.IsNullOrWhiteSpace(report.Branch))
            {
                Add(report, "PR gate report 缺少 branch，无法确认 Feishu 分支绑定。");
            }
        }

        private static void ValidateWaiver(PrGateReport report)
        {
            if (!report.Waiver.Approved)
            {
                Add(report, "同步豁免没有批准。只有配置负责人批准的有效 waiver 才能放行。");
            }

            if (!string.Equals(report.Waiver.ApprovedByRole, "configOwner", StringComparison.OrdinalIgnoreCase))
            {
                Add(report, "同步豁免不是配置负责人批准的。请让配置负责人重新批准 waiver。");
            }

            if (string.IsNullOrWhiteSpace(report.Waiver.ExpiresAt) ||
                !DateTimeOffset.TryParse(report.Waiver.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresAt))
            {
                Add(report, "同步豁免缺少过期时间。waiver 必须设置 expiresAt。");
            }
            else if (expiresAt <= DateTimeOffset.UtcNow)
            {
                Add(report, "同步豁免已经过期。请重新同步或申请新的 waiver。");
            }

            if (!string.IsNullOrWhiteSpace(report.Waiver.Branch) &&
                !string.Equals(report.Waiver.Branch, report.Branch, StringComparison.OrdinalIgnoreCase))
            {
                Add(report, "同步豁免属于分支 “" + report.Waiver.Branch + "”，不能用于当前分支 “" + report.Branch + "”。");
            }
        }

        private static bool ReviewPassed(string status)
        {
            return string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase);
        }

        private static void Add(PrGateReport report, string message)
        {
            if (!report.HumanReadableFailures.Contains(message))
            {
                report.HumanReadableFailures.Add(message);
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
