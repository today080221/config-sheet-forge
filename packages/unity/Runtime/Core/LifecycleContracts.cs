using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        public MergeInputsContract MergeInputs { get; set; } = new MergeInputsContract();
        public MergeReviewContract MergeReview { get; set; } = new MergeReviewContract();
        public SchemaReviewApprovalContract SchemaReviewApproval { get; set; } = new SchemaReviewApprovalContract();
        public WaiverApprovalContract WaiverApproval { get; set; } = new WaiverApprovalContract();
        public PrGateReport GateReport { get; set; } = new PrGateReport();
        public SeedFromLocalXlsxContract SeedFromLocalXlsx { get; set; } = new SeedFromLocalXlsxContract();
        public TargetBranchBootstrapContract TargetBranchBootstrap { get; set; } = new TargetBranchBootstrapContract();
        public SyncCacheContract SyncCache { get; set; } = new SyncCacheContract();
        public BranchWorkspaceContract BranchWorkspace { get; set; } = new BranchWorkspaceContract();
        public string GateReportPath { get; set; } = "";
        public string ReportPath { get; set; } = "";
        public string RequiredPreviewFingerprint { get; set; } = "";
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

    public sealed class MergeInputsContract
    {
        public string BasePath { get; set; } = "";
        public string OursPath { get; set; } = "";
        public string TheirsPath { get; set; } = "";
        public string SourceBranch { get; set; } = "";
        public string TargetBranch { get; set; } = "";
        public string TargetFeishuProfile { get; set; } = "";
        public string TargetBranchWikiNodeTitle { get; set; } = "";
        public string TargetBranchWikiNodeUrl { get; set; } = "";
        public string TargetBranchWikiNodeToken { get; set; } = "";
        public string MergeBase { get; set; } = "";
        public string GithubRepository { get; set; } = "";
        public string PrNumber { get; set; } = "";
        public string PrUrl { get; set; } = "";
        public bool AllowPrAutoDetect { get; set; }
        public string MergeReportPath { get; set; } = "";
        public string MergedPath { get; set; } = "";
        public bool WriteBackToMain { get; set; }
        public bool ConfirmWriteMain { get; set; }
        public string TableId { get; set; } = "";
    }

    public sealed class MergeReviewContract
    {
        public string SourceBranch { get; set; } = "";
        public string TargetBranch { get; set; } = "";
        public List<string> TableIds { get; set; } = new List<string>();
        public string TableId { get; set; } = "__project_pr_gate__";
        public string PrNumber { get; set; } = "";
        public string PrUrl { get; set; } = "";
        public string MergeReportPath { get; set; } = "";
        public string MergedPath { get; set; } = "";
        public string RequestFingerprint { get; set; } = "";
        public string RequiredPreviewFingerprint { get; set; } = "";
        public string PreviewResultPath { get; set; } = "";
        public string ApproverRole { get; set; } = "configOwner";
        public string ReviewComment { get; set; } = "";
        public string ReviewId { get; set; } = "";
        public string Status { get; set; } = "approved";
        public bool ConfirmSubmit { get; set; }
    }

    public sealed class SchemaReviewApprovalContract
    {
        public string TableId { get; set; } = "";
        public string Branch { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Status { get; set; } = "approved";
        public string ApproverRole { get; set; } = "schemaReviewer";
        public string ReviewComment { get; set; } = "";
        public bool ConfirmSubmit { get; set; }
    }

    public sealed class WaiverApprovalContract
    {
        public string TableId { get; set; } = "__project_pr_gate__";
        public string Branch { get; set; } = "";
        public string Reason { get; set; } = "";
        public string ExpiresAt { get; set; } = "";
        public string ApprovedByRole { get; set; } = "configOwner";
        public bool ConfirmApprove { get; set; }
    }

    public sealed class MergeReviewInputSummary
    {
        public string Fingerprint { get; set; } = "";
        public string SourceBranch { get; set; } = "";
        public string TargetBranch { get; set; } = "";
        public string TableIdsText { get; set; } = "";
        public List<string> TableIds { get; set; } = new List<string>();
        public string PrNumber { get; set; } = "";
        public string PrUrl { get; set; } = "";
        public string MergeReportPath { get; set; } = "";
        public string MergedPath { get; set; } = "";
    }

    public sealed class TargetBranchBootstrapContract
    {
        public string TargetGitBranch { get; set; } = "";
        public string TargetFeishuProfile { get; set; } = "";
        public string TargetBranchWikiNodeTitle { get; set; } = "";
        public string SourceMode { get; set; } = "local-xlsx";
        public List<string> TableIds { get; set; } = new List<string>();
        public bool ConfirmCreateOnlineSheets { get; set; }
        public bool ConfirmRegistryUpsert { get; set; }
        public bool ConfirmSchemaReviews { get; set; }
        public bool ConfirmWriteLocalCache { get; set; }
        public bool ConfirmWriteProjectConfig { get; set; }
        public bool ConfirmExcelToSoSettings { get; set; }
        public string PreviewResultPath { get; set; } = "";
        public string RequiredPreviewFingerprint { get; set; } = "";
    }

    public sealed class BranchBindingContract
    {
        public string RecordId { get; set; } = "";
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

    public sealed class ResolvedOnlineTableStatus
    {
        public string TableId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RecordId { get; set; } = "";
        public string SpreadsheetToken { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string OnlineSheetUrl { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public string WikiNodeUrl { get; set; } = "";
        public string SemanticHash { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
        public string OwnerRole { get; set; } = "";
        public string Branch { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Status { get; set; } = "";
        public string BlockingReason { get; set; } = "";
    }

    public sealed class BranchStatusSummary
    {
        public string CurrentGitBranch { get; set; } = "";
        public string CurrentProfile { get; set; } = "";
        public string TargetBranch { get; set; } = "";
        public string TargetProfile { get; set; } = "";
        public string BranchBindingStatus { get; set; } = "unknown";
        public string BranchWikiNodeTitle { get; set; } = "";
        public string BranchWikiNodeUrl { get; set; } = "";
        public string BranchWikiNodeToken { get; set; } = "";
        public List<string> ExpectedTableIds { get; set; } = new List<string>();
        public List<ResolvedOnlineTableStatus> RegisteredOnlineTables { get; set; } = new List<ResolvedOnlineTableStatus>();
        public int TableCountExpected { get; set; }
        public int TableCountRegistered { get; set; }
        public List<string> MissingTables { get; set; } = new List<string>();
        public List<string> MissingLocators { get; set; } = new List<string>();
        public List<string> DuplicateConfigSheets { get; set; } = new List<string>();
        public bool CanReadRegistry { get; set; } = true;
        public bool CanReadSheetsMetadata { get; set; } = true;
        public List<string> HumanReadableFailures { get; set; } = new List<string>();
        public string NextRecommendedAction { get; set; } = "";
    }

    public sealed class SyncCacheSummary
    {
        public string CacheStatus { get; set; } = "unknown";
        public List<string> ChangedTables { get; set; } = new List<string>();
        public List<string> MissingCacheTables { get; set; } = new List<string>();
        public List<string> UpToDateTables { get; set; } = new List<string>();
        public List<string> BlockedTables { get; set; } = new List<string>();
        public bool WillWriteFiles { get; set; }
        public List<string> WillWriteFilePaths { get; set; } = new List<string>();
        public bool NoChangeKeepsMtime { get; set; } = true;
        public int TableCount { get; set; }
        public int TriangulationPassedCount { get; set; }
        public int TriangulationFailedCount { get; set; }
        public List<string> PortableSubsetFindings { get; set; } = new List<string>();
        public List<ResolvedOnlineTableStatus> ResolvedOnlineTables { get; set; } = new List<ResolvedOnlineTableStatus>();
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
        public string RequestFingerprint { get; set; } = "";
        public Dictionary<string, string> RequestSummary { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public PrGateReport PrGateReport { get; set; } = new PrGateReport();
        public string GateReportPath { get; set; } = "";
        public List<SeedTableLifecycleResult> SeedTables { get; set; } = new List<SeedTableLifecycleResult>();
        public BranchStatusSummary BranchStatus { get; set; } = new BranchStatusSummary();
        public SyncCacheSummary SyncCacheSummary { get; set; } = new SyncCacheSummary();
        public List<ResolvedOnlineTableStatus> ResolvedOnlineTables { get; set; } = new List<ResolvedOnlineTableStatus>();

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
        public bool CleanupDuplicateBranchBindings { get; set; }
        public string Only { get; set; } = "";
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
        public List<string> Options { get; set; } = new List<string>();
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
        private static readonly Dictionary<string, string[]> RequiredStatusOptionsByTable = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["MergeReviews"] = new[] { "approved", "completed", "passed" },
            ["SchemaReviews"] = new[] { "pending", "approved", "completed", "passed", "rejected" },
            ["Waivers"] = new[] { "approved", "completed", "passed", "rejected", "expired" }
        };

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
            if (OnlyReviewStatusOptions(options))
            {
                foreach (var table in snapshot.Tables)
                {
                    AddStatusOptionDiagnostics(plan, table);
                }

                return plan;
            }

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
                else
                {
                    foreach (var record in table.Records.Where(r => r.IsEmpty))
                    {
                        var action = Add(plan, "registry.record.empty_default", "planned", "检测到注册中心自动生成的空白默认行；dry-run 不删除，apply 清理需显式传 cleanup 选项和 --yes。");
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

                AddFieldAmbiguityDiagnostics(plan, table);
                AddStatusOptionDiagnostics(plan, table);
                AddBranchBindingDuplicateDiagnostics(plan, table, options);
            }

            return plan;
        }

        public static bool OnlyReviewStatusOptions(RegistryMigrationOptions options)
        {
            return options != null &&
                   (string.Equals(options.Only, "review-status-options", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(options.Only, "review-status", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(options.Only, "status-options", StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<string> RequiredStatusOptions(string tableMachineKey)
        {
            return RequiredStatusOptionsByTable.TryGetValue(tableMachineKey ?? "", out var options)
                ? options
                : Array.Empty<string>();
        }

        public static bool StatusOptionsReady(RegistryFieldSnapshot field, string tableMachineKey)
        {
            if (field == null)
            {
                return false;
            }

            var required = RequiredStatusOptions(tableMachineKey);
            if (required.Count == 0)
            {
                return true;
            }

            var existing = new HashSet<string>(field.Options ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            return required.All(existing.Contains);
        }

        private static void AddStatusOptionDiagnostics(RegistryMigrationPlan plan, RegistryTableSnapshot table)
        {
            var tableMachineKey = ResolveGovernanceTableMachineKey(table, plan);
            if (string.IsNullOrWhiteSpace(tableMachineKey) ||
                !RequiredStatusOptionsByTable.TryGetValue(tableMachineKey, out var requiredOptions))
            {
                return;
            }

            var statusField = table.Fields.FirstOrDefault(f =>
                string.Equals(f.MachineKey, "Status", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.DisplayName, "Status", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.DisplayName, plan.DisplayNameMapping.Fields["Status"], StringComparison.OrdinalIgnoreCase));
            if (statusField == null)
            {
                return;
            }

            if (!IsSelectLikeStatusField(statusField))
            {
                var mismatch = Add(plan, "registry.field.status_select_mismatch", "blocked", "检测到 " + tableMachineKey + ".状态 不是单选字段；自动迁移不会改字段类型。请负责人在 Base 中确认迁移方案，或显式使用单独的字段类型转换流程。");
                mismatch.Details["table"] = FirstNonEmpty(table.MachineKey, table.DisplayName, table.TableId);
                mismatch.Details["tableMachineKey"] = tableMachineKey;
                mismatch.Details["tableId"] = table.TableId;
                mismatch.Details["fieldId"] = statusField.FieldId;
                mismatch.Details["fieldType"] = statusField.Type;
                mismatch.Details["nextStep"] = tableMachineKey + ".状态 当前不是单选字段，请负责人在 Base 中确认迁移方案。";
                return;
            }

            var existing = new HashSet<string>(statusField.Options ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var missing = requiredOptions.Where(option => !existing.Contains(option)).ToList();
            if (missing.Count == 0)
            {
                return;
            }

            var merged = (statusField.Options ?? new List<string>())
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .Concat(missing)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var action = Add(plan, "registry.field.options.ensure", "planned", "补齐 " + tableMachineKey + ".状态 单选选项：" + string.Join(", ", missing) + "。");
            action.Details["table"] = FirstNonEmpty(table.MachineKey, table.DisplayName, table.TableId);
            action.Details["tableMachineKey"] = tableMachineKey;
            action.Details["tableId"] = table.TableId;
            action.Details["fieldId"] = statusField.FieldId;
            action.Details["fieldName"] = FirstNonEmpty(statusField.DisplayName, plan.DisplayNameMapping.Fields["Status"], "状态");
            action.Details["fieldType"] = statusField.Type;
            action.Details["existingOptions"] = string.Join(",", statusField.Options ?? new List<string>());
            action.Details["missingOptions"] = string.Join(",", missing);
            action.Details["requiredOptions"] = string.Join(",", requiredOptions);
            action.Details["allOptions"] = string.Join(",", merged);
        }

        private static string ResolveGovernanceTableMachineKey(RegistryTableSnapshot table, RegistryMigrationPlan plan)
        {
            foreach (var machineKey in RequiredStatusOptionsByTable.Keys)
            {
                var displayName = plan.DisplayNameMapping.Tables.TryGetValue(machineKey, out var mapped) ? mapped : machineKey;
                if (string.Equals(table.MachineKey, machineKey, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(table.DisplayName, machineKey, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(table.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return machineKey;
                }
            }

            return "";
        }

        public static bool IsSelectLikeStatusField(RegistryFieldSnapshot field)
        {
            var type = field != null ? field.Type ?? "" : "";
            return type.IndexOf("select", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddFieldAmbiguityDiagnostics(RegistryMigrationPlan plan, RegistryTableSnapshot table)
        {
            var mapping = plan.DisplayNameMapping.Fields;
            var groups = table.Fields
                .Select(f => new { Field = f, MachineKey = ResolveMachineKey(f.DisplayName, f.MachineKey, mapping) })
                .Where(v => !string.IsNullOrWhiteSpace(v.MachineKey))
                .GroupBy(v => v.MachineKey, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                var action = Add(plan, "registry.field.ambiguous_alias", "planned", "检测到注册中心字段存在中英文/旧 schema 重复：“" + group.Key + "”。请保留一个字段名，避免 upsert/lookup 歧义。");
                action.Details["table"] = FirstNonEmpty(table.MachineKey, table.DisplayName, table.TableId);
                action.Details["tableId"] = table.TableId;
                action.Details["machineKey"] = group.Key;
                action.Details["fieldIds"] = string.Join(",", group.Select(v => v.Field.FieldId).Where(v => !string.IsNullOrWhiteSpace(v)));
                action.Details["displayNames"] = string.Join(",", group.Select(v => v.Field.DisplayName).Where(v => !string.IsNullOrWhiteSpace(v)));
            }
        }

        private static void AddBranchBindingDuplicateDiagnostics(RegistryMigrationPlan plan, RegistryTableSnapshot table, RegistryMigrationOptions options)
        {
            var tableName = FirstNonEmpty(table.MachineKey, table.DisplayName);
            if (!string.Equals(tableName, "BranchBindings", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tableName, plan.DisplayNameMapping.Tables["BranchBindings"], StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var groups = table.Records
                .Where(r => !r.IsEmpty)
                .Select(r => new
                {
                    Record = r,
                    GitBranch = GetRecordValue(r, "GitBranch", plan.DisplayNameMapping),
                    Profile = FirstNonEmpty(GetRecordValue(r, "Profile", plan.DisplayNameMapping), GetRecordValue(r, "FeishuBranch", plan.DisplayNameMapping))
                })
                .Where(v => !string.IsNullOrWhiteSpace(v.GitBranch) && !string.IsNullOrWhiteSpace(v.Profile))
                .GroupBy(v => v.GitBranch + "\n" + v.Profile, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                var first = group.First();
                var recordIds = group.Select(v => v.Record.RecordId).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                var action = Add(plan, "registry.branch_bindings.duplicate", "planned", "检测到 BranchBindings 重复绑定：GitBranch “" + first.GitBranch + "” + Profile “" + first.Profile + "” 有 " + group.Count().ToString(CultureInfo.InvariantCulture) + " 条记录（record_id: " + string.Join(", ", recordIds) + "）。请先清理重复历史记录后再运行 seed/sync-cache。");
                action.Details["table"] = FirstNonEmpty(table.MachineKey, table.DisplayName, table.TableId);
                action.Details["tableId"] = table.TableId;
                action.Details["gitBranch"] = first.GitBranch;
                action.Details["profile"] = first.Profile;
                action.Details["count"] = group.Count().ToString(CultureInfo.InvariantCulture);
                action.Details["recordIds"] = string.Join(",", recordIds);

                if (options.CleanupDuplicateBranchBindings)
                {
                    foreach (var duplicate in group.Skip(1))
                    {
                        var delete = Add(plan, "registry.record.delete_duplicate_branch_binding", "planned", "清理重复 BranchBindings 历史记录，保留第一条记录。");
                        delete.Details["table"] = FirstNonEmpty(table.MachineKey, table.DisplayName, table.TableId);
                        delete.Details["tableId"] = table.TableId;
                        delete.Details["recordId"] = duplicate.Record.RecordId;
                        delete.Details["gitBranch"] = first.GitBranch;
                        delete.Details["profile"] = first.Profile;
                    }
                }
            }
        }

        private static string GetRecordValue(RegistryRecordSnapshot record, string machineKey, RegistryDisplayMapping mapping)
        {
            var fallback = "";
            foreach (var alias in FieldAliases(machineKey, mapping))
            {
                foreach (var value in record.Values)
                {
                    if (string.Equals(value.Key, alias, StringComparison.OrdinalIgnoreCase))
                    {
                        fallback = value.Value ?? "";
                        if (!string.IsNullOrWhiteSpace(fallback))
                        {
                            return fallback;
                        }
                    }
                }
            }

            return fallback;
        }

        private static IEnumerable<string> FieldAliases(string machineKey, RegistryDisplayMapping mapping)
        {
            yield return machineKey;
            if (mapping.Fields.TryGetValue(machineKey, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
            {
                yield return displayName;
            }
        }

        private static string ResolveMachineKey(string displayName, string existingMachineKey, IDictionary<string, string> mapping)
        {
            if (!string.IsNullOrWhiteSpace(existingMachineKey))
            {
                return existingMachineKey;
            }

            foreach (var pair in mapping)
            {
                if (string.Equals(displayName, pair.Key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(displayName, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Key;
                }
            }

            return displayName ?? "";
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

    public interface IReviewRegistryPlatform
    {
        Task<LifecycleActionResult> UpsertMergeReviewAsync(RegistryContract registry, MergeReviewContract review, MergeReviewInputSummary summary, CancellationToken cancellationToken);
        Task<LifecycleActionResult> UpsertSchemaReviewApprovalAsync(RegistryContract registry, SchemaReviewApprovalContract review, CancellationToken cancellationToken);
        Task<LifecycleActionResult> UpsertWaiverApprovalAsync(RegistryContract registry, WaiverApprovalContract waiver, CancellationToken cancellationToken);
    }

    public sealed class SheetCreationResult
    {
        public string SpreadsheetToken { get; set; } = "";
        public string SpreadsheetUrl { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
    }

    public sealed class PreviewLifecyclePlatform : ILifecyclePlatform, IBranchWorkspacePlatform, IReviewRegistryPlatform
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

        public Task<LifecycleActionResult> UpsertMergeReviewAsync(RegistryContract registry, MergeReviewContract review, MergeReviewInputSummary summary, CancellationToken cancellationToken)
        {
            var action = Action("registry.merge_reviews.upsert", "planned", "预览：写入 MergeReviews 合并审查记录。");
            action.Details["reviewId"] = FirstNonEmpty(review.ReviewId, "preview-merge-review-id");
            action.Details["tableId"] = FirstNonEmpty(review.TableId, "__project_pr_gate__");
            action.Details["sourceBranch"] = summary.SourceBranch;
            action.Details["targetBranch"] = summary.TargetBranch;
            action.Details["requestFingerprint"] = summary.Fingerprint;
            return Task.FromResult(action);
        }

        public Task<LifecycleActionResult> UpsertSchemaReviewApprovalAsync(RegistryContract registry, SchemaReviewApprovalContract review, CancellationToken cancellationToken)
        {
            var action = Action("registry.schema_reviews.approve", "planned", "预览：更新 SchemaReviews 审查状态。");
            action.Details["tableId"] = review.TableId;
            action.Details["status"] = review.Status;
            return Task.FromResult(action);
        }

        public Task<LifecycleActionResult> UpsertWaiverApprovalAsync(RegistryContract registry, WaiverApprovalContract waiver, CancellationToken cancellationToken)
        {
            var action = Action("registry.waivers.approve", "planned", "预览：写入/更新 Waivers 临时放行记录。");
            action.Details["tableId"] = waiver.TableId;
            action.Details["expiresAt"] = waiver.ExpiresAt;
            return Task.FromResult(action);
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
                case "bootstrap-target-branch-from-local-xlsx":
                    await SeedFromLocalXlsxLifecycle.ExecuteAsync(request, platform, result, cancellationToken).ConfigureAwait(false);
                    break;
                case "bootstrap-current-branch-from-target":
                case "branch-workspace-bootstrap-from-target":
                    ApplyCurrentBranchBootstrapPlan(request, result);
                    break;
                case "registry-status":
                case "branch-status":
                case "sync-status":
                    ApplyBranchStatus(request, result);
                    break;
                case "sync-cache":
                case "sync-from-online-sheet":
                    ApplySyncCachePlan(request, result);
                    break;
                case "compare-merge":
                    ApplyMergePolicy(request, result);
                    break;
                case "submit-merge-review":
                case "approve-merge-review":
                    await SubmitMergeReviewAsync(request, platform, result, cancellationToken).ConfigureAwait(false);
                    break;
                case "approve-schema-review":
                    await ApproveSchemaReviewAsync(request, platform, result, cancellationToken).ConfigureAwait(false);
                    break;
                case "approve-waiver":
                    await ApproveWaiverAsync(request, platform, result, cancellationToken).ConfigureAwait(false);
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

        public static MergeReviewInputSummary BuildMergeReviewInputSummary(LifecycleContractRequest request, IEnumerable<string> tableIds)
        {
            request = request ?? new LifecycleContractRequest();
            request.MergeInputs = request.MergeInputs ?? new MergeInputsContract();
            request.MergeReview = request.MergeReview ?? new MergeReviewContract();
            var tableSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tableId in tableIds ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(tableId))
                {
                    tableSet.Add(tableId.Trim());
                }
            }

            foreach (var tableId in request.MergeReview.TableIds ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(tableId))
                {
                    tableSet.Add(tableId.Trim());
                }
            }

            if (!string.IsNullOrWhiteSpace(request.MergeInputs.TableId))
            {
                tableSet.Add(request.MergeInputs.TableId.Trim());
            }

            var sourceBranch = FirstNonEmpty(request.MergeReview.SourceBranch, request.MergeInputs.SourceBranch, request.Git.Branch, request.BranchWorkspace.GitBranch, request.Git.FeishuBranch, request.Git.Profile);
            var targetBranch = FirstNonEmpty(request.MergeReview.TargetBranch, request.MergeInputs.TargetBranch, request.BranchWorkspace.MainGitBranch, "main");
            var prNumber = FirstNonEmpty(request.MergeReview.PrNumber, request.MergeInputs.PrNumber);
            var prUrl = FirstNonEmpty(request.MergeReview.PrUrl, request.MergeInputs.PrUrl);
            var mergeReportPath = FirstNonEmpty(request.MergeReview.MergeReportPath, request.MergeInputs.MergeReportPath, "Temp/ConfigSheetForge/merge-report.md");
            var mergedPath = FirstNonEmpty(request.MergeReview.MergedPath, request.MergeInputs.MergedPath, "Temp/ConfigSheetForge/merged.semantic.json");

            var basis = new StringBuilder();
            basis.AppendLine("operation=compare-merge");
            basis.AppendLine("sourceBranch=" + NormalizeFingerprintValue(sourceBranch));
            basis.AppendLine("targetBranch=" + NormalizeFingerprintValue(targetBranch));
            basis.AppendLine("tableIds=" + string.Join(",", tableSet.Select(NormalizeFingerprintValue)));
            basis.AppendLine("prNumber=" + NormalizeFingerprintValue(prNumber));
            basis.AppendLine("prUrl=" + NormalizeFingerprintValue(prUrl));
            basis.AppendLine("mergeReportPath=" + NormalizeFingerprintValue(mergeReportPath));
            basis.AppendLine("mergedPath=" + NormalizeFingerprintValue(mergedPath));

            return new MergeReviewInputSummary
            {
                Fingerprint = Sha256Hex(basis.ToString()),
                SourceBranch = sourceBranch,
                TargetBranch = targetBranch,
                TableIds = tableSet.ToList(),
                TableIdsText = string.Join(", ", tableSet),
                PrNumber = prNumber,
                PrUrl = prUrl,
                MergeReportPath = mergeReportPath,
                MergedPath = mergedPath
            };
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
            request.MergeInputs = request.MergeInputs ?? new MergeInputsContract();
            request.MergePolicy = request.MergePolicy ?? new MergePolicyContract();

            var branchWorkspace = BranchWorkspaceResolver.Resolve(request);
            result.BranchWorkspace = branchWorkspace;
            BranchWorkspaceResolver.ValidateOneToOne(request, branchWorkspace, result);
            result.Actions.Add(BranchWorkspaceResolver.BuildAction(branchWorkspace, request.DryRun ? "planned" : "ready", "预览：解析当前分支工作区，用于生成可审查的合并计划。"));

            var targetRequest = BuildTargetMergeRequest(request);
            var targetWorkspace = BranchWorkspaceResolver.Resolve(targetRequest);
            var targetAction = BranchWorkspaceResolver.BuildAction(targetWorkspace, request.DryRun ? "planned" : "ready", "预览：解析目标分支工作区，用于定位 main/base 在线表。");
            targetAction.Action = "target_branch_workspace.resolve";
            targetAction.Details["targetBranch"] = targetWorkspace.GitBranch;
            targetAction.Details["targetProfile"] = FirstNonEmpty(targetWorkspace.Profile, targetWorkspace.FeishuBranch);
            targetAction.Details["targetWikiNodeToken"] = targetWorkspace.WikiNodeToken;
            targetAction.Details["targetWikiNodeUrl"] = targetWorkspace.WikiNodeUrl;
            result.Actions.Add(targetAction);

            if (!result.Success)
            {
                return;
            }

            var sourceProfile = FirstNonEmpty(branchWorkspace.Profile, branchWorkspace.FeishuBranch);
            var targetProfile = FirstNonEmpty(targetWorkspace.Profile, targetWorkspace.FeishuBranch);
            if (string.IsNullOrWhiteSpace(branchWorkspace.WikiNodeToken))
            {
                result.AddFailure("合并预览需要先定位当前分支工作区，但 BranchBindings 中找不到 GitBranch “" + branchWorkspace.GitBranch + "” + Profile “" + sourceProfile + "” 的 Wiki 节点。请先运行“预览同步计划”，确认当前分支已经 Seed/绑定。");
            }

            if (string.IsNullOrWhiteSpace(targetWorkspace.WikiNodeToken))
            {
                result.AddFailure("合并预览需要定位目标分支 “" + targetWorkspace.GitBranch + "” 的工作区，但 BranchBindings 中没有可用 Wiki 节点。请确认目标分支已经登记 BranchBindings，或在合并页高级诊断里检查目标分支。");
            }

            var selectedTable = FirstNonEmpty(request.MergeInputs.TableId, request.SyncCache != null ? request.SyncCache.TableId : "", request.Table != null ? request.Table.TableId : "");
            var sourceRows = FindMergeTableRows(request, sourceProfile, selectedTable);
            var targetRows = FindMergeTableRows(request, targetProfile, selectedTable);
            var tableIds = BuildMergeTableScope(sourceRows, targetRows, selectedTable);
            var missingSourceTables = new List<string>();
            var missingTargetTables = new List<string>();
            var missingSourceLocators = new List<string>();
            var missingTargetLocators = new List<string>();
            foreach (var tableId in tableIds)
            {
                var source = sourceRows.FirstOrDefault(t => string.Equals(t.TableId, tableId, StringComparison.OrdinalIgnoreCase));
                var target = targetRows.FirstOrDefault(t => string.Equals(t.TableId, tableId, StringComparison.OrdinalIgnoreCase));
                if (source == null)
                {
                    missingSourceTables.Add(tableId);
                }
                else if (!HasOnlineSheetLocator(source))
                {
                    missingSourceLocators.Add(tableId);
                }

                if (target == null)
                {
                    missingTargetTables.Add(tableId);
                }
                else if (!HasOnlineSheetLocator(target))
                {
                    missingTargetLocators.Add(tableId);
                }
            }

            if (tableIds.Count == 0)
            {
                result.AddFailure("合并预览没有找到可比较的在线表范围。请确认当前分支和目标分支的 ConfigSheets 都已登记 TableId、Branch/Profile、在线表 Token 和 SheetId。");
            }

            if (missingSourceTables.Count > 0)
            {
                result.AddFailure("当前分支 “" + branchWorkspace.GitBranch + "” 缺少这些 ConfigSheets 记录：" + string.Join(", ", missingSourceTables) + "。请先同步/Seed 当前分支，或检查 Branch/Profile 是否匹配。");
            }

            if (missingTargetTables.Count > 0)
            {
                result.AddFailure("目标分支 “" + targetWorkspace.GitBranch + "” 缺少这些 ConfigSheets 记录：" + string.Join(", ", missingTargetTables) + "。请确认 main/目标分支在线表已经登记，或重新 hydrate live registry。");
            }

            if (missingSourceLocators.Count > 0)
            {
                result.AddFailure("当前分支这些表缺少在线 Sheet 定位信息（SpreadsheetToken/SheetId）：" + string.Join(", ", missingSourceLocators) + "。请在 ConfigSheets 补齐在线表 Token 和工作表 ID。");
            }

            if (missingTargetLocators.Count > 0)
            {
                result.AddFailure("目标分支这些表缺少在线 Sheet 定位信息（SpreadsheetToken/SheetId）：" + string.Join(", ", missingTargetLocators) + "。请在目标分支 ConfigSheets 补齐在线表 Token 和工作表 ID。");
            }

            var prepareAction = result.AddAction("merge.inputs.prepare", result.Success ? "planned" : "blocked", result.Success ? "预览：准备 base/ours/theirs semantic 输入，默认比较当前分支全部在线表。" : "合并输入不足，无法生成有效合并预览。");
            AddMergePlanDetails(prepareAction, request, branchWorkspace, targetWorkspace, tableIds, missingSourceTables, missingTargetTables, missingSourceLocators, missingTargetLocators);

            var compareAction = result.AddAction("merge.compare", result.Success ? "planned" : "blocked", result.Success ? "预览：执行三方一致性/冲突检查，并生成合并报告。" : "在线表定位或表范围未就绪，已阻断三方比较。");
            AddMergePlanDetails(compareAction, request, branchWorkspace, targetWorkspace, tableIds, missingSourceTables, missingTargetTables, missingSourceLocators, missingTargetLocators);
            ApplyMergeReviewRequestSummary(result, BuildMergeReviewInputSummaryForWorkspaces(request, tableIds, branchWorkspace, targetWorkspace));

            result.DocumentationTargets["sourceWikiNodeUrl"] = branchWorkspace.WikiNodeUrl;
            result.DocumentationTargets["targetWikiNodeUrl"] = FirstNonEmpty(targetWorkspace.WikiNodeUrl, request.MergeInputs.TargetBranchWikiNodeUrl);
            result.DocumentationTargets["mergeReportPath"] = FirstNonEmpty(request.MergeInputs.MergeReportPath, "Temp/ConfigSheetForge/merge-report.md");
            result.DocumentationTargets["mergedPath"] = FirstNonEmpty(request.MergeInputs.MergedPath, "Temp/ConfigSheetForge/merged.semantic.json");

            if (!result.Success)
            {
                return;
            }

            var writeBackRequested = request.MergeInputs.WriteBackToMain || request.MergePolicy.ConfirmWriteMain;
            var confirmWriteMain = request.MergeInputs.ConfirmWriteMain || request.MergePolicy.ConfirmWriteMain;
            if (writeBackRequested && !confirmWriteMain)
            {
                result.AddFailure("写回 main 是高风险操作，必须先生成合并预览，并在 Unity 窗口中勾选确认写回。");
                return;
            }

            if (confirmWriteMain && !string.Equals(request.MergePolicy.ApprovedByRole, "configOwner", StringComparison.OrdinalIgnoreCase))
            {
                result.AddFailure("写回 main 需要配置负责人批准。请让配置负责人完成审批后再重试。");
                return;
            }

            var preview = result.AddAction("merge.preview", request.DryRun ? "planned" : "ready", request.DryRun ? "已生成可审查的合并预览计划；不会写回 main。" : "合并预览有效，准备执行已确认的写入流程。");
            AddMergePlanDetails(preview, request, branchWorkspace, targetWorkspace, tableIds, missingSourceTables, missingTargetTables, missingSourceLocators, missingTargetLocators);

            if (confirmWriteMain)
            {
                var write = result.AddAction("merge.write_main", "ready", "已确认写回 main；执行前仍需通过冲突检查、Schema review 和 MergeReviews。");
                AddMergePlanDetails(write, request, branchWorkspace, targetWorkspace, tableIds, missingSourceTables, missingTargetTables, missingSourceLocators, missingTargetLocators);
            }
        }

        private static async Task SubmitMergeReviewAsync(LifecycleContractRequest request, ILifecyclePlatform platform, LifecycleContractResult result, CancellationToken cancellationToken)
        {
            request.MergeReview = request.MergeReview ?? new MergeReviewContract();
            request.MergeInputs = request.MergeInputs ?? new MergeInputsContract();
            if (string.IsNullOrWhiteSpace(request.MergeReview.TableId))
            {
                request.MergeReview.TableId = "__project_pr_gate__";
            }

            request.MergeReview.SourceBranch = FirstNonEmpty(request.MergeReview.SourceBranch, request.MergeInputs.SourceBranch, request.Git.Branch, request.BranchWorkspace.GitBranch);
            request.MergeReview.TargetBranch = FirstNonEmpty(request.MergeReview.TargetBranch, request.MergeInputs.TargetBranch, request.BranchWorkspace.MainGitBranch, "main");
            request.MergeReview.PrNumber = FirstNonEmpty(request.MergeReview.PrNumber, request.MergeInputs.PrNumber);
            request.MergeReview.PrUrl = FirstNonEmpty(request.MergeReview.PrUrl, request.MergeInputs.PrUrl);
            request.MergeReview.MergeReportPath = FirstNonEmpty(request.MergeReview.MergeReportPath, request.MergeInputs.MergeReportPath, "Temp/ConfigSheetForge/merge-report.md");
            request.MergeReview.MergedPath = FirstNonEmpty(request.MergeReview.MergedPath, request.MergeInputs.MergedPath, "Temp/ConfigSheetForge/merged.semantic.json");
            request.MergeReview.ApproverRole = FirstNonEmpty(request.MergeReview.ApproverRole, request.MergePolicy.ApprovedByRole, "configOwner");
            request.MergeReview.Status = FirstNonEmpty(request.MergeReview.Status, "approved");

            var summary = BuildMergeReviewInputSummary(request, request.MergeReview.TableIds);
            ApplyMergeReviewRequestSummary(result, summary);
            result.DocumentationTargets["mergeReportPath"] = summary.MergeReportPath;
            result.DocumentationTargets["mergedPath"] = summary.MergedPath;

            var required = FirstNonEmpty(request.MergeReview.RequestFingerprint, request.MergeReview.RequiredPreviewFingerprint, request.RequiredPreviewFingerprint);
            if (!string.IsNullOrWhiteSpace(required) &&
                !string.Equals(required, summary.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                result.AddFailure("提交合并审查记录的输入和最近一次合并预览不一致。请重新生成合并预览，再提交审查记录。");
                return;
            }

            if (!request.DryRun && !request.MergeReview.ConfirmSubmit)
            {
                result.AddFailure("提交 MergeReviews 会写入 Base，必须在 Unity 窗口确认，或在 contract.mergeReview.confirmSubmit=true。");
                return;
            }

            var reviewPlatform = request.DryRun ? new PreviewLifecyclePlatform() as IReviewRegistryPlatform : platform as IReviewRegistryPlatform;
            if (reviewPlatform == null)
            {
                result.AddFailure("当前执行平台不支持写入 MergeReviews。请使用 config-sheet-forge CLI apply-contract，或升级项目 adapter。");
                return;
            }

            var action = await reviewPlatform.UpsertMergeReviewAsync(request.Registry, request.MergeReview, summary, cancellationToken).ConfigureAwait(false);
            result.Actions.Add(action);
            if (!string.Equals(action.Status, "done", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action.Status, "planned", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action.Status, "reused", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action.Status, "updated", StringComparison.OrdinalIgnoreCase))
            {
                result.AddFailure(FirstNonEmpty(action.Message, "MergeReviews 写入失败。请展开详细日志查看 Base 字段和权限。"));
            }
            else
            {
                var summaryAction = result.AddAction("merge_review.summary", request.DryRun ? "planned" : "done", request.DryRun ? "预览：合并审查记录可提交。" : "合并审查记录已提交；现在可以重新运行 PR 检查。");
                summaryAction.Details["reviewId"] = FirstNonEmpty(action.Details.TryGetValue("reviewId", out var reviewId) ? reviewId : "", request.MergeReview.ReviewId);
                summaryAction.Details["recordId"] = action.Details.TryGetValue("recordId", out var recordId) ? recordId : "";
                summaryAction.Details["requestFingerprint"] = summary.Fingerprint;
                summaryAction.Details["writesMain"] = "false";
                summaryAction.Details["writesLocalCache"] = "false";
                summaryAction.Details["writesProjectSettings"] = "false";
                summaryAction.Details["writesExcelToSo"] = "false";
            }
        }

        private static async Task ApproveSchemaReviewAsync(LifecycleContractRequest request, ILifecyclePlatform platform, LifecycleContractResult result, CancellationToken cancellationToken)
        {
            request.SchemaReviewApproval = request.SchemaReviewApproval ?? new SchemaReviewApprovalContract();
            request.SchemaReviewApproval.Branch = FirstNonEmpty(request.SchemaReviewApproval.Branch, request.Git.FeishuBranch, request.Git.Profile, request.Git.Branch);
            request.SchemaReviewApproval.Profile = FirstNonEmpty(request.SchemaReviewApproval.Profile, request.Git.Profile, request.Git.FeishuBranch, request.Git.Branch);
            if (string.IsNullOrWhiteSpace(request.SchemaReviewApproval.TableId))
            {
                result.AddFailure("Schema 审查需要指定配表 ID。");
                return;
            }

            if (!request.DryRun && !request.SchemaReviewApproval.ConfirmSubmit)
            {
                result.AddFailure("提交 SchemaReviews 审查结果会写入 Base，必须先确认。");
                return;
            }

            var reviewPlatform = request.DryRun ? new PreviewLifecyclePlatform() as IReviewRegistryPlatform : platform as IReviewRegistryPlatform;
            if (reviewPlatform == null)
            {
                result.AddFailure("当前执行平台不支持更新 SchemaReviews。");
                return;
            }

            var action = await reviewPlatform.UpsertSchemaReviewApprovalAsync(request.Registry, request.SchemaReviewApproval, cancellationToken).ConfigureAwait(false);
            result.Actions.Add(action);
            if (!string.Equals(action.Status, "done", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action.Status, "planned", StringComparison.OrdinalIgnoreCase))
            {
                result.AddFailure(FirstNonEmpty(action.Message, "SchemaReviews 更新失败。"));
            }
        }

        private static async Task ApproveWaiverAsync(LifecycleContractRequest request, ILifecyclePlatform platform, LifecycleContractResult result, CancellationToken cancellationToken)
        {
            request.WaiverApproval = request.WaiverApproval ?? new WaiverApprovalContract();
            request.WaiverApproval.Branch = FirstNonEmpty(request.WaiverApproval.Branch, request.Git.Branch, request.Git.FeishuBranch, request.Git.Profile);
            if (string.IsNullOrWhiteSpace(request.WaiverApproval.Reason))
            {
                result.AddFailure("申请 waiver 必须填写原因。");
            }

            if (string.IsNullOrWhiteSpace(request.WaiverApproval.ExpiresAt))
            {
                result.AddFailure("申请 waiver 必须填写过期时间。");
            }

            if (!string.Equals(request.WaiverApproval.ApprovedByRole, "configOwner", StringComparison.OrdinalIgnoreCase))
            {
                result.AddFailure("批准 waiver 需要配置负责人角色 configOwner。");
            }

            if (!result.Success)
            {
                return;
            }

            if (!request.DryRun && !request.WaiverApproval.ConfirmApprove)
            {
                result.AddFailure("批准 waiver 会写入 Base，必须先确认。");
                return;
            }

            var reviewPlatform = request.DryRun ? new PreviewLifecyclePlatform() as IReviewRegistryPlatform : platform as IReviewRegistryPlatform;
            if (reviewPlatform == null)
            {
                result.AddFailure("当前执行平台不支持写入 Waivers。");
                return;
            }

            var action = await reviewPlatform.UpsertWaiverApprovalAsync(request.Registry, request.WaiverApproval, cancellationToken).ConfigureAwait(false);
            result.Actions.Add(action);
            if (!string.Equals(action.Status, "done", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action.Status, "planned", StringComparison.OrdinalIgnoreCase))
            {
                result.AddFailure(FirstNonEmpty(action.Message, "Waivers 更新失败。"));
            }
        }

        private static MergeReviewInputSummary BuildMergeReviewInputSummaryForWorkspaces(LifecycleContractRequest request, IEnumerable<string> tableIds, BranchWorkspaceResolution sourceWorkspace, BranchWorkspaceResolution targetWorkspace)
        {
            request.MergeReview = request.MergeReview ?? new MergeReviewContract();
            var previousSource = request.MergeReview.SourceBranch;
            var previousTarget = request.MergeReview.TargetBranch;
            request.MergeReview.SourceBranch = FirstNonEmpty(request.MergeReview.SourceBranch, sourceWorkspace == null ? "" : sourceWorkspace.GitBranch);
            request.MergeReview.TargetBranch = FirstNonEmpty(request.MergeReview.TargetBranch, targetWorkspace == null ? "" : targetWorkspace.GitBranch);
            var summary = BuildMergeReviewInputSummary(request, tableIds);
            request.MergeReview.SourceBranch = previousSource;
            request.MergeReview.TargetBranch = previousTarget;
            return summary;
        }

        private static void ApplyMergeReviewRequestSummary(LifecycleContractResult result, MergeReviewInputSummary summary)
        {
            if (result == null || summary == null)
            {
                return;
            }

            result.RequestFingerprint = summary.Fingerprint;
            result.RequestSummary["sourceBranch"] = summary.SourceBranch;
            result.RequestSummary["targetBranch"] = summary.TargetBranch;
            result.RequestSummary["tableIds"] = summary.TableIdsText;
            result.RequestSummary["tableCount"] = summary.TableIds.Count.ToString(CultureInfo.InvariantCulture);
            result.RequestSummary["prNumber"] = summary.PrNumber;
            result.RequestSummary["prUrl"] = summary.PrUrl;
            result.RequestSummary["mergeReportPath"] = summary.MergeReportPath;
            result.RequestSummary["mergedPath"] = summary.MergedPath;
        }

        private static LifecycleContractRequest BuildTargetMergeRequest(LifecycleContractRequest sourceRequest)
        {
            var inputs = sourceRequest.MergeInputs ?? new MergeInputsContract();
            var sourceWorkspaceContract = BranchWorkspaceResolver.NormalizeContract(sourceRequest);
            var targetBranch = FirstNonEmpty(inputs.TargetBranch, sourceWorkspaceContract.MainGitBranch, "main");
            var targetProfile = FirstNonEmpty(inputs.TargetFeishuProfile, string.Equals(targetBranch, FirstNonEmpty(sourceWorkspaceContract.MainGitBranch, "main"), StringComparison.OrdinalIgnoreCase) ? FirstNonEmpty(sourceWorkspaceContract.MainFeishuBranch, "main") : "");
            return new LifecycleContractRequest
            {
                Operation = sourceRequest.Operation,
                Locale = sourceRequest.Locale,
                DryRun = sourceRequest.DryRun,
                Registry = sourceRequest.Registry,
                Table = sourceRequest.Table,
                Git = new ContractGitSpec
                {
                    Branch = targetBranch,
                    FeishuBranch = targetProfile,
                    Profile = targetProfile,
                    Head = sourceRequest.Git != null ? sourceRequest.Git.Head : ""
                },
                SeedFromLocalXlsx = sourceRequest.SeedFromLocalXlsx,
                SyncCache = sourceRequest.SyncCache,
                MergePolicy = sourceRequest.MergePolicy,
                MergeInputs = sourceRequest.MergeInputs,
                BranchWorkspace = new BranchWorkspaceContract
                {
                    Mode = sourceWorkspaceContract.Mode,
                    RootWikiToken = sourceWorkspaceContract.RootWikiToken,
                    RootWikiUrl = sourceWorkspaceContract.RootWikiUrl,
                    RootWikiTitle = sourceWorkspaceContract.RootWikiTitle,
                    GitBranch = targetBranch,
                    FeishuBranch = targetProfile,
                    Profile = targetProfile,
                    MainGitBranch = sourceWorkspaceContract.MainGitBranch,
                    MainFeishuBranch = sourceWorkspaceContract.MainFeishuBranch,
                    ProfileNameTemplate = sourceWorkspaceContract.ProfileNameTemplate,
                    BranchNodeTitleTemplate = sourceWorkspaceContract.BranchNodeTitleTemplate,
                    MainNodeTitle = sourceWorkspaceContract.MainNodeTitle,
                    CreateIfMissing = sourceWorkspaceContract.CreateIfMissing,
                    RequireOneToOneBinding = sourceWorkspaceContract.RequireOneToOneBinding,
                    BindingRegistryTable = sourceWorkspaceContract.BindingRegistryTable,
                    OwnerRole = sourceWorkspaceContract.OwnerRole,
                    CreatedBy = sourceWorkspaceContract.CreatedBy,
                    ExistingWikiNodeToken = inputs.TargetBranchWikiNodeToken,
                    ExistingWikiNodeUrl = inputs.TargetBranchWikiNodeUrl
                },
                BranchBindings = sourceRequest.BranchBindings ?? new List<BranchBindingContract>(),
                DocumentationLinks = sourceRequest.DocumentationLinks
            };
        }

        private static List<SeedTableContract> FindMergeTableRows(LifecycleContractRequest request, string profile, string selectedTable)
        {
            var seed = request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract();
            return seed.Tables
                .Where(t => !string.IsNullOrWhiteSpace(t.TableId))
                .Where(t => string.IsNullOrWhiteSpace(selectedTable) || string.Equals(t.TableId, selectedTable, StringComparison.OrdinalIgnoreCase))
                .Where(t => string.Equals(FirstNonEmpty(t.Profile, t.Branch), profile, StringComparison.OrdinalIgnoreCase))
                .GroupBy(t => t.TableId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(t => t.TableId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> BuildMergeTableScope(List<SeedTableContract> sourceRows, List<SeedTableContract> targetRows, string selectedTable)
        {
            var tableIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(selectedTable))
            {
                tableIds.Add(selectedTable);
            }

            foreach (var table in sourceRows)
            {
                tableIds.Add(table.TableId);
            }

            foreach (var table in targetRows)
            {
                tableIds.Add(table.TableId);
            }

            return tableIds.ToList();
        }

        private static bool HasOnlineSheetLocator(SeedTableContract table)
        {
            if (table == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(FirstNonEmpty(table.SpreadsheetToken, table.SpreadsheetUrl)) &&
                   !string.IsNullOrWhiteSpace(table.SheetId);
        }

        private static void AddMergePlanDetails(
            LifecycleActionResult action,
            LifecycleContractRequest request,
            BranchWorkspaceResolution sourceWorkspace,
            BranchWorkspaceResolution targetWorkspace,
            List<string> tableIds,
            List<string> missingSourceTables,
            List<string> missingTargetTables,
            List<string> missingSourceLocators,
            List<string> missingTargetLocators)
        {
            var inputs = request.MergeInputs ?? new MergeInputsContract();
            action.Details["sourceBranch"] = sourceWorkspace.GitBranch;
            action.Details["sourceProfile"] = FirstNonEmpty(sourceWorkspace.Profile, sourceWorkspace.FeishuBranch);
            action.Details["sourceWikiNodeToken"] = sourceWorkspace.WikiNodeToken;
            action.Details["sourceWikiNodeUrl"] = sourceWorkspace.WikiNodeUrl;
            action.Details["targetBranch"] = targetWorkspace.GitBranch;
            action.Details["targetProfile"] = FirstNonEmpty(targetWorkspace.Profile, targetWorkspace.FeishuBranch);
            action.Details["targetWikiNodeToken"] = FirstNonEmpty(targetWorkspace.WikiNodeToken, inputs.TargetBranchWikiNodeToken);
            action.Details["targetWikiNodeUrl"] = FirstNonEmpty(targetWorkspace.WikiNodeUrl, inputs.TargetBranchWikiNodeUrl);
            action.Details["tableCount"] = tableIds.Count.ToString(CultureInfo.InvariantCulture);
            action.Details["tableIds"] = string.Join(", ", tableIds);
            action.Details["missingSourceTables"] = string.Join(", ", missingSourceTables);
            action.Details["missingTargetTables"] = string.Join(", ", missingTargetTables);
            action.Details["missingSourceLocators"] = string.Join(", ", missingSourceLocators);
            action.Details["missingTargetLocators"] = string.Join(", ", missingTargetLocators);
            action.Details["basePath"] = inputs.BasePath;
            action.Details["oursPath"] = inputs.OursPath;
            action.Details["theirsPath"] = inputs.TheirsPath;
            action.Details["mergeBase"] = inputs.MergeBase;
            action.Details["prNumber"] = inputs.PrNumber;
            action.Details["prUrl"] = inputs.PrUrl;
            action.Details["mergeReportPath"] = FirstNonEmpty(inputs.MergeReportPath, "Temp/ConfigSheetForge/merge-report.md");
            action.Details["mergedPath"] = FirstNonEmpty(inputs.MergedPath, "Temp/ConfigSheetForge/merged.semantic.json");
            action.Details["writeBackToMain"] = inputs.WriteBackToMain.ToString().ToLowerInvariant();
        }

        private static void ApplyBranchStatus(LifecycleContractRequest request, LifecycleContractResult result)
        {
            var branchWorkspace = BranchWorkspaceResolver.Resolve(request);
            result.BranchWorkspace = branchWorkspace;
            BranchWorkspaceResolver.ValidateOneToOne(request, branchWorkspace, result);
            result.Actions.Add(BranchWorkspaceResolver.BuildAction(branchWorkspace, request.DryRun ? "done" : "done", "只读：已从注册中心上下文解析当前分支工作区。"));
            PopulateBranchStatus(request, result, branchWorkspace);
        }

        private static void ApplyCurrentBranchBootstrapPlan(LifecycleContractRequest request, LifecycleContractResult result)
        {
            request.MergeInputs = request.MergeInputs ?? new MergeInputsContract();
            var branchWorkspace = BranchWorkspaceResolver.Resolve(request);
            result.BranchWorkspace = branchWorkspace;
            PopulateBranchStatus(request, result, branchWorkspace);

            var targetBranch = FirstNonEmpty(request.MergeInputs.TargetBranch, request.BranchWorkspace.MainGitBranch, "main");
            var targetProfile = FirstNonEmpty(request.MergeInputs.TargetFeishuProfile, request.BranchWorkspace.MainFeishuBranch, targetBranch);
            var tableIds = BuildExpectedTableIds(request, request.SyncCache != null ? request.SyncCache.TableId : "");
            var targetRows = FindConfigSheetRowsForProfile(request, targetProfile, request.SyncCache != null ? request.SyncCache.TableId : "");
            var targetRegistered = new HashSet<string>(
                targetRows.Select(t => t.TableId).Where(t => !string.IsNullOrWhiteSpace(t)),
                StringComparer.OrdinalIgnoreCase);
            var missingTargetTables = tableIds.Where(t => !targetRegistered.Contains(t)).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
            var missingTargetLocators = targetRows
                .Where(t => string.IsNullOrWhiteSpace(FirstNonEmpty(t.SpreadsheetToken, t.SpreadsheetUrl)) || string.IsNullOrWhiteSpace(t.SheetId))
                .Select(t => t.TableId)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.RequestSummary["sourceMode"] = "target-branch";
            result.RequestSummary["targetBranch"] = targetBranch;
            result.RequestSummary["targetProfile"] = targetProfile;
            result.RequestSummary["currentBranch"] = branchWorkspace.GitBranch;
            result.RequestSummary["currentProfile"] = FirstNonEmpty(branchWorkspace.Profile, branchWorkspace.FeishuBranch);
            result.RequestSummary["tableIds"] = string.Join(", ", tableIds);
            result.RequestSummary["tableCount"] = tableIds.Count.ToString(CultureInfo.InvariantCulture);

            var plan = result.AddAction("current_branch.bootstrap_from_target.plan", request.DryRun ? "planned" : "blocked", "从目标分支 “" + targetBranch + "” 派生当前分支在线工作区和 ConfigSheets；dry-run 只生成计划。");
            plan.Details["targetBranch"] = targetBranch;
            plan.Details["targetProfile"] = targetProfile;
            plan.Details["currentBranch"] = branchWorkspace.GitBranch;
            plan.Details["currentProfile"] = FirstNonEmpty(branchWorkspace.Profile, branchWorkspace.FeishuBranch);
            plan.Details["branchNodeTitle"] = branchWorkspace.NodeTitle;
            plan.Details["tableCount"] = tableIds.Count.ToString(CultureInfo.InvariantCulture);
            plan.Details["tableIds"] = string.Join(", ", tableIds);
            plan.Details["targetRegisteredTableCount"] = targetRows.Count.ToString(CultureInfo.InvariantCulture);
            plan.Details["missingTargetTables"] = string.Join(", ", missingTargetTables);
            plan.Details["missingTargetLocators"] = string.Join(", ", missingTargetLocators);
            plan.Details["writesLocalCache"] = "false";
            plan.Details["writesProjectSettings"] = "false";
            plan.Details["writesExcelToSo"] = "false";

            result.AddAction("current_branch.workspace.ensure", request.DryRun ? "planned" : "blocked", "创建/复用当前分支 Wiki 节点 “" + branchWorkspace.NodeTitle + "”。");
            result.AddAction("current_branch.sheets.copy_from_target", request.DryRun ? "planned" : "blocked", "按目标分支在线表导出/导入，创建或复用当前分支在线 Sheet。");
            result.AddAction("current_branch.registry.upsert", request.DryRun ? "planned" : "blocked", "upsert BranchBindings、ConfigSheets 和 SchemaReviews baseline。");

            if (missingTargetTables.Count > 0 || missingTargetLocators.Count > 0)
            {
                if (missingTargetTables.Count > 0)
                {
                    result.AddFailure("无法从目标分支派生当前分支：目标分支 “" + targetBranch + "” 缺少 ConfigSheets 记录：" + string.Join(", ", missingTargetTables) + "。");
                }

                if (missingTargetLocators.Count > 0)
                {
                    result.AddFailure("无法从目标分支派生当前分支：目标分支这些表缺少 SpreadsheetToken/SheetId：" + string.Join(", ", missingTargetLocators) + "。");
                }

                return;
            }

            if (!request.DryRun)
            {
                result.AddFailure("从目标分支初始化当前分支在线表的 apply 需要项目 adapter 或后续 CLI 复制实现；当前版本先提供一等 dry-run 入口，避免误导用户去做本地 Excel Seed。");
            }
        }

        private static void PopulateBranchStatus(LifecycleContractRequest request, LifecycleContractResult result, BranchWorkspaceResolution branchWorkspace)
        {
            request.SyncCache = request.SyncCache ?? new SyncCacheContract();
            var profile = FirstNonEmpty(branchWorkspace.Profile, branchWorkspace.FeishuBranch, request.Git.Profile, request.Git.FeishuBranch, request.Git.Branch);
            var selectedTable = FirstNonEmpty(request.SyncCache.TableId, request.Table != null ? request.Table.TableId : "");
            var expected = BuildExpectedTableIds(request, selectedTable);
            var rows = FindConfigSheetRowsForProfile(request, profile, selectedTable);
            var duplicateRows = FindDuplicateConfigSheetRows(rows);
            var resolved = new List<ResolvedOnlineTableStatus>();
            foreach (var row in rows
                .GroupBy(t => t.TableId ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(t => t.TableId, StringComparer.OrdinalIgnoreCase))
            {
                resolved.Add(ToResolvedOnlineTable(row, profile));
            }

            var registered = new HashSet<string>(
                resolved
                    .Where(t => !string.IsNullOrWhiteSpace(t.TableId))
                    .Select(t => t.TableId),
                StringComparer.OrdinalIgnoreCase);
            var missingTables = expected
                .Where(t => !registered.Contains(t))
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var missingLocators = resolved
                .Where(t => string.IsNullOrWhiteSpace(FirstNonEmpty(t.SpreadsheetToken, t.OnlineSheetUrl)) || string.IsNullOrWhiteSpace(t.SheetId))
                .Select(t => t.TableId)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var status = new BranchStatusSummary
            {
                CurrentGitBranch = branchWorkspace.GitBranch,
                CurrentProfile = profile,
                TargetBranch = FirstNonEmpty(request.MergeInputs != null ? request.MergeInputs.TargetBranch : "", request.BranchWorkspace.MainGitBranch, "main"),
                TargetProfile = FirstNonEmpty(request.MergeInputs != null ? request.MergeInputs.TargetFeishuProfile : "", request.BranchWorkspace.MainFeishuBranch, "main"),
                BranchWikiNodeTitle = branchWorkspace.NodeTitle,
                BranchWikiNodeUrl = branchWorkspace.WikiNodeUrl,
                BranchWikiNodeToken = branchWorkspace.WikiNodeToken,
                ExpectedTableIds = expected,
                RegisteredOnlineTables = resolved,
                TableCountExpected = expected.Count,
                TableCountRegistered = resolved.Count,
                MissingTables = missingTables,
                MissingLocators = missingLocators,
                DuplicateConfigSheets = duplicateRows,
                CanReadRegistry = true,
                CanReadSheetsMetadata = true
            };

            if (duplicateRows.Count > 0)
            {
                status.BranchBindingStatus = "duplicate";
                status.HumanReadableFailures.Add("ConfigSheets 中存在重复记录：" + string.Join(", ", duplicateRows) + "。请先清理重复行，工具不会静默任选一条。");
                status.NextRecommendedAction = "cleanup-duplicate-config-sheets";
            }
            else if (string.IsNullOrWhiteSpace(branchWorkspace.WikiNodeToken) && string.IsNullOrWhiteSpace(branchWorkspace.WikiNodeUrl))
            {
                status.BranchBindingStatus = "missing";
                status.HumanReadableFailures.Add("当前分支还没有 BranchBindings 工作区记录。下一步：从目标分支初始化当前分支在线表。");
                status.NextRecommendedAction = "bootstrap-current-branch-from-target";
            }
            else if (missingTables.Count > 0 || missingLocators.Count > 0)
            {
                status.BranchBindingStatus = "ok";
                if (missingTables.Count > 0)
                {
                    status.HumanReadableFailures.Add("当前分支缺少 ConfigSheets 记录：" + string.Join(", ", missingTables) + "。");
                }

                if (missingLocators.Count > 0)
                {
                    status.HumanReadableFailures.Add("当前分支这些表缺少在线 Sheet 定位：" + string.Join(", ", missingLocators) + "。");
                }

                status.NextRecommendedAction = resolved.Count == 0 ? "bootstrap-current-branch-from-target" : "fix-config-sheets";
            }
            else
            {
                status.BranchBindingStatus = "ok";
                status.NextRecommendedAction = "preview-sync-cache";
            }

            result.BranchStatus = status;
            result.ResolvedOnlineTables = new List<ResolvedOnlineTableStatus>(resolved);
            result.SyncCacheSummary.ResolvedOnlineTables = new List<ResolvedOnlineTableStatus>(resolved);
            result.SyncCacheSummary.TableCount = resolved.Count;

            result.SeedTables.Clear();
            foreach (var table in resolved)
            {
                result.SeedTables.Add(new SeedTableLifecycleResult
                {
                    TableId = table.TableId,
                    DisplayName = table.DisplayName,
                    Status = string.IsNullOrWhiteSpace(table.BlockingReason) ? "registered" : "blocked",
                    SpreadsheetToken = table.SpreadsheetToken,
                    SpreadsheetUrl = table.OnlineSheetUrl,
                    SheetId = table.SheetId,
                    WikiNodeToken = table.WikiNodeToken,
                    WikiNodeUrl = table.WikiNodeUrl,
                    Branch = table.Branch,
                    Profile = table.Profile,
                    RegistryRecordId = table.RecordId,
                    SemanticHash = table.SemanticHash
                });
            }
        }

        private static List<string> BuildExpectedTableIds(LifecycleContractRequest request, string selectedTable)
        {
            var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(selectedTable))
            {
                result.Add(selectedTable);
            }

            foreach (var table in request.SeedFromLocalXlsx != null ? request.SeedFromLocalXlsx.Tables : new List<SeedTableContract>())
            {
                if (!string.IsNullOrWhiteSpace(table.TableId) &&
                    (string.IsNullOrWhiteSpace(selectedTable) || string.Equals(table.TableId, selectedTable, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(table.TableId);
                }
            }

            if (result.Count == 0 && request.Table != null && !string.IsNullOrWhiteSpace(request.Table.TableId))
            {
                result.Add(request.Table.TableId);
            }

            return result.ToList();
        }

        private static List<SeedTableContract> FindConfigSheetRowsForProfile(LifecycleContractRequest request, string profile, string selectedTable)
        {
            var seed = request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract();
            return seed.Tables
                .Where(t => !string.IsNullOrWhiteSpace(t.TableId))
                .Where(t => string.IsNullOrWhiteSpace(selectedTable) || string.Equals(t.TableId, selectedTable, StringComparison.OrdinalIgnoreCase))
                .Where(t =>
                {
                    var tableProfile = FirstNonEmpty(t.Profile, t.Branch);
                    if (!string.IsNullOrWhiteSpace(tableProfile))
                    {
                        return string.IsNullOrWhiteSpace(profile) ||
                               string.Equals(tableProfile, profile, StringComparison.OrdinalIgnoreCase);
                    }

                    return HasOnlineLocator(t);
                })
                .OrderBy(t => t.TableId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool HasOnlineLocator(SeedTableContract table)
        {
            return table != null &&
                   (!string.IsNullOrWhiteSpace(table.SpreadsheetToken) ||
                    !string.IsNullOrWhiteSpace(table.SpreadsheetUrl) ||
                    !string.IsNullOrWhiteSpace(table.SheetId));
        }

        private static List<string> FindDuplicateConfigSheetRows(List<SeedTableContract> rows)
        {
            return rows
                .GroupBy(t => t.TableId ?? "", StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
                .Select(g => g.Key + "(" + string.Join(", ", g.Select(t => FirstNonEmpty(t.RegistryRecordId, "无 record_id"))) + ")")
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ResolvedOnlineTableStatus ToResolvedOnlineTable(SeedTableContract table, string fallbackProfile)
        {
            var onlineUrl = table.SpreadsheetUrl;
            return new ResolvedOnlineTableStatus
            {
                TableId = table.TableId,
                DisplayName = FirstNonEmpty(table.DisplayName, table.TableId),
                RecordId = table.RegistryRecordId,
                SpreadsheetToken = table.SpreadsheetToken,
                SheetId = table.SheetId,
                OnlineSheetUrl = onlineUrl,
                WikiNodeToken = table.WikiNodeToken,
                WikiNodeUrl = table.WikiNodeUrl,
                SemanticHash = table.SemanticHash,
                OwnerRole = table.OwnerRole,
                Branch = table.Branch,
                Profile = FirstNonEmpty(table.Profile, table.Branch, fallbackProfile),
                BlockingReason = BuildTableLocatorBlockingReason(table)
            };
        }

        private static string BuildTableLocatorBlockingReason(SeedTableContract table)
        {
            if (table == null)
            {
                return "在线表记录为空。";
            }

            if (string.IsNullOrWhiteSpace(table.TableId))
            {
                return "在线表记录缺少 TableId。";
            }

            if (string.IsNullOrWhiteSpace(FirstNonEmpty(table.SpreadsheetToken, table.SpreadsheetUrl)))
            {
                return "缺少在线 Sheet token 或链接。";
            }

            if (string.IsNullOrWhiteSpace(table.SheetId))
            {
                return "缺少工作表 ID。";
            }

            return "";
        }

        private static void ApplySyncCachePlan(LifecycleContractRequest request, LifecycleContractResult result)
        {
            var branchWorkspace = BranchWorkspaceResolver.Resolve(request);
            result.BranchWorkspace = branchWorkspace;
            BranchWorkspaceResolver.ValidateOneToOne(request, branchWorkspace, result);
            result.Actions.Add(BranchWorkspaceResolver.BuildAction(branchWorkspace, request.DryRun ? "planned" : "ready", request.DryRun ? "预览：从 BranchBindings 解析当前分支工作区，不写飞书或本地 cache。" : "已解析当前分支工作区。"));
            PopulateBranchStatus(request, result, branchWorkspace);
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

            if (result.BranchStatus.MissingTables.Count > 0 || result.BranchStatus.MissingLocators.Count > 0 || result.BranchStatus.DuplicateConfigSheets.Count > 0)
            {
                if (result.BranchStatus.MissingTables.Count > 0)
                {
                    result.AddFailure("sync-cache 需要 live registry 中当前 branch/profile 的 ConfigSheets；缺少：" + string.Join(", ", result.BranchStatus.MissingTables) + "。");
                }

                if (result.BranchStatus.MissingLocators.Count > 0)
                {
                    result.AddFailure("sync-cache 需要 ConfigSheets 提供 SpreadsheetToken/SheetId；缺少定位：" + string.Join(", ", result.BranchStatus.MissingLocators) + "。");
                }

                if (result.BranchStatus.DuplicateConfigSheets.Count > 0)
                {
                    result.AddFailure("sync-cache 发现 ConfigSheets 重复记录：" + string.Join(", ", result.BranchStatus.DuplicateConfigSheets) + "。请先清理重复记录。");
                }

                result.SyncCacheSummary.CacheStatus = "blocked";
                result.SyncCacheSummary.BlockedTables.AddRange(result.BranchStatus.MissingTables.Concat(result.BranchStatus.MissingLocators).Distinct(StringComparer.OrdinalIgnoreCase));
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
            var branchProfiles = branchMatches
                .Select(b => FirstNonEmpty(b.Profile, b.FeishuBranch))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (branchProfiles.Count > 1)
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
        public bool Waived { get; set; }
        public string GateState { get; set; } = "";
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
        public List<string> WaivedFailures { get; set; } = new List<string>();
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
        public string ReviewId { get; set; } = "";
        public string ApproverRole { get; set; } = "";
        public string GitBranch { get; set; } = "";
        public string TableId { get; set; } = "";
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
        public string Status { get; set; } = "";
        public string ApprovedByRole { get; set; } = "";
        public string ExpiresAt { get; set; } = "";
        public string Branch { get; set; } = "";
        public string TableId { get; set; } = "";
        public string Reason { get; set; } = "";
        public string RecordId { get; set; } = "";
        public string Message { get; set; } = "";
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
            report.WaivedFailures.Clear();
            report.Waived = false;
            report.GateState = "";
            NormalizeReportStatuses(report);
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

            if (report.ChangedTables.Count > 0 && report.CacheHashes.Count == 0)
            {
                Add(report, "缺少同步报告。请先运行 sync，生成 semantic cache 和 sha256 后再跑 gate。");
            }

            ApplyValidWaiver(report);
            report.Passed = report.HumanReadableFailures.Count == 0;
            if (string.IsNullOrWhiteSpace(report.GateState))
            {
                report.GateState = report.Passed ? "passed" : "failed";
            }
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

            if (!string.IsNullOrWhiteSpace(report.Waiver.Status) && !ReviewPassed(report.Waiver.Status))
            {
                Add(report, "同步豁免状态不是 approved/completed/passed。请让配置负责人重新批准 waiver。");
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

        private static void ApplyValidWaiver(PrGateReport report)
        {
            if (!HasWaiver(report))
            {
                return;
            }

            var beforeValidation = report.HumanReadableFailures.Count;
            ValidateWaiver(report);
            var waiverValidationFailed = report.HumanReadableFailures.Count > beforeValidation;
            if (waiverValidationFailed)
            {
                return;
            }

            var retained = new List<string>();
            var waived = new List<string>();
            foreach (var failure in report.HumanReadableFailures)
            {
                if (IsNonWaivableFailure(failure))
                {
                    retained.Add(failure);
                }
                else
                {
                    waived.Add(failure);
                }
            }

            if (waived.Count == 0)
            {
                return;
            }

            report.Waived = true;
            report.GateState = "waived";
            report.Waiver.Message = "已由配置负责人 waiver 临时放行。";
            report.WaivedFailures.AddRange(waived);
            report.HumanReadableFailures.Clear();
            report.HumanReadableFailures.AddRange(retained);
        }

        private static bool HasWaiver(PrGateReport report)
        {
            return report.Waiver.Approved ||
                   !string.IsNullOrWhiteSpace(report.Waiver.RecordId) ||
                   !string.IsNullOrWhiteSpace(report.Waiver.Status);
        }

        private static bool IsNonWaivableFailure(string failure)
        {
            failure = failure ?? "";
            return failure.IndexOf("gitHead", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   failure.IndexOf("缺少 branch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   failure.IndexOf("无权", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   failure.IndexOf("权限", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   failure.IndexOf("读取 Base 注册中心", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   failure.IndexOf("读取在线 Sheet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool ReviewPassed(string status)
        {
            foreach (var candidate in NormalizeReviewStatusCandidates(status))
            {
                if (string.Equals(candidate, "approved", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate, "completed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate, "passed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate, "通过", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate, "已通过", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate, "完成", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate, "已完成", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string NormalizeReviewStatus(string status)
        {
            var candidates = NormalizeReviewStatusCandidates(status);
            return candidates.Count > 0 ? candidates[0] : "";
        }

        public static IReadOnlyList<string> NormalizeReviewStatusCandidates(string status)
        {
            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trimmed = (status ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return candidates;
            }

            if (LooksLikeJson(trimmed))
            {
                try
                {
                    CollectReviewStatusCandidates(SimpleJsonParser.Parse(trimmed), candidates, seen);
                }
                catch (Exception)
                {
                    AddReviewStatusCandidate(candidates, seen, trimmed);
                }
            }
            else
            {
                AddReviewStatusCandidate(candidates, seen, trimmed);
            }

            return candidates;
        }

        private static void NormalizeReportStatuses(PrGateReport report)
        {
            report.BranchBinding.Status = NormalizeReviewStatus(report.BranchBinding.Status);
            report.MergeReview.Status = NormalizeReviewStatus(report.MergeReview.Status);
            report.SchemaReview.Status = NormalizeReviewStatus(report.SchemaReview.Status);
            report.Waiver.Status = NormalizeReviewStatus(report.Waiver.Status);
        }

        private static bool LooksLikeJson(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var first = value[0];
            return first == '[' || first == '{' || first == '"';
        }

        private static void CollectReviewStatusCandidates(SimpleJsonValue value, IList<string> candidates, ISet<string> seen)
        {
            if (value == null)
            {
                return;
            }

            if (value.Kind == SimpleJsonKind.String || value.Kind == SimpleJsonKind.Number)
            {
                AddReviewStatusCandidate(candidates, seen, value.StringValue);
                return;
            }

            if (value.Kind == SimpleJsonKind.Boolean)
            {
                AddReviewStatusCandidate(candidates, seen, value.BooleanValue ? "true" : "false");
                return;
            }

            if (value.Kind == SimpleJsonKind.Array)
            {
                foreach (var item in value.ArrayValue)
                {
                    CollectReviewStatusCandidates(item, candidates, seen);
                }

                return;
            }

            if (value.Kind != SimpleJsonKind.Object)
            {
                return;
            }

            var countBeforeObject = candidates.Count;
            foreach (var name in new[] { "text", "name", "value", "label", "option_name", "optionName", "status", "Status" })
            {
                if (value.ObjectValue.TryGetValue(name, out var property))
                {
                    CollectReviewStatusCandidates(property, candidates, seen);
                }
            }

            if (candidates.Count > countBeforeObject)
            {
                return;
            }

            foreach (var property in value.ObjectValue.Values)
            {
                CollectReviewStatusCandidates(property, candidates, seen);
            }
        }

        private static void AddReviewStatusCandidate(IList<string> candidates, ISet<string> seen, string value)
        {
            var trimmed = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || seen.Contains(trimmed))
            {
                return;
            }

            seen.Add(trimmed);
            candidates.Add(trimmed);
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
