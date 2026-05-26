using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ConfigSheetForge.Core
{
    public sealed class ProjectConfigSummary
    {
        public string ProjectConfigPath { get; set; } = "";
        public bool Exists { get; set; }
        public string SchemaVersion { get; set; } = "";
        public int TableCount { get; set; }
        public int CurrentBranchTableCount { get; set; }
        public string CurrentBranchTableSource { get; set; } = "";
        public string LifecycleApplyMode { get; set; } = "";
        public string GateReportPath { get; set; } = "";
        public string GitBranch { get; set; } = "";
        public string FeishuBranch { get; set; } = "";
        public string Profile { get; set; } = "";
        public string ProfileNameTemplate { get; set; } = "";
        public string BranchNodeTitleTemplate { get; set; } = "";
        public string MainGitBranch { get; set; } = "";
        public string MainFeishuBranch { get; set; } = "";
        public string BranchWorkspaceRootWikiUrl { get; set; } = "";
        public string BranchWorkspaceRootWikiToken { get; set; } = "";
        public string BranchWikiNodeTitle { get; set; } = "";
        public string BranchWikiNodeUrl { get; set; } = "";
        public string BranchWikiNodeToken { get; set; } = "";
        public string RegistryBaseToken { get; set; } = "";
        public string RegistryBaseUrl { get; set; } = "";
        public string AdapterScript { get; set; } = "";
        public string AdapterInterpreter { get; set; } = "";
        public string ContractCommand { get; set; } = "";
        public string ContractRequestPath { get; set; } = "";
        public string CoreCliEnvironmentVariable { get; set; } = "";
        public string SourceCheckoutEnvironmentVariable { get; set; } = "";
        public string SourceCliProjectRelativePath { get; set; } = "";
        public List<string> ContractArguments { get; set; } = new List<string>();
        public List<ProjectConfigTableSummary> Tables { get; set; } = new List<ProjectConfigTableSummary>();
        public List<ProjectConfigTableSummary> CurrentBranchTables { get; set; } = new List<ProjectConfigTableSummary>();
        public List<BranchBindingContract> BranchBindings { get; set; } = new List<BranchBindingContract>();
        public List<string> Diagnostics { get; set; } = new List<string>();

        public bool HasLifecycleAdapter
        {
            get
            {
                return !string.IsNullOrWhiteSpace(AdapterScript) ||
                       !string.IsNullOrWhiteSpace(ContractCommand);
            }
        }

        public string BranchProfile
        {
            get
            {
                return FirstNonEmpty(Profile, FeishuBranch);
            }
        }

        public string AdapterDescription
        {
            get
            {
                return FirstNonEmpty(AdapterScript, ContractCommand);
            }
        }

        internal static string FirstNonEmpty(params string[] values)
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

    public sealed class ProjectConfigTableSummary
    {
        public string TableId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string OnlineSheetUrl { get; set; } = "";
        public string SpreadsheetToken { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string Branch { get; set; } = "";
        public string Profile { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public string WikiNodeUrl { get; set; } = "";
        public string SemanticHash { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
        public string OwnerRole { get; set; } = "";
        public string Status { get; set; } = "";
        public string SemanticCachePath { get; set; } = "";
        public string HashCachePath { get; set; } = "";
        public string CacheXlsxPath { get; set; } = "";
        public string SchemaStatus { get; set; } = "";
        public string Source { get; set; } = "";
        public string BlockingReason { get; set; } = "";

        public string EffectiveProfile
        {
            get { return ProjectConfigSummary.FirstNonEmpty(Profile, Branch); }
        }
    }

    public static class ProjectConfigProbe
    {
        private static readonly string[] TableArrayNames =
        {
            "tables",
            "configSheets",
            "tableMappings",
            "excelTables",
            "excelToSoTables"
        };

        public static ProjectConfigSummary ProbeFile(string path)
        {
            return ProbeFile(path, "");
        }

        public static ProjectConfigSummary ProbeFile(string path, string currentGitBranch)
        {
            var summary = new ProjectConfigSummary { ProjectConfigPath = path ?? "" };
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return summary;
            }

            return ProbeJson(path, File.ReadAllText(path), currentGitBranch);
        }

        public static ProjectConfigSummary ProbeJson(string path, string json)
        {
            return ProbeJson(path, json, "");
        }

        public static ProjectConfigSummary ProbeJson(string path, string json, string currentGitBranch)
        {
            var summary = new ProjectConfigSummary
            {
                ProjectConfigPath = path ?? "",
                Exists = true
            };

            SimpleJsonValue root;
            try
            {
                root = SimpleJsonParser.Parse(json ?? "");
            }
            catch (Exception ex)
            {
                summary.Diagnostics.Add("项目配置 JSON 无法解析：" + ex.Message);
                return summary;
            }

            if (root.Kind != SimpleJsonKind.Object)
            {
                summary.Diagnostics.Add("项目配置不是 JSON object。");
                return summary;
            }

            ReadSharedSummary(summary, root);
            summary.Tables.AddRange(ParseRootTableArray(root, "project-config"));
            summary.TableCount = summary.Tables.Count;
            summary.BranchBindings.AddRange(ParseBranchBindings(root));

            var liveTables = ParseConfigSheetArrays(root);
            ApplyBranchContext(summary, currentGitBranch, root);
            ApplyCurrentBranchTables(summary, liveTables);
            return summary;
        }

        private static void ReadSharedSummary(ProjectConfigSummary summary, SimpleJsonValue root)
        {
            summary.SchemaVersion = FirstNonEmpty(
                GetString(root, "schemaVersion"),
                GetString(root, "schema"),
                GetString(root, "version"));
            summary.LifecycleApplyMode = FirstNonEmpty(
                GetString(root, "lifecycleApplyMode"),
                GetString(root, "applyMode"),
                GetString(root, "writeMode"),
                FindStringDeep(root, "lifecycleApplyMode", "applyMode", "writeMode"));
            summary.GateReportPath = FirstNonEmpty(
                GetString(root, "gateReportPath", "prGateReportPath", "defaultGateReportPath", "reportPath"),
                FindStringDeep(root, "gateReportPath", "prGateReportPath", "defaultGateReportPath"));
            summary.AdapterScript = FirstNonEmpty(
                GetString(root, "adapterScript", "lifecycleAdapterScript", "contractAdapterScript", "adapterPath"),
                FindStringDeep(root, "adapterScript", "lifecycleAdapterScript", "contractAdapterScript", "adapterPath"));
            summary.AdapterInterpreter = FirstNonEmpty(
                GetString(root, "adapterInterpreter", "scriptInterpreter", "interpreter"),
                FindStringDeep(root, "adapterInterpreter", "scriptInterpreter"));
            summary.ContractCommand = FirstNonEmpty(
                GetString(root, "contractCommand", "lifecycleContractCommand", "adapterCommand"),
                FindStringDeep(root, "contractCommand", "lifecycleContractCommand", "adapterCommand"));
            summary.ContractRequestPath = FirstNonEmpty(
                GetString(root, "contractRequestPath", "requestPath", "contractJsonPath"),
                FindStringDeep(root, "contractRequestPath", "requestPath", "contractJsonPath"));
            summary.CoreCliEnvironmentVariable = FirstNonEmpty(
                GetString(root, "coreCliEnvironmentVariable", "cliEnvironmentVariable"),
                FindStringDeep(root, "coreCliEnvironmentVariable", "cliEnvironmentVariable"),
                "CONFIG_SHEET_FORGE_CLI");
            summary.SourceCheckoutEnvironmentVariable = FirstNonEmpty(
                GetString(root, "sourceCheckoutEnvironmentVariable", "checkoutEnvironmentVariable"),
                FindStringDeep(root, "sourceCheckoutEnvironmentVariable", "checkoutEnvironmentVariable"),
                "CONFIG_SHEET_FORGE_ROOT");
            summary.SourceCliProjectRelativePath = FirstNonEmpty(
                GetString(root, "sourceCliProjectRelativePath", "cliProjectRelativePath"),
                FindStringDeep(root, "sourceCliProjectRelativePath", "cliProjectRelativePath"),
                Path.Combine("src", "cli", "ConfigSheetForge.Cli"));
            summary.ContractArguments.AddRange(GetStringArray(root, "contractArgs", "adapterArgs", "lifecycleContractArgs"));
            if (summary.ContractArguments.Count == 0)
            {
                summary.ContractArguments.AddRange(FindStringArrayDeep(root, "contractArgs", "adapterArgs", "lifecycleContractArgs"));
            }

            ReadRegistry(summary, root);
        }

        private static void ReadRegistry(ProjectConfigSummary summary, SimpleJsonValue root)
        {
            var registry = GetObject(root, "registry");
            summary.RegistryBaseToken = FirstNonEmpty(GetString(registry, "baseToken", "token"), GetString(root, "baseToken", "registryBaseToken"));
            summary.RegistryBaseUrl = FirstNonEmpty(GetString(registry, "baseUrl", "url"), GetString(root, "baseUrl", "registryBaseUrl"));

            var feishu = GetObject(root, "feishu", "lark");
            var registryBase = GetObject(feishu, "registryBase", "baseRegistry");
            summary.RegistryBaseToken = FirstNonEmpty(summary.RegistryBaseToken, GetString(registryBase, "baseToken", "token"));
            summary.RegistryBaseUrl = FirstNonEmpty(summary.RegistryBaseUrl, GetString(registryBase, "baseUrl", "url"));
        }

        private static void ApplyBranchContext(ProjectConfigSummary summary, string currentGitBranch, SimpleJsonValue root)
        {
            var git = GetObject(root, "git");
            var branchWorkspace = GetObject(root, "branchWorkspace", "workspace");
            var branchWorkspaceResult = GetObject(root, "branchWorkspace");

            summary.ProfileNameTemplate = FirstNonEmpty(GetString(branchWorkspace, "profileNameTemplate"), FindStringDeep(root, "profileNameTemplate"), "{gitBranch}");
            summary.BranchNodeTitleTemplate = FirstNonEmpty(GetString(branchWorkspace, "branchNodeTitleTemplate"), FindStringDeep(root, "branchNodeTitleTemplate"), "branch-{slug}");
            summary.MainGitBranch = FirstNonEmpty(GetString(branchWorkspace, "mainGitBranch"), FindStringDeep(root, "mainGitBranch"), "main");
            summary.MainFeishuBranch = FirstNonEmpty(GetString(branchWorkspace, "mainFeishuBranch"), FindStringDeep(root, "mainFeishuBranch"), "main");
            summary.BranchWorkspaceRootWikiUrl = FirstNonEmpty(
                GetString(branchWorkspace, "rootWikiUrl"),
                GetString(root, "branchWorkspaceRootWikiUrl", "rootWikiUrl", "wikiRootUrl"),
                FindStringDeep(root, "branchWorkspaceRootWikiUrl", "rootWikiUrl", "wikiRootUrl"));
            summary.BranchWorkspaceRootWikiToken = FirstNonEmpty(
                GetString(branchWorkspace, "rootWikiToken"),
                GetString(root, "branchWorkspaceRootWikiToken", "wikiRootToken", "feishuRootToken", "rootToken"),
                FindStringDeep(root, "branchWorkspaceRootWikiToken", "wikiRootToken", "feishuRootToken"));

            var configuredBranch = FirstNonEmpty(
                currentGitBranch,
                GetString(root, "gitBranch", "currentGitBranch"),
                GetString(git, "branch", "gitBranch"),
                GetString(branchWorkspace, "gitBranch"),
                GetString(root, "branch"));
            var configuredFeishuBranch = FirstNonEmpty(
                GetString(root, "feishuBranch", "larkBranch"),
                GetString(git, "feishuBranch", "larkBranch"),
                GetString(branchWorkspace, "feishuBranch"));
            var configuredProfile = FirstNonEmpty(
                GetString(root, "profile", "feishuProfile", "larkProfile"),
                GetString(git, "profile", "feishuProfile", "larkProfile"),
                GetString(branchWorkspace, "profile"));

            summary.BranchWikiNodeTitle = FirstNonEmpty(
                GetString(root, "branchWikiNodeTitle", "wikiNodeTitle", "nodeTitle"),
                GetString(branchWorkspaceResult, "nodeTitle", "wikiNodeTitle"));
            summary.BranchWikiNodeUrl = FirstNonEmpty(
                GetString(root, "branchWikiNodeUrl", "wikiNodeUrl"),
                GetString(branchWorkspaceResult, "wikiNodeUrl", "url"),
                GetString(branchWorkspace, "existingWikiNodeUrl"));
            summary.BranchWikiNodeToken = FirstNonEmpty(
                GetString(root, "branchWikiNodeToken", "wikiNodeToken"),
                GetString(branchWorkspaceResult, "wikiNodeToken"),
                GetString(branchWorkspace, "existingWikiNodeToken"));

            if (string.IsNullOrWhiteSpace(configuredBranch))
            {
                summary.GitBranch = "";
                summary.FeishuBranch = configuredFeishuBranch;
                summary.Profile = configuredProfile;
                return;
            }

            var request = new LifecycleContractRequest();
            request.Git.Branch = configuredBranch;
            request.Git.FeishuBranch = configuredFeishuBranch;
            request.Git.Profile = configuredProfile;
            request.BranchWorkspace.GitBranch = configuredBranch;
            request.BranchWorkspace.FeishuBranch = configuredFeishuBranch;
            request.BranchWorkspace.Profile = configuredProfile;
            request.BranchWorkspace.ProfileNameTemplate = summary.ProfileNameTemplate;
            request.BranchWorkspace.BranchNodeTitleTemplate = summary.BranchNodeTitleTemplate;
            request.BranchWorkspace.MainGitBranch = summary.MainGitBranch;
            request.BranchWorkspace.MainFeishuBranch = summary.MainFeishuBranch;
            request.BranchWorkspace.RootWikiUrl = summary.BranchWorkspaceRootWikiUrl;
            request.BranchWorkspace.RootWikiToken = summary.BranchWorkspaceRootWikiToken;
            request.BranchWorkspace.ExistingWikiNodeToken = summary.BranchWikiNodeToken;
            request.BranchWorkspace.ExistingWikiNodeUrl = summary.BranchWikiNodeUrl;
            request.BranchBindings.AddRange(summary.BranchBindings);

            var resolution = BranchWorkspaceResolver.Resolve(request);
            summary.GitBranch = resolution.GitBranch;
            summary.FeishuBranch = resolution.FeishuBranch;
            summary.Profile = resolution.Profile;
            summary.BranchWikiNodeTitle = FirstNonEmpty(summary.BranchWikiNodeTitle, resolution.NodeTitle);
            summary.BranchWikiNodeToken = FirstNonEmpty(summary.BranchWikiNodeToken, resolution.WikiNodeToken);
            summary.BranchWikiNodeUrl = FirstNonEmpty(summary.BranchWikiNodeUrl, resolution.WikiNodeUrl);
            if (string.IsNullOrWhiteSpace(summary.BranchWikiNodeUrl) && !string.IsNullOrWhiteSpace(summary.BranchWikiNodeToken))
            {
                summary.BranchWikiNodeUrl = BuildWikiUrl(summary.BranchWorkspaceRootWikiUrl, summary.BranchWikiNodeToken);
            }
        }

        private static void ApplyCurrentBranchTables(ProjectConfigSummary summary, List<ProjectConfigTableSummary> liveTables)
        {
            var profile = summary.BranchProfile;
            var matchedLiveTables = FilterTablesForProfile(liveTables, profile);
            if (matchedLiveTables.Count > 0)
            {
                summary.CurrentBranchTables.AddRange(matchedLiveTables);
                summary.CurrentBranchTableSource = "live-registry";
            }
            else
            {
                summary.CurrentBranchTables.AddRange(FilterTablesForProfile(summary.Tables, profile));
                summary.CurrentBranchTableSource = summary.CurrentBranchTables.Count > 0 ? "project-config" : "";
                if (liveTables.Count > 0 && summary.CurrentBranchTables.Count == 0)
                {
                    summary.Diagnostics.Add("ConfigSheets 中没有匹配当前 branch/profile 的配表记录。");
                }
            }

            summary.CurrentBranchTableCount = summary.CurrentBranchTables.Count;
            foreach (var table in summary.CurrentBranchTables)
            {
                if (string.IsNullOrWhiteSpace(table.BlockingReason))
                {
                    table.BlockingReason = BuildTableBlockingReason(summary, table);
                }
            }
        }

        private static List<ProjectConfigTableSummary> FilterTablesForProfile(IEnumerable<ProjectConfigTableSummary> tables, string profile)
        {
            var result = new List<ProjectConfigTableSummary>();
            foreach (var table in tables)
            {
                var tableProfile = table.EffectiveProfile;
                if (string.IsNullOrWhiteSpace(tableProfile) ||
                    string.IsNullOrWhiteSpace(profile) ||
                    string.Equals(tableProfile, profile, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(table);
                }
            }

            return result;
        }

        private static string BuildTableBlockingReason(ProjectConfigSummary summary, ProjectConfigTableSummary table)
        {
            if (string.IsNullOrWhiteSpace(summary.BranchWikiNodeToken) && summary.BranchBindings.Count == 0)
            {
                return "未读取到当前分支的 BranchBindings。请先刷新在线状态，或确认 Base 注册中心权限。";
            }

            if (string.IsNullOrWhiteSpace(table.TableId))
            {
                return "ConfigSheets 记录缺少 TableId。";
            }

            if (string.IsNullOrWhiteSpace(table.SpreadsheetToken) && string.IsNullOrWhiteSpace(table.OnlineSheetUrl))
            {
                return "缺少在线 Sheet token 或链接。";
            }

            if (string.IsNullOrWhiteSpace(table.SheetId))
            {
                return "缺少工作表 ID。";
            }

            return "";
        }

        private static IEnumerable<ProjectConfigTableSummary> ParseRootTableArray(SimpleJsonValue root, string source)
        {
            var array = FindFirstRootArray(root, TableArrayNames);
            if (array == null)
            {
                yield break;
            }

            foreach (var item in array.ArrayValue)
            {
                var table = ParseTable(item, source);
                if (!string.IsNullOrWhiteSpace(table.TableId) || !string.IsNullOrWhiteSpace(table.DisplayName))
                {
                    yield return table;
                }
            }
        }

        private static List<ProjectConfigTableSummary> ParseConfigSheetArrays(SimpleJsonValue root)
        {
            var result = new List<ProjectConfigTableSummary>();
            foreach (var item in FindArraysDeep(root, "configSheets", "seedTables"))
            {
                foreach (var child in item.ArrayValue)
                {
                    var table = ParseTable(child, "ConfigSheets");
                    if (!string.IsNullOrWhiteSpace(table.TableId) || !string.IsNullOrWhiteSpace(table.DisplayName))
                    {
                        result.Add(table);
                    }
                }
            }

            return result;
        }

        private static ProjectConfigTableSummary ParseTable(SimpleJsonValue item, string source)
        {
            var table = new ProjectConfigTableSummary { Source = source };
            if (item == null)
            {
                return table;
            }

            if (item.Kind == SimpleJsonKind.String)
            {
                table.TableId = item.StringValue;
                table.DisplayName = item.StringValue;
                return table;
            }

            if (item.Kind != SimpleJsonKind.Object)
            {
                return table;
            }

            var feishu = GetObject(item, "feishu", "lark", "online");
            table.TableId = FirstNonEmpty(GetString(item, "tableId", "id", "key", "machineKey"), GetString(feishu, "tableId", "id"));
            table.DisplayName = FirstNonEmpty(GetString(item, "displayName", "name", "title"), GetString(feishu, "displayName", "name"), table.TableId);
            table.OnlineSheetUrl = FirstNonEmpty(GetString(item, "onlineSheetUrl", "spreadsheetUrl", "url"), GetString(feishu, "onlineSheetUrl", "spreadsheetUrl", "url"));
            table.SpreadsheetToken = FirstNonEmpty(GetString(item, "spreadsheetToken", "spreadsheet", "token"), GetString(feishu, "spreadsheetToken", "spreadsheet", "token"));
            if (IsUrl(table.SpreadsheetToken) && string.IsNullOrWhiteSpace(table.OnlineSheetUrl))
            {
                table.OnlineSheetUrl = table.SpreadsheetToken;
            }

            table.SheetId = FirstNonEmpty(GetString(item, "sheetId", "worksheetId"), GetString(feishu, "sheetId", "worksheetId"));
            table.Branch = FirstNonEmpty(GetString(item, "branch", "feishuBranch", "larkBranch"), GetString(feishu, "branch", "feishuBranch", "larkBranch"));
            table.Profile = FirstNonEmpty(GetString(item, "profile", "feishuProfile", "larkProfile"), GetString(feishu, "profile", "feishuProfile", "larkProfile"));
            table.WikiNodeToken = FirstNonEmpty(GetString(item, "wikiNodeToken"), GetString(feishu, "wikiNodeToken"));
            table.WikiNodeUrl = FirstNonEmpty(GetString(item, "wikiNodeUrl"), GetString(feishu, "wikiNodeUrl"));
            table.SemanticHash = FirstNonEmpty(GetString(item, "semanticHash", "hash"), GetString(feishu, "semanticHash", "hash"));
            table.UpdatedAt = FirstNonEmpty(GetString(item, "updatedAt", "lastUpdatedAt", "syncedAt"), GetString(feishu, "updatedAt", "lastUpdatedAt", "syncedAt"));
            table.OwnerRole = FirstNonEmpty(GetString(item, "ownerRole"), GetString(feishu, "ownerRole"));
            table.Status = FirstNonEmpty(GetString(item, "status"), GetString(feishu, "status"));
            table.SemanticCachePath = GetString(item, "semanticCachePath", "semanticPath");
            table.HashCachePath = GetString(item, "hashCachePath", "sha256Path");
            table.CacheXlsxPath = GetString(item, "cacheXlsxPath", "excelPath");
            table.SchemaStatus = ReadSchemaStatus(item);
            return table;
        }

        private static string ReadSchemaStatus(SimpleJsonValue item)
        {
            var schemaChanged = GetBoolean(item, "schemaChangeDetected", "schemaChanged");
            if (schemaChanged.HasValue)
            {
                return schemaChanged.Value ? "有变化，需审查" : "未检测到变化";
            }

            var required = GetBoolean(item, "schemaReviewRequired", "requiresSchemaReview");
            if (required.HasValue)
            {
                return required.Value ? "需要审查" : "无需审查";
            }

            return FirstNonEmpty(GetString(item, "schemaStatus", "schemaReviewStatus"), "未知");
        }

        private static IEnumerable<BranchBindingContract> ParseBranchBindings(SimpleJsonValue root)
        {
            var array = FindFirstRootArray(root, new[] { "branchBindings", "bindings" });
            if (array == null)
            {
                yield break;
            }

            foreach (var item in array.ArrayValue)
            {
                if (item.Kind != SimpleJsonKind.Object)
                {
                    continue;
                }

                var binding = new BranchBindingContract
                {
                    RecordId = GetString(item, "recordId", "record_id"),
                    GitBranch = GetString(item, "gitBranch", "branch"),
                    FeishuBranch = GetString(item, "feishuBranch", "larkBranch"),
                    Profile = GetString(item, "profile", "feishuProfile", "larkProfile"),
                    Slug = GetString(item, "slug"),
                    NodeTitle = GetString(item, "nodeTitle", "wikiNodeTitle"),
                    WikiNodeToken = GetString(item, "wikiNodeToken"),
                    WikiNodeUrl = GetString(item, "wikiNodeUrl", "url"),
                    Status = GetString(item, "status"),
                    OwnerRole = GetString(item, "ownerRole"),
                    CreatedBy = GetString(item, "createdBy"),
                    CreatedAt = GetString(item, "createdAt"),
                    UpdatedAt = GetString(item, "updatedAt")
                };

                if (!string.IsNullOrWhiteSpace(binding.GitBranch) ||
                    !string.IsNullOrWhiteSpace(binding.Profile) ||
                    !string.IsNullOrWhiteSpace(binding.FeishuBranch))
                {
                    yield return binding;
                }
            }
        }

        private static SimpleJsonValue FindFirstRootArray(SimpleJsonValue root, IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                var value = GetValue(root, name);
                if (value != null && value.Kind == SimpleJsonKind.Array)
                {
                    return value;
                }
            }

            return null;
        }

        private static IEnumerable<SimpleJsonValue> FindArraysDeep(SimpleJsonValue root, params string[] names)
        {
            if (root == null)
            {
                yield break;
            }

            if (root.Kind == SimpleJsonKind.Object)
            {
                foreach (var pair in root.ObjectValue)
                {
                    if (NameMatches(pair.Key, names) && pair.Value.Kind == SimpleJsonKind.Array)
                    {
                        yield return pair.Value;
                    }

                    foreach (var nested in FindArraysDeep(pair.Value, names))
                    {
                        yield return nested;
                    }
                }
            }
            else if (root.Kind == SimpleJsonKind.Array)
            {
                foreach (var item in root.ArrayValue)
                {
                    foreach (var nested in FindArraysDeep(item, names))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static string FindStringDeep(SimpleJsonValue root, params string[] names)
        {
            if (root == null)
            {
                return "";
            }

            if (root.Kind == SimpleJsonKind.Object)
            {
                foreach (var pair in root.ObjectValue)
                {
                    if (NameMatches(pair.Key, names) && pair.Value.Kind == SimpleJsonKind.String)
                    {
                        return pair.Value.StringValue;
                    }

                    var nested = FindStringDeep(pair.Value, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }
            else if (root.Kind == SimpleJsonKind.Array)
            {
                foreach (var item in root.ArrayValue)
                {
                    var nested = FindStringDeep(item, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }

            return "";
        }

        private static List<string> FindStringArrayDeep(SimpleJsonValue root, params string[] names)
        {
            if (root == null)
            {
                return new List<string>();
            }

            if (root.Kind == SimpleJsonKind.Object)
            {
                foreach (var pair in root.ObjectValue)
                {
                    if (NameMatches(pair.Key, names) && pair.Value.Kind == SimpleJsonKind.Array)
                    {
                        return ToStringList(pair.Value);
                    }

                    var nested = FindStringArrayDeep(pair.Value, names);
                    if (nested.Count > 0)
                    {
                        return nested;
                    }
                }
            }
            else if (root.Kind == SimpleJsonKind.Array)
            {
                foreach (var item in root.ArrayValue)
                {
                    var nested = FindStringArrayDeep(item, names);
                    if (nested.Count > 0)
                    {
                        return nested;
                    }
                }
            }

            return new List<string>();
        }

        private static SimpleJsonValue GetObject(SimpleJsonValue parent, params string[] names)
        {
            var value = GetValue(parent, names);
            return value != null && value.Kind == SimpleJsonKind.Object ? value : SimpleJsonValue.EmptyObject;
        }

        private static SimpleJsonValue GetValue(SimpleJsonValue parent, params string[] names)
        {
            if (parent == null || parent.Kind != SimpleJsonKind.Object)
            {
                return null;
            }

            foreach (var name in names)
            {
                SimpleJsonValue value;
                if (parent.ObjectValue.TryGetValue(name, out value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string GetString(SimpleJsonValue parent, params string[] names)
        {
            var value = GetValue(parent, names);
            if (value == null)
            {
                return "";
            }

            if (value.Kind == SimpleJsonKind.String || value.Kind == SimpleJsonKind.Number)
            {
                return value.StringValue;
            }

            if (value.Kind == SimpleJsonKind.Boolean)
            {
                return value.BooleanValue ? "true" : "false";
            }

            return "";
        }

        private static List<string> GetStringArray(SimpleJsonValue parent, params string[] names)
        {
            var value = GetValue(parent, names);
            return value != null && value.Kind == SimpleJsonKind.Array ? ToStringList(value) : new List<string>();
        }

        private static List<string> ToStringList(SimpleJsonValue value)
        {
            var result = new List<string>();
            if (value == null || value.Kind != SimpleJsonKind.Array)
            {
                return result;
            }

            foreach (var item in value.ArrayValue)
            {
                if (item.Kind == SimpleJsonKind.String || item.Kind == SimpleJsonKind.Number)
                {
                    result.Add(item.StringValue);
                }
                else if (item.Kind == SimpleJsonKind.Boolean)
                {
                    result.Add(item.BooleanValue ? "true" : "false");
                }
            }

            return result;
        }

        private static bool? GetBoolean(SimpleJsonValue parent, params string[] names)
        {
            var value = GetValue(parent, names);
            if (value == null)
            {
                return null;
            }

            if (value.Kind == SimpleJsonKind.Boolean)
            {
                return value.BooleanValue;
            }

            if (value.Kind == SimpleJsonKind.String)
            {
                if (string.Equals(value.StringValue, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value.StringValue, "yes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value.StringValue, "是", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(value.StringValue, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value.StringValue, "no", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value.StringValue, "否", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return null;
        }

        private static string BuildWikiUrl(string rootWikiUrl, string wikiNodeToken)
        {
            if (string.IsNullOrWhiteSpace(wikiNodeToken))
            {
                return "";
            }

            if (IsUrl(wikiNodeToken))
            {
                return wikiNodeToken;
            }

            Uri root;
            if (!Uri.TryCreate(rootWikiUrl, UriKind.Absolute, out root))
            {
                return "";
            }

            var port = root.IsDefaultPort ? "" : ":" + root.Port.ToString(CultureInfo.InvariantCulture);
            return root.Scheme + "://" + root.Host + port + "/wiki/" + wikiNodeToken;
        }

        private static bool IsUrl(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static bool NameMatches(string value, IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return ProjectConfigSummary.FirstNonEmpty(values);
        }
    }

    internal enum SimpleJsonKind
    {
        Null,
        Object,
        Array,
        String,
        Number,
        Boolean
    }

    internal sealed class SimpleJsonValue
    {
        public static readonly SimpleJsonValue EmptyObject = new SimpleJsonValue
        {
            Kind = SimpleJsonKind.Object,
            ObjectValue = new Dictionary<string, SimpleJsonValue>(StringComparer.OrdinalIgnoreCase)
        };

        public SimpleJsonKind Kind { get; set; }
        public string StringValue { get; set; } = "";
        public bool BooleanValue { get; set; }
        public Dictionary<string, SimpleJsonValue> ObjectValue { get; set; } = new Dictionary<string, SimpleJsonValue>(StringComparer.OrdinalIgnoreCase);
        public List<SimpleJsonValue> ArrayValue { get; set; } = new List<SimpleJsonValue>();
    }

    internal sealed class SimpleJsonParser
    {
        private readonly string _json;
        private int _index;

        private SimpleJsonParser(string json)
        {
            _json = json ?? "";
        }

        public static SimpleJsonValue Parse(string json)
        {
            var parser = new SimpleJsonParser(json);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (parser._index != parser._json.Length)
            {
                throw new InvalidOperationException("JSON 末尾存在无法识别的内容。");
            }

            return value;
        }

        private SimpleJsonValue ParseValue()
        {
            SkipWhitespace();
            if (_index >= _json.Length)
            {
                throw new InvalidOperationException("JSON 内容为空。");
            }

            var c = _json[_index];
            if (c == '{')
            {
                return ParseObject();
            }

            if (c == '[')
            {
                return ParseArray();
            }

            if (c == '"')
            {
                return new SimpleJsonValue { Kind = SimpleJsonKind.String, StringValue = ParseString() };
            }

            if (c == 't' || c == 'f')
            {
                return ParseBoolean();
            }

            if (c == 'n')
            {
                Expect("null");
                return new SimpleJsonValue { Kind = SimpleJsonKind.Null };
            }

            return ParseNumber();
        }

        private SimpleJsonValue ParseObject()
        {
            Expect('{');
            var result = new SimpleJsonValue { Kind = SimpleJsonKind.Object };
            SkipWhitespace();
            if (TryConsume('}'))
            {
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (_index >= _json.Length || _json[_index] != '"')
                {
                    throw new InvalidOperationException("JSON object 需要字符串 key。");
                }

                var key = ParseString();
                SkipWhitespace();
                Expect(':');
                result.ObjectValue[key] = ParseValue();
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                Expect(',');
            }
        }

        private SimpleJsonValue ParseArray()
        {
            Expect('[');
            var result = new SimpleJsonValue { Kind = SimpleJsonKind.Array };
            SkipWhitespace();
            if (TryConsume(']'))
            {
                return result;
            }

            while (true)
            {
                result.ArrayValue.Add(ParseValue());
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return result;
                }

                Expect(',');
            }
        }

        private string ParseString()
        {
            Expect('"');
            var builder = new System.Text.StringBuilder();
            while (_index < _json.Length)
            {
                var c = _json[_index++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (_index >= _json.Length)
                {
                    throw new InvalidOperationException("JSON 字符串转义不完整。");
                }

                var escaped = _json[_index++];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        builder.Append(ParseUnicodeEscape());
                        break;
                    default:
                        builder.Append(escaped);
                        break;
                }
            }

            throw new InvalidOperationException("JSON 字符串没有结束。");
        }

        private char ParseUnicodeEscape()
        {
            if (_index + 4 > _json.Length)
            {
                throw new InvalidOperationException("JSON unicode 转义不完整。");
            }

            var hex = _json.Substring(_index, 4);
            _index += 4;
            int value;
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException("JSON unicode 转义非法。");
            }

            return (char)value;
        }

        private SimpleJsonValue ParseBoolean()
        {
            if (_json.Substring(_index).StartsWith("true", StringComparison.Ordinal))
            {
                _index += 4;
                return new SimpleJsonValue { Kind = SimpleJsonKind.Boolean, BooleanValue = true };
            }

            if (_json.Substring(_index).StartsWith("false", StringComparison.Ordinal))
            {
                _index += 5;
                return new SimpleJsonValue { Kind = SimpleJsonKind.Boolean, BooleanValue = false };
            }

            throw new InvalidOperationException("JSON boolean 非法。");
        }

        private SimpleJsonValue ParseNumber()
        {
            var start = _index;
            while (_index < _json.Length)
            {
                var c = _json[_index];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                {
                    _index++;
                    continue;
                }

                break;
            }

            if (start == _index)
            {
                throw new InvalidOperationException("JSON value 非法。");
            }

            return new SimpleJsonValue
            {
                Kind = SimpleJsonKind.Number,
                StringValue = _json.Substring(start, _index - start)
            };
        }

        private void SkipWhitespace()
        {
            while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
            {
                _index++;
            }
        }

        private bool TryConsume(char c)
        {
            if (_index < _json.Length && _json[_index] == c)
            {
                _index++;
                return true;
            }

            return false;
        }

        private void Expect(char c)
        {
            if (_index >= _json.Length || _json[_index] != c)
            {
                throw new InvalidOperationException("JSON 期望字符 “" + c + "”。");
            }

            _index++;
        }

        private void Expect(string literal)
        {
            if (!_json.Substring(_index).StartsWith(literal, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("JSON 期望 literal “" + literal + "”。");
            }

            _index += literal.Length;
        }
    }
}
