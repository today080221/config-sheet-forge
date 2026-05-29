using System.Security.Cryptography;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using ConfigSheetForge.Core;
using ConfigSheetForge.Providers.Lark;

namespace ConfigSheetForge.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--version" or "-v" or "version")
        {
            Console.WriteLine(GetCliVersion());
            return 0;
        }

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var parsed = ParsedArgs.Parse(args.Skip(1));

        try
        {
            return command switch
            {
                "init" => await InitAsync(parsed),
                "doctor" => await DoctorAsync(parsed),
                "discover-root" => await DiscoverRootAsync(parsed),
                "new-table" => await NewTableAsync(parsed),
                "sync" => await SyncAsync(parsed),
                "sync-cache" => await SyncCacheLifecycleAsync(parsed),
                "repair-cache-dialect" => await RepairCacheDialectAsync(parsed),
                "registry-status" => await RegistryStatusAsync(parsed, "registry-status"),
                "branch-status" => await RegistryStatusAsync(parsed, "branch-status"),
                "sync-status" => await RegistryStatusAsync(parsed, "sync-status"),
                "bootstrap-current-branch-from-target" => await CurrentBranchBootstrapFromTargetAsync(parsed, "bootstrap-current-branch-from-target"),
                "branch-workspace-bootstrap-from-target" => await CurrentBranchBootstrapFromTargetAsync(parsed, "branch-workspace-bootstrap-from-target"),
                "seed-from-xlsx" => await SeedFromXlsxAsync(parsed),
                "bootstrap-target-branch-from-local-xlsx" => await SeedFromXlsxAsync(parsed, "bootstrap-target-branch-from-local-xlsx"),
                "merge" => await MergeAsync(parsed),
                "gate" => await GateAsync(parsed),
                "apply-contract" => await ApplyContractAsync(parsed),
                "submit-merge-review" => await ApplyContractAsync(parsed.WithDefault("operation", "submit-merge-review")),
                "approve-merge-review" => await ApplyContractAsync(parsed.WithDefault("operation", "approve-merge-review")),
                "registry-migrate" => await RegistryMigrateAsync(parsed),
                _ => UnknownCommand(command)
            };
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            if (!string.IsNullOrWhiteSpace(ex.Detail) && parsed.HasFlag("details"))
            {
                Console.Error.WriteLine("Details: " + ex.Detail);
            }

            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: Something unexpected happened while running this command.");
            if (parsed.HasFlag("details"))
            {
                Console.Error.WriteLine("Details: " + ex);
            }

            return 1;
        }
    }

    private static string GetCliVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0-dev";
    }

    private static async Task<int> InitAsync(ParsedArgs args)
    {
        var root = Directory.GetCurrentDirectory();
        var paths = WorkspacePaths.For(root);
        var force = args.HasFlag("force");

        if (File.Exists(paths.ConfigPath) && !force)
        {
            throw new CliException("A local config already exists. Use --force if you want to rewrite the template.", 2, paths.ConfigPath);
        }

        Directory.CreateDirectory(paths.StateDirectory);
        Directory.CreateDirectory(paths.CacheDirectory);

        var config = new ForgeConfig
        {
            Provider = args.Get("provider", "lark"),
            RootUrl = args.Get("root", ""),
            RootObjectType = args.Get("root-type", ""),
            CacheDirectory = ".config-sheet-forge/cache",
            RegistryPath = ".config-sheet-forge/registry.json"
        };
        config.ProviderSettings["larkCliPath"] = args.Get("lark-cli", "lark-cli");
        config.ProviderSettings["larkCliIdentity"] = args.Get("lark-identity", "bot");
        config.ProviderSettings["larkAllowUserFallback"] = args.HasFlag("allow-user-fallback") ? "true" : "false";

        var registry = new TableRegistry();
        await WriteJsonAsync(paths.ConfigPath, config);
        await WriteJsonAsync(paths.RegistryPath, registry);

        Console.WriteLine("Created local Config Sheet Forge config.");
        Console.WriteLine("Config: " + paths.ConfigPath);
        Console.WriteLine("Registry: " + paths.RegistryPath);
        Console.WriteLine("Next: run config-sheet-forge doctor, then config-sheet-forge discover-root --query <name>.");
        return 0;
    }

    private static async Task<int> DoctorAsync(ParsedArgs args)
    {
        await EmitProgressEventAsync(args, "doctor", "doctor", "", 0, 0, "正在检查飞书 CLI 和本机环境。", "info");
        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        var hasError = false;

        PrintCheck(File.Exists(workspace.Paths.ConfigPath), "Local config file exists.", "Run config-sheet-forge init in the project root.", ref hasError);
        PrintCheck(File.Exists(workspace.Paths.RegistryPath), "Table registry file exists.", "Create or restore .config-sheet-forge/registry.json.", ref hasError);
        PrintCheck(!string.IsNullOrWhiteSpace(workspace.Config.Provider), "Provider is configured.", "Set provider in .config-sheet-forge/config.json.", ref hasError);
        PrintCheck(!string.IsNullOrWhiteSpace(workspace.Config.RootUrl) || !string.IsNullOrWhiteSpace(workspace.Config.RootToken), "A root is configured.", "Run discover-root and copy the confirmed root into local config.", ref hasError, warningOnly: true);

        var provider = CreateProvider(workspace.Config.Provider);
        var findings = await provider.DoctorAsync(CreateProviderContext(workspace), CancellationToken.None);
        foreach (var finding in findings)
        {
            PrintFinding(finding, args.HasFlag("details"));
            if (finding.Severity == FindingSeverity.Error)
            {
                hasError = true;
            }
        }

        return hasError ? 1 : 0;
    }

    private static async Task<int> DiscoverRootAsync(ParsedArgs args)
    {
        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        var provider = CreateProvider(workspace.Config.Provider);
        var query = args.Get("query", args.Positionals.FirstOrDefault() ?? "");
        var candidates = await provider.DiscoverRootsAsync(CreateProviderContext(workspace, args), query, CancellationToken.None);

        if (candidates.Count == 0)
        {
            Console.WriteLine("No candidates found. Try a more specific --query, or set rootUrl manually in local config.");
            return 0;
        }

        Console.WriteLine("Candidate roots. Review manually; this command never selects one for you.");
        foreach (var candidate in candidates)
        {
            Console.WriteLine("- " + FirstNonEmpty(candidate.Title, candidate.Url, candidate.TokenOrId, "Untitled"));
            Console.WriteLine("  type: " + FirstNonEmpty(candidate.ObjectType, "unknown"));
            Console.WriteLine("  url: " + FirstNonEmpty(candidate.Url, "(none)"));
            Console.WriteLine("  token/id: " + Mask(candidate.TokenOrId));
            Console.WriteLine("  why: " + candidate.Reason);
        }

        return 0;
    }

    private static async Task<int> NewTableAsync(ParsedArgs args)
    {
        var workspace = await LoadWorkspaceAsync(requireConfig: true);
        var id = args.Get("id", "");
        var name = args.Get("name", "");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            throw new CliException("new-table needs --id and --name.", 2);
        }

        if (args.HasFlag("dry-run"))
        {
            var request = args.TryGet("manifest", out var manifestPath)
                ? await ReadSeedManifestAsync(workspace, manifestPath, args)
                : NewSeedRequestFromWorkspace(workspace, args);
            request.Operation = "new-table";
            request.DryRun = true;
            request.Table.TableId = id;
            request.Table.DisplayName = name;
            request.Table.OwnerRole = args.Get("owner-role", request.Table.OwnerRole);
            request.Table.Fields = ParseNewTableFieldsJson(args.Get("fields-json", ""));
            if (request.Table.Fields.Count == 0)
            {
                request.Table.Fields.Add(new ContractFieldSpec { Key = "id", DisplayName = "ID", ValueKind = "string", Description = "唯一 ID" });
                request.Table.Fields.Add(new ContractFieldSpec { Key = "name", DisplayName = "名称", ValueKind = "string", Description = "显示名称" });
            }

            var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
            await EmitLifecycleResultAsync(args, result);
            return result.Success ? 0 : 1;
        }

        var existing = workspace.Registry.Tables.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new TableConfig { Id = id };
            workspace.Registry.Tables.Add(existing);
        }

        existing.Name = name;
        existing.Provider = args.Get("provider", workspace.Config.Provider);
        existing.Spreadsheet = args.Get("spreadsheet", existing.Spreadsheet);
        existing.SheetId = args.Get("sheet-id", existing.SheetId);
        existing.Range = args.Get("range", existing.Range);
        existing.LocalSourcePath = args.Get("local-source", existing.LocalSourcePath);
        existing.FieldRow = args.GetInt("field-row", existing.FieldRow);
        existing.TypeRow = args.GetInt("type-row", existing.TypeRow);
        existing.DescriptionRow = args.GetInt("description-row", existing.DescriptionRow);
        existing.DataStartRow = args.GetInt("data-start-row", existing.DataStartRow);
        existing.TreatUnknownTypesAsEnum = args.GetBool("treat-unknown-types-as-enum", existing.TreatUnknownTypesAsEnum);

        await WriteJsonAsync(workspace.Paths.RegistryPath, workspace.Registry);
        Console.WriteLine("Registered table " + id + ".");
        return 0;
    }

    private static List<ContractFieldSpec> ParseNewTableFieldsJson(string json)
    {
        var fields = new List<ContractFieldSpec>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return fields;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return fields;
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = GetJsonString(item, "key", "id");
            var displayName = GetJsonString(item, "displayName", "name", "title");
            if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            fields.Add(new ContractFieldSpec
            {
                Key = key,
                DisplayName = displayName,
                ValueKind = GetJsonString(item, "type", "valueKind", "excelToSoType"),
                ExcelToSoType = GetJsonString(item, "type", "excelToSoType"),
                OriginalType = GetJsonString(item, "type", "originalType"),
                Description = GetJsonString(item, "description", "comment")
            });
        }

        return fields;
    }

    private static async Task<int> SyncAsync(ParsedArgs args)
    {
        var workspace = await LoadWorkspaceAsync(requireConfig: true);
        Directory.CreateDirectory(workspace.Paths.CacheDirectory);

        if (args.TryGet("input", out var input))
        {
            return await SyncLocalInputAsync(workspace, input, args.Get("table", "local"));
        }

        var selectedTable = args.Get("table", "");
        var tables = string.IsNullOrWhiteSpace(selectedTable)
            ? workspace.Registry.Tables
            : workspace.Registry.Tables.Where(t => string.Equals(t.Id, selectedTable, StringComparison.OrdinalIgnoreCase)).ToList();

        if (tables.Count == 0)
        {
            throw new CliException("No matching table is registered. Run new-table first, or pass --input for a local semantic model.", 2);
        }

        var hasError = false;
        foreach (var table in tables)
        {
            var tableProvider = CreateProvider(FirstNonEmpty(table.Provider, workspace.Config.Provider));
            var tableTemp = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ConfigSheetForge", "sync-temp", Guid.NewGuid().ToString("N"), table.Id);
            Directory.CreateDirectory(tableTemp);
            var result = await tableProvider.ExportAsync(CreateProviderContext(workspace, args), new ProviderExportRequest
            {
                RootTokenOrUrl = FirstNonEmpty(table.Spreadsheet, workspace.Config.RootUrl, workspace.Config.RootToken),
                SpreadsheetTokenOrUrl = table.Spreadsheet,
                TableId = table.Id,
                SheetId = table.SheetId,
                Range = table.Range,
                CacheDirectory = tableTemp,
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            }, CancellationToken.None);

            foreach (var finding in result.Findings)
            {
                PrintFinding(finding, args.HasFlag("details"));
                if (finding.Severity == FindingSeverity.Error)
                {
                    hasError = true;
                }
            }

            var tableHasError = result.Findings.Any(f => f.Severity == FindingSeverity.Error);
            if (result.Workbook != null)
            {
                var xlsxPath = FindExportedXlsx(tableTemp, table.Id);
                if (string.IsNullOrWhiteSpace(xlsxPath))
                {
                    var finding = new ProviderDoctorFinding
                    {
                        Severity = FindingSeverity.Error,
                        Code = "sync.triangulation_xlsx_missing",
                        Message = table.Id + " 缺少导出的 xlsx，无法证明在线读取、xlsx 导出、语义归一化三方一致。请确认应用有导出权限后重试。"
                    };
                    PrintFinding(finding, args.HasFlag("details"));
                    hasError = true;
                    tableHasError = true;
                }
                else
                {
                    var structureReport = XlsxWorkbookReader.InspectPortableStructures(xlsxPath, table.Id);
                    foreach (var finding in structureReport.Findings)
                    {
                        PrintValidation(finding, args.HasFlag("details"));
                        if (finding.Severity == FindingSeverity.Error)
                        {
                            hasError = true;
                            tableHasError = true;
                        }
                    }

                    var xlsxImport = XlsxWorkbookReader.Import(xlsxPath, new MatrixWorkbookImportOptions
                    {
                        ProviderId = "xlsx",
                        SourceId = xlsxPath,
                        SourceTitle = table.Id,
                        SheetId = table.SheetId,
                        SheetName = table.Id,
                        FieldRow = table.FieldRow,
                        TypeRow = table.TypeRow,
                        DescriptionRow = table.DescriptionRow,
                        DataStartRow = table.DataStartRow,
                        TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
                    });
                    foreach (var finding in xlsxImport.Report.Findings)
                    {
                        PrintValidation(finding, args.HasFlag("details"));
                        if (finding.Severity == FindingSeverity.Error)
                        {
                            hasError = true;
                            tableHasError = true;
                        }
                    }

                    var triangulation = SemanticTriangulator.Compare(result.Workbook, xlsxImport.Workbook, SemanticTriangulator.Normalize(result.Workbook));
                    if (!triangulation.Passed)
                    {
                        hasError = true;
                        tableHasError = true;
                        foreach (var diff in triangulation.DiffSummary)
                        {
                            Console.WriteLine("[error] " + diff + " (sync.triangulation_failed)");
                        }
                    }
                }

                if (!tableHasError)
                {
                    var hash = SemanticHasher.ComputeHash(result.Workbook);
                    var cacheWrite = await WriteCacheIfChangedAsync(workspace.Paths.CacheDirectory, table.Id, result.Workbook, hash, xlsxPath);
                    Console.WriteLine(table.Id + ": " + hash);
                    Console.WriteLine(cacheWrite ? "  cache updated" : "  无变化，未重写 cache");
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(result.SemanticHash))
            {
                Console.WriteLine(table.Id + ": " + result.SemanticHash);
                Console.WriteLine("  cache: not updated because sync checks did not pass");
            }
        }

        return hasError ? 1 : 0;
    }

    private static async Task<SyncCacheSummary> SyncTableConfigsAsync(Workspace workspace, ParsedArgs args, IList<TableConfig> tables, string cacheDirectory, string excelCacheDirectory, bool writeFormalCache)
    {
        var summary = new SyncCacheSummary
        {
            CacheStatus = "unknown",
            TableCount = tables.Count,
            WillWriteFiles = writeFormalCache,
            NoChangeKeepsMtime = true
        };

        if (writeFormalCache)
        {
            Directory.CreateDirectory(cacheDirectory);
            Directory.CreateDirectory(excelCacheDirectory);
        }

        var hasError = false;
        await EmitProgressEventAsync(args, "sync-cache", "sync-plan", "", 0, tables.Count, writeFormalCache ? "正在准备写入本地 cache。会先完成在线读取、临时导出和三方一致性检查。" : "正在准备完整同步预览。会读取在线表并临时导出 xlsx，不写本地 cache。", "info");
        var tableIndex = 0;
        foreach (var table in tables)
        {
            tableIndex++;
            var tableStatus = new SyncTableCacheStatus
            {
                TableId = table.Id,
                DisplayName = FirstNonEmpty(table.Name, table.Id)
            };
            summary.Tables.Add(tableStatus);
            var tableProvider = CreateProvider(FirstNonEmpty(table.Provider, workspace.Config.Provider));
            var tableTemp = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ConfigSheetForge", "sync-cache-temp", Guid.NewGuid().ToString("N"), table.Id);
            Directory.CreateDirectory(tableTemp);
            await EmitProgressEventAsync(args, "sync-cache", "online_read", table.Id, tableIndex, tables.Count, "正在读取在线表：" + table.Id, "info");
            Console.WriteLine("[stage] 正在读取在线 Sheet: " + table.Id);
            await EmitProgressEventAsync(args, "sync-cache", "export_xlsx", table.Id, tableIndex, tables.Count, "正在导出 xlsx：" + table.Id, "info");
            Console.WriteLine("[stage] 正在导出 xlsx: " + table.Id);
            var result = await tableProvider.ExportAsync(CreateProviderContext(workspace, args), new ProviderExportRequest
            {
                RootTokenOrUrl = FirstNonEmpty(table.Spreadsheet, workspace.Config.RootUrl, workspace.Config.RootToken),
                SpreadsheetTokenOrUrl = table.Spreadsheet,
                TableId = table.Id,
                SheetId = table.SheetId,
                Range = table.Range,
                CacheDirectory = tableTemp,
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            }, CancellationToken.None);

            foreach (var finding in CollapseNoisyProviderFindings(table.Id, result.Findings))
            {
                PrintFinding(finding, args.HasFlag("details"));
                summary.PortableSubsetFindings.Add(FormatProviderFindingForSummary(table.Id, finding));
                if (finding.Severity == FindingSeverity.Error)
                {
                    hasError = true;
                    tableStatus.Blockers.Add(finding.Code + ": " + finding.Message);
                }
            }

            var tableHasError = result.Findings.Any(f => f.Severity == FindingSeverity.Error);
            if (result.Workbook == null)
            {
                hasError = true;
                summary.BlockedTables.Add(table.Id);
                tableStatus.CacheStatus = "blocked";
                if (tableStatus.Blockers.Count == 0)
                {
                    tableStatus.Blockers.Add("在线表读取失败，未生成可比较的语义数据。");
                }
                continue;
            }

            Console.WriteLine("[stage] 正在三方一致性检查: " + table.Id);
            await EmitProgressEventAsync(args, "sync-cache", "triangulation_compare", table.Id, tableIndex, tables.Count, "正在三方一致性检查：" + table.Id, "info");
            var xlsxPath = FindExportedXlsx(tableTemp, table.Id);
            if (string.IsNullOrWhiteSpace(xlsxPath))
            {
                var finding = new ProviderDoctorFinding
                {
                    Severity = FindingSeverity.Error,
                    Code = "sync.triangulation_xlsx_missing",
                    Message = table.Id + " 缺少导出的 xlsx，无法证明在线读取、xlsx 导出、语义归一化三方一致。请确认应用有导出权限后重试。"
                };
                PrintFinding(finding, args.HasFlag("details"));
                hasError = true;
                tableHasError = true;
                summary.PortableSubsetFindings.Add(table.Id + ": " + finding.Code + " " + finding.Message);
                tableStatus.Blockers.Add(finding.Code + ": " + finding.Message);
            }
            else
            {
                var structureReport = XlsxWorkbookReader.InspectPortableStructures(xlsxPath, table.Id);
                foreach (var finding in structureReport.Findings)
                {
                    PrintValidation(finding, args.HasFlag("details"));
                    summary.PortableSubsetFindings.Add(table.Id + ": " + finding.Code + " " + finding.Message);
                    if (finding.Severity == FindingSeverity.Error)
                    {
                        hasError = true;
                        tableHasError = true;
                        tableStatus.Blockers.Add(finding.Code + ": " + finding.Message);
                    }
                }

                var xlsxImport = XlsxWorkbookReader.Import(xlsxPath, new MatrixWorkbookImportOptions
                {
                    ProviderId = "xlsx",
                    SourceId = xlsxPath,
                    SourceTitle = table.Id,
                    SheetId = table.SheetId,
                    SheetName = table.Id,
                    FieldRow = table.FieldRow,
                    TypeRow = table.TypeRow,
                    DescriptionRow = table.DescriptionRow,
                    DataStartRow = table.DataStartRow,
                    TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
                });
                foreach (var finding in CollapseNoisyValidationFindings(table.Id, xlsxImport.Report.Findings))
                {
                    PrintValidation(finding, args.HasFlag("details"));
                    summary.PortableSubsetFindings.Add(table.Id + ": " + finding.Code + " " + finding.Message);
                    if (finding.Severity == FindingSeverity.Error)
                    {
                        hasError = true;
                        tableHasError = true;
                        tableStatus.Blockers.Add(finding.Code + ": " + finding.Message);
                    }
                }

                var triangulation = SemanticTriangulator.Compare(result.Workbook, xlsxImport.Workbook, SemanticTriangulator.Normalize(result.Workbook));
                if (!triangulation.Passed)
                {
                    hasError = true;
                    tableHasError = true;
                    summary.TriangulationFailedCount++;
                    foreach (var diff in triangulation.DiffSummary)
                    {
                        Console.WriteLine("[error] " + diff + " (sync.triangulation_failed)");
                        tableStatus.Blockers.Add("sync.triangulation_failed: " + diff);
                    }
                }
                else
                {
                    summary.TriangulationPassedCount++;
                }

                if (!tableHasError && table.UseExcelToSoCacheDialect)
                {
                    var dialectPlan = BuildExcelToSoCacheDialectPlan(result.Workbook, table, workspace.Root);
                    foreach (var warning in dialectPlan.Warnings)
                    {
                        summary.PortableSubsetFindings.Add(table.Id + ": excel_to_so_cache_dialect " + warning);
                    }

                    foreach (var error in dialectPlan.Errors)
                    {
                        hasError = true;
                        tableHasError = true;
                        Console.WriteLine("[error] " + table.Id + ": " + error + " (excel_to_so_cache_dialect)");
                        summary.PortableSubsetFindings.Add(table.Id + ": excel_to_so_cache_dialect " + error);
                        tableStatus.Blockers.Add("excel_to_so_cache_dialect: " + error);
                    }
                }
            }

            if (!tableHasError)
            {
                Console.WriteLine("[stage] 正在 hash gate: " + table.Id);
                await EmitProgressEventAsync(args, "sync-cache", "cache_hash_gate", table.Id, tableIndex, tables.Count, "正在比较 cache hash：" + table.Id, "info");
                var hash = SemanticHasher.ComputeHash(result.Workbook);
                var cacheState = await InspectCacheStateAsync(workspace, cacheDirectory, excelCacheDirectory, table, hash, xlsxPath);
                tableStatus.OnlineSemanticHash = hash;
                tableStatus.LocalSemanticHash = cacheState.ExistingHash;
                if (cacheState.Missing)
                {
                    summary.MissingCacheTables.Add(table.Id);
                    tableStatus.CacheStatus = writeFormalCache ? "upToDate" : "missingCache";
                    tableStatus.NeedsWriteCache = !writeFormalCache;
                }
                else if (cacheState.Changed)
                {
                    summary.ChangedTables.Add(table.Id);
                    if (!writeFormalCache && cacheState.DialectOutdated && string.Equals(cacheState.ExistingHash, hash, StringComparison.Ordinal))
                    {
                        tableStatus.CacheStatus = "dialectOutdated";
                        tableStatus.NeedsWriteCache = false;
                        tableStatus.Blockers.Add("cache xlsx 类型行仍是 portable/canonical dialect，导入 Unity 前需要先修复为 ExcelToSO dialect。");
                    }
                    else
                    {
                        tableStatus.CacheStatus = writeFormalCache ? "upToDate" : "needsUpdate";
                        tableStatus.NeedsWriteCache = !writeFormalCache;
                    }
                }
                else
                {
                    summary.UpToDateTables.Add(table.Id);
                    tableStatus.CacheStatus = "upToDate";
                    tableStatus.NeedsWriteCache = false;
                }

                bool cacheWrite = false;
                if (writeFormalCache)
                {
                    cacheWrite = await WriteCacheIfChangedAsync(workspace, cacheDirectory, table, result.Workbook, hash, xlsxPath, excelCacheDirectory);
                    if (cacheWrite)
                    {
                        summary.WillWriteFilePaths.AddRange(BuildCacheFilePaths(cacheDirectory, excelCacheDirectory, table.Id));
                    }
                }

                Console.WriteLine(table.Id + ": " + hash);
                if (writeFormalCache)
                {
                    Console.WriteLine(cacheWrite ? "  cache updated" : "  无变化，未重写 cache");
                }
                else
                {
                    Console.WriteLine(cacheState.Fresh ? "  dry-run: cache 已是最新，不会写本地文件" : "  dry-run: cache 需要更新，但本次不写本地文件");
                }
            }
            else
            {
                summary.BlockedTables.Add(table.Id);
                tableStatus.CacheStatus = "blocked";
                tableStatus.NeedsWriteCache = false;
                if (tableStatus.Blockers.Count == 0)
                {
                    tableStatus.Blockers.Add("同步预检未通过，但没有拿到更细的阻断原因。请打开 Debug 查看 stdout/stderr。");
                }
            }
        }

        FinalizeSyncCacheSummary(summary, hasError, writeFormalCache);
        return summary;
    }

    private static async Task<CacheState> InspectCacheStateAsync(Workspace workspace, string cacheDirectory, string excelCacheDirectory, TableConfig table, string hash, string tempXlsxPath)
    {
        var tableId = table.Id;
        var semanticPath = Path.Combine(cacheDirectory, tableId + ".semantic.json");
        var shaPath = Path.Combine(cacheDirectory, tableId + ".sha256");
        var xlsxRoot = FirstNonEmpty(excelCacheDirectory, cacheDirectory);
        var xlsxPath = Path.Combine(xlsxRoot, MakeSafeFileName(tableId) + ".xlsx");
        var existingHash = await ReadExistingHashAsync(shaPath);
        var requiresXlsx = !string.IsNullOrWhiteSpace(tempXlsxPath);
        var missing = string.IsNullOrWhiteSpace(existingHash) || !File.Exists(semanticPath) || (requiresXlsx && !File.Exists(xlsxPath));
        var hashChanged = !missing && !string.Equals(existingHash, hash, StringComparison.Ordinal);
        var dialectOutdated = !missing && table.UseExcelToSoCacheDialect && CacheXlsxNeedsExcelToSoDialectRewrite(xlsxPath, table, workspace.Root);
        var changed = hashChanged || dialectOutdated;

        return new CacheState { Missing = missing, Changed = changed, Fresh = !missing && !changed, DialectOutdated = dialectOutdated, ExistingHash = existingHash };
    }

    private static List<string> BuildCacheFilePaths(string cacheDirectory, string excelCacheDirectory, string tableId)
    {
        var xlsxRoot = FirstNonEmpty(excelCacheDirectory, cacheDirectory);
        return new List<string>
        {
            Path.Combine(cacheDirectory, tableId + ".semantic.json"),
            Path.Combine(cacheDirectory, tableId + ".sha256"),
            Path.Combine(xlsxRoot, MakeSafeFileName(tableId) + ".xlsx")
        };
    }

    private static List<string> DistinctSorted(IEnumerable<string> values)
    {
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ComputeSyncCacheStatus(SyncCacheSummary summary, bool hasError)
    {
        if (hasError || summary.BlockedTables.Count > 0)
        {
            return "blocked";
        }

        if (summary.MissingCacheTables.Count > 0)
        {
            return "missingCache";
        }

        var hasDialectOutdated = summary.Tables.Any(t => string.Equals(t.CacheStatus, "dialectOutdated", StringComparison.OrdinalIgnoreCase));
        var hasWriteCacheChanges = summary.Tables.Any(t => string.Equals(t.CacheStatus, "needsUpdate", StringComparison.OrdinalIgnoreCase));
        if (hasDialectOutdated && !hasWriteCacheChanges)
        {
            return "dialectOutdated";
        }

        if (summary.ChangedTables.Count > 0)
        {
            return "needsUpdate";
        }

        if (summary.UpToDateTables.Count > 0 && summary.UpToDateTables.Count == summary.TableCount)
        {
            return "upToDate";
        }

        return "unknown";
    }

    private static void FinalizeSyncCacheSummary(SyncCacheSummary summary, bool hasError, bool writeFormalCache)
    {
        summary.ChangedTables = DistinctSorted(summary.ChangedTables);
        summary.MissingCacheTables = DistinctSorted(summary.MissingCacheTables);
        summary.UpToDateTables = DistinctSorted(summary.UpToDateTables);
        summary.BlockedTables = DistinctSorted(summary.BlockedTables);
        summary.WillWriteFilePaths = DistinctSorted(summary.WillWriteFilePaths);
        summary.PortableSubsetFindings = DistinctSorted(summary.PortableSubsetFindings);

        foreach (var table in summary.Tables)
        {
            table.Blockers = DistinctSorted(table.Blockers);
        }

        if (summary.ChangedTables.Count == 0)
        {
            summary.ChangedTables = DistinctSorted(summary.Tables
                .Where(t => string.Equals(t.CacheStatus, "needsUpdate", StringComparison.OrdinalIgnoreCase) || (t.NeedsWriteCache && !string.Equals(t.CacheStatus, "missingCache", StringComparison.OrdinalIgnoreCase)))
                .Select(t => t.TableId));
        }

        if (summary.MissingCacheTables.Count == 0)
        {
            summary.MissingCacheTables = DistinctSorted(summary.Tables
                .Where(t => string.Equals(t.CacheStatus, "missingCache", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.TableId));
        }

        if (writeFormalCache && !hasError && summary.BlockedTables.Count == 0)
        {
            summary.CacheStatus = "upToDate";
        }
        else
        {
            summary.CacheStatus = ComputeSyncCacheStatus(summary, hasError);
        }

        summary.WillWriteFiles = writeFormalCache && (summary.ChangedTables.Count > 0 || summary.MissingCacheTables.Count > 0);
        summary.CanApplyCache = !writeFormalCache && !hasError && summary.BlockedTables.Count == 0 &&
            (string.Equals(summary.CacheStatus, "needsUpdate", StringComparison.OrdinalIgnoreCase) || string.Equals(summary.CacheStatus, "missingCache", StringComparison.OrdinalIgnoreCase));
        summary.NextAction = ComputeSyncNextAction(summary);
    }

    private static string ComputeSyncNextAction(SyncCacheSummary summary)
    {
        var status = summary?.CacheStatus ?? "";
        if (string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "fix-blocker";
        }

        if (summary?.CanApplyCache == true ||
            string.Equals(status, "needsUpdate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "missingCache", StringComparison.OrdinalIgnoreCase))
        {
            return "write-cache";
        }

        if (string.Equals(status, "dialectOutdated", StringComparison.OrdinalIgnoreCase))
        {
            return "repair-cache-dialect";
        }

        if (string.Equals(status, "upToDate", StringComparison.OrdinalIgnoreCase))
        {
            return "import-unity";
        }

        return "run-pr-gate";
    }

    private static void ApplySyncCacheSummary(LifecycleContractResult result, SyncCacheSummary summary)
    {
        result.SyncCacheSummary = summary ?? new SyncCacheSummary();
        var previewFingerprint = FirstNonEmpty(result.SyncCacheSummary.PreviewFingerprint, result.PreviewFingerprint, result.RequestFingerprint);
        result.SyncCacheSummary.PreviewFingerprint = previewFingerprint;
        result.PreviewFingerprint = previewFingerprint;
        MirrorSyncCacheSummary(result);
        if (result.BranchStatus != null && result.BranchStatus.RegisteredOnlineTables.Count > 0)
        {
            result.SyncCacheSummary.ResolvedOnlineTables = new List<ResolvedOnlineTableStatus>(result.BranchStatus.RegisteredOnlineTables);
        }

        foreach (var action in result.Actions.Where(a => string.Equals(a.Action, "sync-cache.cache_hash_gate", StringComparison.OrdinalIgnoreCase)))
        {
            action.Details["cacheStatus"] = result.SyncCacheSummary.CacheStatus;
            action.Details["changedTables"] = string.Join(", ", result.SyncCacheSummary.ChangedTables);
            action.Details["missingCacheTables"] = string.Join(", ", result.SyncCacheSummary.MissingCacheTables);
            action.Details["upToDateTables"] = string.Join(", ", result.SyncCacheSummary.UpToDateTables);
            action.Details["blockedTables"] = string.Join(", ", result.SyncCacheSummary.BlockedTables);
            action.Details["willWriteFiles"] = result.SyncCacheSummary.WillWriteFiles.ToString().ToLowerInvariant();
            action.Details["noChangeKeepsMtime"] = result.SyncCacheSummary.NoChangeKeepsMtime.ToString().ToLowerInvariant();
            action.Details["canApplyCache"] = result.SyncCacheSummary.CanApplyCache.ToString().ToLowerInvariant();
            action.Details["nextAction"] = result.SyncCacheSummary.NextAction;
        }
    }

    private static void MirrorSyncCacheSummary(LifecycleContractResult result)
    {
        result.SyncCacheSummary ??= new SyncCacheSummary();
        result.CacheStatus = result.SyncCacheSummary.CacheStatus;
        result.CanApplyCache = result.SyncCacheSummary.CanApplyCache;
        result.NextAction = result.SyncCacheSummary.NextAction;
        result.ChangedTables = new List<string>(result.SyncCacheSummary.ChangedTables);
        result.MissingCacheTables = new List<string>(result.SyncCacheSummary.MissingCacheTables);
        result.UpToDateTables = new List<string>(result.SyncCacheSummary.UpToDateTables);
        result.BlockedTables = new List<string>(result.SyncCacheSummary.BlockedTables);
        result.Tables = new List<SyncTableCacheStatus>(result.SyncCacheSummary.Tables);
    }

    private static void ApplyReadOnlySyncStatus(LifecycleContractResult result, LifecycleContractRequest request)
    {
        request.SyncCache ??= new SyncCacheContract();
        var branchStatus = result.BranchStatus ?? new BranchStatusSummary();
        var summary = new SyncCacheSummary
        {
            TableCount = branchStatus.TableCountExpected > 0 ? branchStatus.TableCountExpected : branchStatus.TableCountRegistered,
            WillWriteFiles = false,
            NoChangeKeepsMtime = true,
            ResolvedOnlineTables = branchStatus.RegisteredOnlineTables.ToList()
        };

        if (branchStatus.MissingTables.Count > 0 ||
            branchStatus.MissingLocators.Count > 0 ||
            branchStatus.DuplicateConfigSheets.Count > 0)
        {
            summary.BlockedTables.AddRange(branchStatus.MissingTables);
            summary.BlockedTables.AddRange(branchStatus.MissingLocators);
            summary.BlockedTables.AddRange(branchStatus.DuplicateConfigSheets);
        }

        foreach (var table in branchStatus.RegisteredOnlineTables)
        {
            if (string.IsNullOrWhiteSpace(table.TableId))
            {
                continue;
            }

            var tableStatus = new SyncTableCacheStatus
            {
                TableId = table.TableId,
                DisplayName = FirstNonEmpty(table.DisplayName, table.TableId),
                OnlineSemanticHash = table.SemanticHash
            };
            summary.Tables.Add(tableStatus);

            if (!string.IsNullOrWhiteSpace(table.BlockingReason))
            {
                summary.BlockedTables.Add(table.TableId);
                tableStatus.CacheStatus = "blocked";
                tableStatus.Blockers.Add(table.BlockingReason);
                continue;
            }

            var paths = BuildCacheFilePaths(request.SyncCache.CacheDirectory, request.SyncCache.ExcelCacheDirectory, table.TableId)
                .Select(Path.GetFullPath)
                .ToList();
            var semanticPath = paths.Count > 0 ? paths[0] : "";
            var shaPath = paths.Count > 1 ? paths[1] : "";
            var xlsxPath = paths.Count > 2 ? paths[2] : "";
            if (!File.Exists(semanticPath) || !File.Exists(shaPath) || !File.Exists(xlsxPath))
            {
                summary.MissingCacheTables.Add(table.TableId);
                tableStatus.CacheStatus = "missingCache";
                tableStatus.NeedsWriteCache = true;
                continue;
            }

            var localHash = File.ReadAllText(shaPath).Trim();
            tableStatus.LocalSemanticHash = localHash;
            if (!string.IsNullOrWhiteSpace(table.SemanticHash) &&
                !string.Equals(localHash, table.SemanticHash, StringComparison.OrdinalIgnoreCase))
            {
                summary.ChangedTables.Add(table.TableId);
                tableStatus.CacheStatus = "needsUpdate";
                tableStatus.NeedsWriteCache = true;
                continue;
            }

            var seedTable = (request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract()).Tables
                .FirstOrDefault(t => string.Equals(t.TableId, table.TableId, StringComparison.OrdinalIgnoreCase));
            if (seedTable != null)
            {
                var cacheTable = ToCacheDialectTableConfig(seedTable);
                if (cacheTable.UseExcelToSoCacheDialect && CacheXlsxNeedsExcelToSoDialectRewrite(xlsxPath, cacheTable, Directory.GetCurrentDirectory()))
                {
                    tableStatus.CacheStatus = "dialectOutdated";
                    tableStatus.NeedsWriteCache = false;
                    tableStatus.Blockers.Add("cache xlsx 类型行仍是 portable/canonical dialect，导入 Unity 前需要先修复为 ExcelToSO dialect。");
                    summary.PortableSubsetFindings.Add(table.TableId + ": cache 类型行需要修复为 ExcelToSO dialect。");
                    continue;
                }
            }

            summary.UpToDateTables.Add(table.TableId);
            tableStatus.CacheStatus = "upToDate";
            if (string.IsNullOrWhiteSpace(table.SemanticHash))
            {
                summary.PortableSubsetFindings.Add(table.TableId + ": registry.semanticHash 缺失，sync-status 只能确认本地 cache 文件存在；请用 sync-cache dry-run 做最终判断。");
            }
        }

        FinalizeSyncCacheSummary(summary, summary.BlockedTables.Count > 0, writeFormalCache: false);
        result.SyncCacheSummary = summary;
        result.ResolvedOnlineTables = summary.ResolvedOnlineTables;
        MirrorSyncCacheSummary(result);

        var action = result.AddAction("sync-status.local_cache.inspect", "done", "只读：已根据 live registry 与本地 cache/sha 文件估算当前 cache 状态；没有读取/导出在线 Sheet，也没有写文件。");
        action.Details["cacheStatus"] = summary.CacheStatus;
        action.Details["tableCount"] = summary.TableCount.ToString(CultureInfo.InvariantCulture);
        action.Details["upToDateTables"] = string.Join(", ", summary.UpToDateTables);
        action.Details["changedTables"] = string.Join(", ", summary.ChangedTables);
        action.Details["missingCacheTables"] = string.Join(", ", summary.MissingCacheTables);
        action.Details["blockedTables"] = string.Join(", ", summary.BlockedTables);
    }

    private sealed class CacheState
    {
        public bool Missing { get; set; }
        public bool Changed { get; set; }
        public bool Fresh { get; set; }
        public bool DialectOutdated { get; set; }
        public string ExistingHash { get; set; } = "";
    }

    private readonly struct CellReference
    {
        public CellReference(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public int Row { get; }
        public int Column { get; }
    }

    private static async Task<int> SyncLocalInputAsync(Workspace workspace, string input, string tableId)
    {
        if (!File.Exists(input))
        {
            throw new CliException("The input file does not exist.", 2, input);
        }

        var extension = Path.GetExtension(input);
        if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var destination = Path.Combine(workspace.Paths.CacheDirectory, Path.GetFileName(input));
            var fileHash = ComputeFileHash(input);
            var fileCacheHash = "file:" + fileHash;
            var previousHash = await ReadExistingHashAsync(Path.Combine(workspace.Paths.CacheDirectory, tableId + ".sha256"));
            if (!string.Equals(previousHash, fileCacheHash, StringComparison.Ordinal) || !File.Exists(destination))
            {
                File.Copy(input, destination, overwrite: true);
                await File.WriteAllTextAsync(Path.Combine(workspace.Paths.CacheDirectory, tableId + ".sha256"), fileCacheHash + Environment.NewLine, Utf8NoBom);
            }

            Console.WriteLine(tableId + ": file:" + fileHash);
            Console.WriteLine("  cache: " + destination);
            return 0;
        }

        var workbook = await ReadJsonAsync<WorkbookDocument>(input);
        var report = SchemaReviewer.Review(workbook);
        foreach (var finding in report.Findings)
        {
            PrintValidation(finding, details: false);
        }

        var hash = SemanticHasher.ComputeHash(workbook);
        var normalized = SemanticTriangulator.Normalize(workbook);
        var triangulation = SemanticTriangulator.Compare(workbook, workbook, normalized);
        foreach (var diff in triangulation.DiffSummary)
        {
            Console.WriteLine("[error] " + diff + " (sync.triangulation_failed)");
        }

        if (triangulation.Passed && !report.HasErrors)
        {
            await WriteCacheIfChangedAsync(workspace.Paths.CacheDirectory, tableId, workbook, hash, null);
        }

        var semanticPath = Path.Combine(workspace.Paths.CacheDirectory, tableId + ".semantic.json");
        Console.WriteLine(tableId + ": " + hash);
        Console.WriteLine("  cache: " + semanticPath);
        return report.HasErrors || !triangulation.Passed ? 1 : 0;
    }

    private static async Task<int> SeedFromXlsxAsync(ParsedArgs args, string operation = "seed-from-local-xlsx")
    {
        if (args.HasFlag("allow-user-fallback"))
        {
            throw new CliException("seed-from-xlsx 默认且固定使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
        }

        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        var request = await BuildSeedRequestAsync(workspace, args);
        request.Operation = operation;
        request.DryRun = args.HasFlag("dry-run") || request.DryRun;
        ApplySeedConfirmationFlags(request, args);
        ApplyTargetBranchBootstrapArgs(request, args);
        await RequireMatchingTargetBootstrapPreviewAsync(request, args);

        var result = await LifecycleExecutor.ExecuteAsync(request, new CliLifecyclePlatform(args, request), CancellationToken.None);
        await EmitLifecycleResultAsync(args, result);

        foreach (var failure in result.HumanReadableFailures)
        {
            Console.Error.WriteLine("[error] " + failure);
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<int> SyncCacheLifecycleAsync(ParsedArgs args)
    {
        var syncDryRun = args.HasFlag("dry-run") || !args.HasFlag("yes");
        await EmitProgressEventAsync(args, "sync-cache", "sync-start", "", 0, 0, syncDryRun ? "正在预览同步，不会写入文件。" : "正在写入本地 cache；不会写飞书、ProjectSettings 或旧 Excel/。", "info");
        if (args.HasFlag("allow-user-fallback") && !(args.HasFlag("interactive-desktop") && args.HasFlag("dry-run")))
        {
            throw new CliException("sync-cache 默认使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
        }

        await EmitProgressEventAsync(args, "sync-cache", "registry", "", 0, 0, "正在读取注册中心和当前分支在线表定位。", "info");
        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        var request = args.TryGet("manifest", out var manifestPath)
            ? await ReadSeedManifestAsync(workspace, manifestPath, args)
            : NewSeedRequestFromWorkspace(workspace, args);
        request.Operation = "sync-cache";
        request.SyncCache ??= new SyncCacheContract();
        request.DryRun = args.HasFlag("dry-run") || !args.HasFlag("yes");
        request.SyncCache.TableId = args.Get("table", args.Get("tables", ""));
        request.SyncCache.CacheDirectory = args.Get("cache-dir", workspace.Paths.CacheDirectory);
        request.SyncCache.ExcelCacheDirectory = args.Get("excel-cache-dir", Path.Combine(workspace.Paths.StateDirectory, "excel-cache"));
        request.SyncCache.ConfirmApply = args.HasFlag("yes") || args.HasFlag("confirm");
        if (!request.DryRun && !request.SyncCache.ConfirmApply)
        {
            throw new CliException("sync-cache apply 会更新本地 cache，必须显式传 --yes。", 2);
        }

        var hydrateSelection = request.SyncCache.TableId.Contains(",") || request.SyncCache.TableId.Contains(";") ? "" : request.SyncCache.TableId;
        await HydrateSyncCacheRequestFromRegistryAsync(request, args, hydrateSelection);
        await RequireMatchingSyncCachePreviewAsync(request, args);
        var result = await LifecycleExecutor.ExecuteAsync(request, new CliLifecyclePlatform(args, request), CancellationToken.None);
        var syncPreviewFingerprint = ComputeSyncCachePreviewFingerprint(request);
        result.PreviewFingerprint = syncPreviewFingerprint;
        if (request.DryRun)
        {
            result.RequestFingerprint = syncPreviewFingerprint;
        }

        if (result.Success)
        {
            var tables = BuildSyncCacheTables(request, args.Get("table", ""));
            if (tables.Count == 0)
            {
                result.AddFailure("sync-cache apply 找不到当前 branch/profile 的在线 Sheet 定位信息。请确认 ConfigSheets/ProjectSettings 已包含 spreadsheetToken、sheetId 和 TableId + Branch/Profile。");
            }
            else
            {
                var summary = await SyncTableConfigsAsync(workspace, args, tables, request.SyncCache.CacheDirectory, request.SyncCache.ExcelCacheDirectory, writeFormalCache: !request.DryRun);
                ApplySyncCacheSummary(result, summary);
                if (string.Equals(summary.CacheStatus, "blocked", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddFailure((request.DryRun ? "sync-cache dry-run" : "sync-cache apply") + " 没有通过在线读取 / xlsx 导出 / 三方一致性检查。" + (request.DryRun ? "本次没有写本地 cache。" : "已阻断 cache 更新。"));
                }
            }
        }

        await EmitLifecycleResultAsync(args, result);
        foreach (var failure in result.HumanReadableFailures)
        {
            Console.Error.WriteLine("[error] " + failure);
        }

        return result.Success ? 0 : 1;
    }

    private static async Task RequireMatchingSyncCachePreviewAsync(LifecycleContractRequest request, ParsedArgs args)
    {
        if (request == null || request.DryRun)
        {
            return;
        }

        var previewPath = FirstNonEmpty(args.Get("preview-result", ""), args.Get("require-preview", ""));
        if (string.IsNullOrWhiteSpace(previewPath))
        {
            throw new CliException("sync-cache apply 必须带最近一次同输入 dry-run 结果。请先预览同步，再执行 sync-cache --yes --preview-result <result.json>。", 2);
        }

        var fullPreviewPath = Path.GetFullPath(previewPath);
        if (!File.Exists(fullPreviewPath))
        {
            throw new CliException("找不到 sync-cache dry-run result 文件，apply 已阻断。请重新预览同步。", 2, fullPreviewPath);
        }

        var preview = await ReadJsonAsync<LifecycleContractResult>(fullPreviewPath);
        if (!string.Equals(preview.Operation, "sync-cache", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("preview-result 不是 sync-cache dry-run 结果，不能作为写 cache 前置证明。", 2, fullPreviewPath);
        }

        if (!preview.DryRun)
        {
            throw new CliException("preview-result 不是 dry-run 结果，不能作为写 cache 前置证明。请重新生成同步预览。", 2, fullPreviewPath);
        }

        if (!preview.Success)
        {
            throw new CliException("最近一次 sync-cache dry-run 未通过，已阻断写入本地 cache。请先修复预检失败。", 2, string.Join(Environment.NewLine, preview.HumanReadableFailures));
        }

        var cacheStatus = preview.SyncCacheSummary == null ? "" : preview.SyncCacheSummary.CacheStatus;
        if (string.Equals(cacheStatus, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("最近一次同步预检仍是 blocked，不能写本地 cache。", 2, "blockedTables=" + string.Join(",", preview.SyncCacheSummary?.BlockedTables ?? new List<string>()));
        }

        if (string.Equals(cacheStatus, "upToDate", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("最近一次同步预览显示本地 cache 已最新，不需要写入。下一步请导入 Unity 配表资产。", 2, fullPreviewPath);
        }

        var expectedFingerprint = ComputeSyncCachePreviewFingerprint(request);
        var dryRunFingerprint = FirstNonEmpty(preview.PreviewFingerprint, preview.RequestFingerprint, preview.SyncCacheSummary?.PreviewFingerprint ?? "");
        if (string.IsNullOrWhiteSpace(dryRunFingerprint))
        {
            throw new CliException("dry-run result 缺少 requestFingerprint，无法确认 apply 输入是否一致。请重新生成同步预览。", 2, fullPreviewPath);
        }

        if (!string.Equals(dryRunFingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                "sync-cache apply 的输入和最近一次 dry-run 不一致，已阻断写入。请重新生成同步预览。",
                2,
                "dryRunFingerprint=" + dryRunFingerprint + "\napplyPreviewFingerprint=" + expectedFingerprint);
        }
    }

    private static string ComputeSyncCachePreviewFingerprint(LifecycleContractRequest request)
    {
        var originalDryRun = request.DryRun;
        var originalConfirmApply = request.SyncCache?.ConfirmApply ?? false;
        request.DryRun = true;
        if (request.SyncCache != null)
        {
            request.SyncCache.ConfirmApply = false;
        }

        try
        {
            return ComputeRequestFingerprint(request);
        }
        finally
        {
            request.DryRun = originalDryRun;
            if (request.SyncCache != null)
            {
                request.SyncCache.ConfirmApply = originalConfirmApply;
            }
        }
    }

    private static async Task<int> RepairCacheDialectAsync(ParsedArgs args)
    {
        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        var request = args.TryGet("manifest", out var manifestPath)
            ? await ReadSeedManifestAsync(workspace, manifestPath, args)
            : NewSeedRequestFromWorkspace(workspace, args);
        request.Operation = "repair-cache-dialect";
        request.DryRun = args.HasFlag("dry-run") || !args.HasFlag("yes");
        request.SyncCache ??= new SyncCacheContract();
        request.SyncCache.CacheDirectory = args.Get("cache-dir", workspace.Paths.CacheDirectory);
        request.SyncCache.ExcelCacheDirectory = args.Get("excel-cache-dir", Path.Combine(workspace.Paths.StateDirectory, "excel-cache"));

        if (!request.DryRun && !args.HasFlag("yes"))
        {
            throw new CliException("repair-cache-dialect apply 会重写 .config-sheet-forge/excel-cache/*.xlsx 的类型行，必须显式传 --yes。", 2);
        }

        var result = new LifecycleContractResult
        {
            Operation = "repair-cache-dialect",
            DryRun = request.DryRun,
            Success = true,
            GitHead = FirstNonEmpty(await TryRunGitAsync("rev-parse", "HEAD"), "unknown"),
            Branch = FirstNonEmpty(await TryRunGitAsync("branch", "--show-current"), "unknown"),
            RequestFingerprint = ComputeRequestFingerprint(request)
        };
        result.RequestSummary["cacheDirectory"] = request.SyncCache.CacheDirectory;
        result.RequestSummary["excelCacheDirectory"] = request.SyncCache.ExcelCacheDirectory;
        result.RequestSummary["network"] = "none";

        var tables = BuildCacheDialectTables(request, args.Get("table", ""), args.Get("tables", ""));
        result.SyncCacheSummary.TableCount = tables.Count;
        if (tables.Count == 0)
        {
            result.AddFailure("repair-cache-dialect 没有找到可处理的表。请检查 manifest 或 --table/--tables 参数。");
            await EmitLifecycleResultAsync(args, result);
            return 1;
        }

        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            await EmitProgressEventAsync(args, "repair-cache-dialect", "cache dialect", table.Id, i + 1, tables.Count, "检查 cache 类型行：" + table.Id, "info");
            var action = result.AddAction("cache_dialect.repair", request.DryRun ? "planned" : "done", table.Id + " cache dialect 已检查。");
            var tableStatus = new SyncTableCacheStatus
            {
                TableId = table.Id,
                DisplayName = FirstNonEmpty(table.Name, table.Id)
            };
            result.SyncCacheSummary.Tables.Add(tableStatus);
            action.Details["tableId"] = table.Id;
            action.Details["network"] = "none";
            action.Details["writesOldExcel"] = "false";
            action.Details["writesFeishu"] = "false";
            action.Details["writesProjectSettings"] = "false";

            if (!table.UseExcelToSoCacheDialect)
            {
                action.Status = "skipped";
                action.Message = table.Id + " 未启用 ExcelToSO cache dialect，不需要重写。";
                result.SyncCacheSummary.UpToDateTables.Add(table.Id);
                tableStatus.CacheStatus = "upToDate";
                continue;
            }

            var semanticPath = Path.Combine(request.SyncCache.CacheDirectory, table.Id + ".semantic.json");
            var excelCacheRoot = FirstNonEmpty(request.SyncCache.ExcelCacheDirectory, request.SyncCache.CacheDirectory);
            var xlsxPath = Path.Combine(excelCacheRoot, MakeSafeFileName(table.Id) + ".xlsx");
            action.Details["semanticPath"] = semanticPath;
            action.Details["xlsxPath"] = xlsxPath;

            if (!File.Exists(semanticPath) || !File.Exists(xlsxPath))
            {
                action.Status = "blocked";
                action.Message = table.Id + " 缺少 semantic cache 或 xlsx cache，无法只做 dialect 快速修复。请先运行 sync-cache dry-run/apply。";
                result.SyncCacheSummary.BlockedTables.Add(table.Id);
                tableStatus.CacheStatus = "blocked";
                tableStatus.Blockers.Add(action.Message);
                result.AddFailure(action.Message);
                continue;
            }

            WorkbookDocument workbook;
            try
            {
                workbook = await ReadJsonAsync<WorkbookDocument>(semanticPath);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                action.Status = "blocked";
                action.Message = table.Id + " 的 semantic cache 无法读取：" + ex.Message;
                result.SyncCacheSummary.BlockedTables.Add(table.Id);
                tableStatus.CacheStatus = "blocked";
                tableStatus.Blockers.Add(action.Message);
                result.AddFailure(action.Message);
                continue;
            }

            var semanticHash = SemanticHasher.ComputeHash(workbook);
            action.Details["semanticHash"] = semanticHash;
            tableStatus.OnlineSemanticHash = semanticHash;
            tableStatus.LocalSemanticHash = semanticHash;
            if (!CacheXlsxNeedsExcelToSoDialectRewrite(xlsxPath, table, workspace.Root))
            {
                action.Status = "skipped";
                action.Message = table.Id + " cache 类型行已经是 ExcelToSO dialect，无需重写。";
                result.SyncCacheSummary.UpToDateTables.Add(table.Id);
                tableStatus.CacheStatus = "upToDate";
                continue;
            }

            var plan = BuildExcelToSoCacheDialectPlan(workbook, table, workspace.Root);
            action.Details["typeRow"] = string.Join(",", plan.TypeRow);
            foreach (var warning in plan.Warnings)
            {
                result.SyncCacheSummary.PortableSubsetFindings.Add(table.Id + ": " + warning);
            }

            if (plan.Errors.Count > 0)
            {
                action.Status = "blocked";
                action.Message = table.Id + " 无法安全重写 cache dialect：" + string.Join("；", plan.Errors);
                result.SyncCacheSummary.BlockedTables.Add(table.Id);
                tableStatus.CacheStatus = "blocked";
                tableStatus.Blockers.Add(action.Message);
                result.AddFailure(action.Message);
                continue;
            }

            result.SyncCacheSummary.ChangedTables.Add(table.Id);
            result.SyncCacheSummary.WillWriteFilePaths.Add(xlsxPath);
            tableStatus.CacheStatus = request.DryRun ? "dialectOutdated" : "upToDate";
            tableStatus.NeedsWriteCache = false;
            action.Message = request.DryRun
                ? table.Id + " 可以快速重写 cache 类型行；不会联网，不改 semantic/hash。"
                : table.Id + " 已重写 cache 类型行；没有联网，没有改 semantic/hash。";
            if (!request.DryRun)
            {
                try
                {
                    WriteExcelToSoCacheXlsx(xlsxPath, workbook, table, plan.TypeRow, workspace.Root);
                }
                catch (Exception ex) when (ex is CliException || ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    action.Status = "blocked";
                    action.Message = table.Id + " cache 类型行重写后无法通过 ExcelToSO 可读性检查：" + ex.Message;
                    result.SyncCacheSummary.BlockedTables.Add(table.Id);
                    tableStatus.CacheStatus = "blocked";
                    tableStatus.Blockers.Add(action.Message);
                    result.AddFailure(action.Message);
                    continue;
                }
            }
        }

        result.SyncCacheSummary.ChangedTables = DistinctSorted(result.SyncCacheSummary.ChangedTables);
        result.SyncCacheSummary.UpToDateTables = DistinctSorted(result.SyncCacheSummary.UpToDateTables);
        result.SyncCacheSummary.BlockedTables = DistinctSorted(result.SyncCacheSummary.BlockedTables);
        result.SyncCacheSummary.WillWriteFilePaths = DistinctSorted(result.SyncCacheSummary.WillWriteFilePaths);
        result.SyncCacheSummary.PortableSubsetFindings = DistinctSorted(result.SyncCacheSummary.PortableSubsetFindings);
        result.SyncCacheSummary.WillWriteFiles = !request.DryRun && result.SyncCacheSummary.ChangedTables.Count > 0;
        result.SyncCacheSummary.NoChangeKeepsMtime = true;
        result.SyncCacheSummary.CacheStatus = result.SyncCacheSummary.BlockedTables.Count > 0
            ? "blocked"
            : result.SyncCacheSummary.ChangedTables.Count > 0
                ? (request.DryRun ? "dialectOutdated" : "upToDate")
                : "upToDate";
        result.SyncCacheSummary.CanApplyCache = false;
        result.SyncCacheSummary.NextAction = result.SyncCacheSummary.BlockedTables.Count > 0
            ? "fix-blocker"
            : result.SyncCacheSummary.ChangedTables.Count > 0
                ? (request.DryRun ? "repair-cache-dialect" : "import-unity")
                : "import-unity";
        MirrorSyncCacheSummary(result);

        await EmitLifecycleResultAsync(args, result);
        foreach (var failure in result.HumanReadableFailures)
        {
            Console.Error.WriteLine("[error] " + failure);
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<int> RegistryStatusAsync(ParsedArgs args, string operation)
    {
        await EmitProgressEventAsync(args, operation, operation, "", 0, 0, string.Equals(operation, "sync-status", StringComparison.OrdinalIgnoreCase) ? "正在快速检查 registry 和本地 cache，不导出 xlsx。" : "正在读取在线注册中心。", "info");
        if (args.HasFlag("allow-user-fallback") && !args.HasFlag("interactive-desktop"))
        {
            throw new CliException(operation + " 默认使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
        }

        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        await EmitProgressEventAsync(args, operation, "registry", "", 0, 0, "正在读取当前 branch/profile 的 BranchBindings 和 ConfigSheets。", "info");
        var request = args.TryGet("manifest", out var manifestPath)
            ? await ReadSeedManifestAsync(workspace, manifestPath, args)
            : NewSeedRequestFromWorkspace(workspace, args);
        request.Operation = operation;
        request.DryRun = true;
        request.SyncCache ??= new SyncCacheContract();
        request.SyncCache.TableId = args.Get("table", args.Get("tables", request.SyncCache.TableId));
        await HydrateSyncCacheRequestFromRegistryAsync(request, args, SingleTableSelectionForHydrate(request.SyncCache.TableId));
        var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
        if (string.Equals(operation, "sync-status", StringComparison.OrdinalIgnoreCase))
        {
            ApplyReadOnlySyncStatus(result, request);
        }

        await EmitLifecycleResultAsync(args, result);
        foreach (var failure in result.HumanReadableFailures)
        {
            Console.Error.WriteLine("[error] " + failure);
        }

        return result.Success ? 0 : 1;
    }

    private static async Task HydrateCurrentBranchBootstrapRequestFromRegistryAsync(LifecycleContractRequest request, ParsedArgs args, string selectedTable)
    {
        if (request == null || request.Registry == null || string.IsNullOrWhiteSpace(request.Registry.BaseToken))
        {
            return;
        }

        var gateway = new LarkCliGateway(args.Get("lark-cli", "lark-cli"));
        var snapshot = await LoadRegistrySnapshotFromLarkAsync(gateway, request.Registry.BaseToken, request.Locale, args);
        HydrateCompareMergeRequestFromRegistrySnapshot(request, snapshot, selectedTable);
    }

    private static async Task<int> CurrentBranchBootstrapFromTargetAsync(ParsedArgs args, string operation)
    {
        if (args.HasFlag("allow-user-fallback"))
        {
            throw new CliException(operation + " 默认使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
        }

        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        var request = args.TryGet("manifest", out var manifestPath)
            ? await ReadSeedManifestAsync(workspace, manifestPath, args)
            : NewSeedRequestFromWorkspace(workspace, args);
        request.Operation = operation;
        var hasApplyConfirmations = args.HasFlag("confirm-create-online-sheets") ||
                                    args.HasFlag("confirm-registry-upsert") ||
                                    args.HasFlag("confirm-schema-reviews") ||
                                    args.HasFlag("confirm-write-local-cache") ||
                                    args.HasFlag("confirm-project-config") ||
                                    args.HasFlag("confirm-write-project-config") ||
                                    args.HasFlag("confirm-excel-to-so");
        request.DryRun = args.HasFlag("dry-run") || !(args.HasFlag("apply") || hasApplyConfirmations);
        request.SyncCache ??= new SyncCacheContract();
        request.MergeInputs ??= new MergeInputsContract();
        request.TargetBranchBootstrap ??= new TargetBranchBootstrapContract();
        request.MergeInputs.TargetBranch = FirstNonEmpty(args.Get("target-branch", ""), request.MergeInputs.TargetBranch, request.BranchWorkspace.MainGitBranch, "main");
        request.MergeInputs.TargetFeishuProfile = FirstNonEmpty(args.Get("target-profile", ""), args.Get("target-feishu-profile", ""), request.MergeInputs.TargetFeishuProfile, request.BranchWorkspace.MainFeishuBranch, request.MergeInputs.TargetBranch);
        request.TargetBranchBootstrap.ConfirmCreateOnlineSheets = request.TargetBranchBootstrap.ConfirmCreateOnlineSheets || args.HasFlag("confirm-create-online-sheets");
        request.TargetBranchBootstrap.ConfirmRegistryUpsert = request.TargetBranchBootstrap.ConfirmRegistryUpsert || args.HasFlag("confirm-registry-upsert");
        request.TargetBranchBootstrap.ConfirmSchemaReviews = request.TargetBranchBootstrap.ConfirmSchemaReviews || args.HasFlag("confirm-schema-reviews");
        request.TargetBranchBootstrap.ConfirmWriteLocalCache = request.TargetBranchBootstrap.ConfirmWriteLocalCache || args.HasFlag("confirm-write-local-cache");
        request.TargetBranchBootstrap.ConfirmWriteProjectConfig = request.TargetBranchBootstrap.ConfirmWriteProjectConfig || args.HasFlag("confirm-project-config") || args.HasFlag("confirm-write-project-config");
        request.TargetBranchBootstrap.ConfirmExcelToSoSettings = request.TargetBranchBootstrap.ConfirmExcelToSoSettings || args.HasFlag("confirm-excel-to-so");
        await HydrateCurrentBranchBootstrapRequestFromRegistryAsync(request, args, request.SyncCache.TableId);
        var result = request.DryRun
            ? await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None)
            : await ApplyCurrentBranchBootstrapFromTargetAsync(workspace, args, request, operation);
        await EmitLifecycleResultAsync(args, result);
        foreach (var failure in result.HumanReadableFailures)
        {
            Console.Error.WriteLine("[error] " + failure);
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<LifecycleContractResult> ApplyCurrentBranchBootstrapFromTargetAsync(Workspace workspace, ParsedArgs args, LifecycleContractRequest request, string operation)
    {
        var originalDryRun = request.DryRun;
        request.DryRun = true;
        var previewPlan = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
        request.DryRun = originalDryRun;
        await RequireMatchingCurrentBranchBootstrapPreviewAsync(previewPlan, args);

        var confirmations = request.TargetBranchBootstrap ?? new TargetBranchBootstrapContract();
        if (!confirmations.ConfirmCreateOnlineSheets)
        {
            previewPlan.DryRun = false;
            previewPlan.AddFailure("从目标分支初始化当前分支需要创建/复用在线 Sheet。apply 前必须显式确认 confirm-create-online-sheets。");
            return previewPlan;
        }

        if (!confirmations.ConfirmRegistryUpsert)
        {
            previewPlan.DryRun = false;
            previewPlan.AddFailure("从目标分支初始化当前分支需要写 BranchBindings / ConfigSheets。apply 前必须显式确认 confirm-registry-upsert。");
            return previewPlan;
        }

        if (!confirmations.ConfirmSchemaReviews)
        {
            previewPlan.DryRun = false;
            previewPlan.AddFailure("从目标分支初始化当前分支需要登记 SchemaReviews baseline。apply 前必须显式确认 confirm-schema-reviews。");
            return previewPlan;
        }

        if (!previewPlan.Success)
        {
            previewPlan.DryRun = false;
            previewPlan.AddFailure("从目标分支初始化当前分支 apply 前预览未通过，请先修复目标分支在线表定位。");
            return previewPlan;
        }

        var seedBuild = await BuildCurrentBranchSeedRequestFromTargetAsync(workspace, args, request, previewPlan);
        if (!seedBuild.Success)
        {
            seedBuild.FailureResult.Operation = operation;
            seedBuild.FailureResult.DryRun = false;
            seedBuild.FailureResult.RequestFingerprint = previewPlan.RequestFingerprint;
            return seedBuild.FailureResult;
        }

        var applyRequest = seedBuild.Request;
        var result = await LifecycleExecutor.ExecuteAsync(applyRequest, new CliLifecyclePlatform(args, applyRequest), CancellationToken.None);
        result.Operation = operation;
        result.DryRun = false;
        result.RequestFingerprint = previewPlan.RequestFingerprint;
        result.RequestSummary["sourceMode"] = "target-branch";
        result.RequestSummary["targetBranch"] = request.MergeInputs.TargetBranch;
        result.RequestSummary["targetProfile"] = request.MergeInputs.TargetFeishuProfile;
        result.RequestSummary["writesLocalCache"] = confirmations.ConfirmWriteLocalCache.ToString().ToLowerInvariant();
        result.RequestSummary["writesProjectSettings"] = confirmations.ConfirmWriteProjectConfig.ToString().ToLowerInvariant();
        result.RequestSummary["writesExcelToSo"] = confirmations.ConfirmExcelToSoSettings.ToString().ToLowerInvariant();
        result.Actions.Insert(0, new LifecycleActionResult
        {
            Action = "current_branch.bootstrap_from_target.apply",
            Status = result.Success ? "done" : "blocked",
            Message = result.Success
                ? "已从目标分支在线 Source of Truth 派生当前分支在线工作区；默认不写本地 cache、ProjectSettings 或 ExcelToSO。"
                : "从目标分支派生当前分支未完成，请查看后续 seed/registry/postflight 失败原因。",
            Details =
            {
                ["requestFingerprint"] = previewPlan.RequestFingerprint,
                ["targetBranch"] = request.MergeInputs.TargetBranch,
                ["targetProfile"] = request.MergeInputs.TargetFeishuProfile,
                ["currentBranch"] = request.Git.Branch,
                ["currentProfile"] = FirstNonEmpty(request.Git.Profile, request.Git.FeishuBranch),
                ["writeLocalCache"] = confirmations.ConfirmWriteLocalCache.ToString().ToLowerInvariant(),
                ["writeProjectSettings"] = confirmations.ConfirmWriteProjectConfig.ToString().ToLowerInvariant(),
                ["writeExcelToSo"] = confirmations.ConfirmExcelToSoSettings.ToString().ToLowerInvariant()
            }
        });
        return result;
    }

    private sealed class CurrentBranchSeedBuildResult
    {
        public LifecycleContractRequest Request { get; set; } = new LifecycleContractRequest();
        public LifecycleContractResult FailureResult { get; set; } = new LifecycleContractResult();
        public bool Success { get; set; }
    }

    private static async Task<CurrentBranchSeedBuildResult> BuildCurrentBranchSeedRequestFromTargetAsync(Workspace workspace, ParsedArgs args, LifecycleContractRequest request, LifecycleContractResult previewPlan)
    {
        var failure = new LifecycleContractResult
        {
            Operation = request.Operation,
            DryRun = false,
            RequestFingerprint = previewPlan.RequestFingerprint,
            BranchWorkspace = previewPlan.BranchWorkspace,
            BranchStatus = previewPlan.BranchStatus
        };
        var currentWorkspace = BranchWorkspaceResolver.Resolve(request);
        var targetProfile = FirstNonEmpty(request.MergeInputs.TargetFeishuProfile, request.MergeInputs.TargetBranch, request.BranchWorkspace.MainFeishuBranch, "main");
        var targetRows = FindSeedRowsForProfile(request, targetProfile, request.SyncCache != null ? request.SyncCache.TableId : "")
            .Where(t => !string.IsNullOrWhiteSpace(FirstNonEmpty(t.SpreadsheetToken, t.SpreadsheetUrl)) && !string.IsNullOrWhiteSpace(t.SheetId))
            .ToList();
        if (targetRows.Count == 0)
        {
            failure.AddFailure("无法从目标分支派生当前分支：目标分支 “" + FirstNonEmpty(request.MergeInputs.TargetBranch, targetProfile) + "” 没有可复制的在线 Sheet 定位。");
            return new CurrentBranchSeedBuildResult { FailureResult = failure };
        }

        var provider = new LarkCliWorkbookProvider();
        var providerContext = CreateProviderContext(workspace, args);
        var tempRoot = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ConfigSheetForge", "branch-bootstrap-from-target", Guid.NewGuid().ToString("N"));
        var seedTables = new List<SeedTableContract>();
        foreach (var target in targetRows)
        {
            var tableTemp = Path.Combine(tempRoot, MakeSafeFileName(target.TableId));
            Directory.CreateDirectory(tableTemp);
            Console.WriteLine("[stage] 正在从目标分支导出在线表: " + target.TableId);
            var export = await provider.ExportAsync(providerContext, new ProviderExportRequest
            {
                RootTokenOrUrl = FirstNonEmpty(target.SpreadsheetToken, target.SpreadsheetUrl),
                SpreadsheetTokenOrUrl = FirstNonEmpty(target.SpreadsheetToken, target.SpreadsheetUrl),
                TableId = target.TableId,
                SheetId = target.SheetId,
                Range = "",
                CacheDirectory = tableTemp,
                FieldRow = target.FieldRow,
                TypeRow = target.TypeRow,
                DescriptionRow = target.DescriptionRow,
                DataStartRow = target.DataStartRow,
                TreatUnknownTypesAsEnum = target.TreatUnknownTypesAsEnum
            }, CancellationToken.None);
            foreach (var finding in export.Findings)
            {
                PrintFinding(finding, args.HasFlag("details"));
            }

            var xlsxPath = Path.GetExtension(export.CachePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? export.CachePath
                : FindExportedXlsx(tableTemp, target.TableId);
            if (export.Findings.Any(f => f.Severity == FindingSeverity.Error) || string.IsNullOrWhiteSpace(xlsxPath) || !File.Exists(xlsxPath))
            {
                failure.AddFailure("无法从目标分支导出配表 “" + target.TableId + "” 为 xlsx；已阻断当前分支初始化，避免创建不完整在线表。");
                continue;
            }

            seedTables.Add(new SeedTableContract
            {
                TableId = target.TableId,
                DisplayName = FirstNonEmpty(target.DisplayName, target.TableId),
                SourceXlsxPath = xlsxPath,
                SheetName = FirstNonEmpty(target.SheetName, target.DisplayName, target.TableId),
                Branch = FirstNonEmpty(currentWorkspace.FeishuBranch, currentWorkspace.Profile, request.Git.Branch),
                Profile = FirstNonEmpty(currentWorkspace.Profile, currentWorkspace.FeishuBranch, request.Git.Branch),
                OwnerRole = target.OwnerRole,
                CacheXlsxPath = target.CacheXlsxPath,
                SemanticCachePath = target.SemanticCachePath,
                HashCachePath = target.HashCachePath,
                ProjectConfigPath = target.ProjectConfigPath,
                SchemaReviewRequired = target.SchemaReviewRequired,
                FieldRow = target.FieldRow,
                TypeRow = target.TypeRow,
                DescriptionRow = target.DescriptionRow,
                DataStartRow = target.DataStartRow,
                TreatUnknownTypesAsEnum = target.TreatUnknownTypesAsEnum
            });
        }

        if (!failure.Success)
        {
            return new CurrentBranchSeedBuildResult { FailureResult = failure };
        }

        var confirmations = request.TargetBranchBootstrap ?? new TargetBranchBootstrapContract();
        var sourceSeed = request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract();
        var seedRequest = new LifecycleContractRequest
        {
            Operation = "bootstrap-target-branch-from-local-xlsx",
            DryRun = false,
            Locale = request.Locale,
            Git = new ContractGitSpec
            {
                Branch = currentWorkspace.GitBranch,
                FeishuBranch = FirstNonEmpty(currentWorkspace.FeishuBranch, currentWorkspace.Profile),
                Profile = FirstNonEmpty(currentWorkspace.Profile, currentWorkspace.FeishuBranch),
                Head = request.Git.Head
            },
            Registry = request.Registry,
            BranchWorkspace = request.BranchWorkspace,
            UnityExcelToSo = request.UnityExcelToSo,
            TargetBranchBootstrap = new TargetBranchBootstrapContract
            {
                TargetGitBranch = currentWorkspace.GitBranch,
                TargetFeishuProfile = FirstNonEmpty(currentWorkspace.Profile, currentWorkspace.FeishuBranch),
                TargetBranchWikiNodeTitle = currentWorkspace.NodeTitle,
                SourceMode = "local-xlsx",
                ConfirmCreateOnlineSheets = confirmations.ConfirmCreateOnlineSheets,
                ConfirmRegistryUpsert = confirmations.ConfirmRegistryUpsert,
                ConfirmSchemaReviews = confirmations.ConfirmSchemaReviews,
                ConfirmWriteLocalCache = confirmations.ConfirmWriteLocalCache,
                ConfirmWriteProjectConfig = confirmations.ConfirmWriteProjectConfig,
                ConfirmExcelToSoSettings = confirmations.ConfirmExcelToSoSettings
            },
            SeedFromLocalXlsx = new SeedFromLocalXlsxContract
            {
                SourceMode = "local-xlsx",
                CacheDirectory = sourceSeed.CacheDirectory,
                ExcelCacheDirectory = sourceSeed.ExcelCacheDirectory,
                ProjectConfigPath = sourceSeed.ProjectConfigPath,
                BaselineStrategy = sourceSeed.BaselineStrategy,
                PreferDriveImport = sourceSeed.PreferDriveImport,
                ConfirmCreateOnlineSheets = confirmations.ConfirmCreateOnlineSheets,
                ConfirmRegistryUpsert = confirmations.ConfirmRegistryUpsert,
                ConfirmSchemaReviews = confirmations.ConfirmSchemaReviews,
                ConfirmWriteLocalCache = confirmations.ConfirmWriteLocalCache,
                ConfirmWriteProjectConfig = confirmations.ConfirmWriteProjectConfig,
                ConfirmProjectConfigUpdate = confirmations.ConfirmWriteProjectConfig,
                ConfirmExcelToSoSettings = confirmations.ConfirmExcelToSoSettings,
                ConfirmExcelToSoSettingsUpdate = confirmations.ConfirmExcelToSoSettings
            }
        };
        seedRequest.SeedFromLocalXlsx.Tables.AddRange(seedTables);
        foreach (var tableId in seedTables.Select(t => t.TableId).Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            seedRequest.TargetBranchBootstrap.TableIds.Add(tableId);
            seedRequest.SeedFromLocalXlsx.TableIds.Add(tableId);
        }

        return new CurrentBranchSeedBuildResult { Success = true, Request = seedRequest };
    }

    private static async Task RequireMatchingCurrentBranchBootstrapPreviewAsync(LifecycleContractResult expected, ParsedArgs args)
    {
        var requiredFingerprint = args.Get("required-preview-fingerprint", "");
        if (!string.IsNullOrWhiteSpace(requiredFingerprint) &&
            !string.Equals(requiredFingerprint, expected.RequestFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("当前分支初始化 apply 的输入和指定 dry-run fingerprint 不一致，已阻断写入。请重新生成 dry-run，再用新的 result.json 执行 apply。", 2);
        }

        var previewPath = FirstNonEmpty(args.Get("preview-result", ""), args.Get("require-preview", ""));
        if (string.IsNullOrWhiteSpace(previewPath))
        {
            throw new CliException("当前分支初始化 apply 必须带最近一次同输入 dry-run 结果。请先运行 bootstrap-current-branch-from-target --dry-run，并在 apply 时传 --preview-result <dry-run-result.json>。", 2);
        }

        var fullPreviewPath = Path.GetFullPath(previewPath);
        if (!File.Exists(fullPreviewPath))
        {
            throw new CliException("找不到当前分支初始化 dry-run result 文件，apply 已阻断。请重新生成 dry-run，并确认 --preview-result 指向正确文件。", 2, fullPreviewPath);
        }

        var preview = await ReadJsonAsync<LifecycleContractResult>(fullPreviewPath);
        if (!string.Equals(preview.Operation, "bootstrap-current-branch-from-target", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(preview.Operation, "branch-workspace-bootstrap-from-target", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("dry-run result 不是当前分支初始化操作，已阻断 apply。请重新生成“从目标分支初始化当前分支”的 dry-run。", 2, fullPreviewPath);
        }

        if (!preview.DryRun || !preview.Success)
        {
            throw new CliException("最近一次当前分支初始化 dry-run 没有通过，不能 apply。请先处理 dry-run 中的失败原因。", 2, string.Join(Environment.NewLine, preview.HumanReadableFailures));
        }

        if (string.IsNullOrWhiteSpace(preview.RequestFingerprint))
        {
            throw new CliException("dry-run result 缺少 requestFingerprint，无法确认 apply 输入是否一致。请使用 v0.4.24 或更新版本重新生成 dry-run。", 2, fullPreviewPath);
        }

        if (!string.Equals(preview.RequestFingerprint, expected.RequestFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("当前分支初始化 apply 的输入和最近一次 dry-run 不一致，已阻断写入。请重新生成 dry-run，再执行 apply。", 2, "dryRunFingerprint=" + preview.RequestFingerprint + "\napplyFingerprint=" + expected.RequestFingerprint);
        }
    }

    private static List<SeedTableContract> FindSeedRowsForProfile(LifecycleContractRequest request, string profile, string selectedTable)
    {
        return (request.SeedFromLocalXlsx != null ? request.SeedFromLocalXlsx.Tables : new List<SeedTableContract>())
            .Where(t => !string.IsNullOrWhiteSpace(t.TableId))
            .Where(t => string.IsNullOrWhiteSpace(selectedTable) || string.Equals(t.TableId, selectedTable, StringComparison.OrdinalIgnoreCase))
            .Where(t =>
            {
                var tableProfile = FirstNonEmpty(t.Profile, t.Branch);
                return !string.IsNullOrWhiteSpace(tableProfile) &&
                       (string.IsNullOrWhiteSpace(profile) || string.Equals(tableProfile, profile, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(t => t.TableId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<TableConfig> BuildSyncCacheTables(LifecycleContractRequest request, string selectedTable)
    {
        var seed = request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract();
        var selectedSet = SplitTableSelection(selectedTable);
        var tables = new List<TableConfig>();
        foreach (var table in seed.Tables)
        {
            if (selectedSet.Count > 0 && !selectedSet.Contains(table.TableId))
            {
                continue;
            }

            var spreadsheet = FirstNonEmpty(table.SpreadsheetToken, table.SpreadsheetUrl);
            if (string.IsNullOrWhiteSpace(table.TableId) || string.IsNullOrWhiteSpace(spreadsheet))
            {
                continue;
            }

            tables.Add(new TableConfig
            {
                Id = table.TableId,
                Name = FirstNonEmpty(table.DisplayName, table.TableId),
                Provider = "lark",
                Spreadsheet = spreadsheet,
                SheetId = table.SheetId,
                Range = "",
                LocalSourcePath = table.SourceXlsxPath,
                UseExcelToSoCacheDialect = UsesExcelToSoCacheDialect(table),
                Fields = table.Fields.Select(CloneFieldSpec).ToList(),
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            });
        }

        return tables;
    }

    private static List<TableConfig> BuildCacheDialectTables(LifecycleContractRequest request, string selectedTable, string selectedTables)
    {
        var selectedSet = SplitTableSelection(selectedTable, selectedTables);
        var seed = request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract();
        var tables = new List<TableConfig>();
        foreach (var table in seed.Tables)
        {
            if (selectedSet.Count > 0 && !selectedSet.Contains(table.TableId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(table.TableId))
            {
                continue;
            }

            tables.Add(new TableConfig
            {
                Id = table.TableId,
                Name = FirstNonEmpty(table.DisplayName, table.TableId),
                Provider = "local",
                Spreadsheet = FirstNonEmpty(table.SpreadsheetToken, table.SpreadsheetUrl),
                SheetId = table.SheetId,
                Range = "",
                LocalSourcePath = table.SourceXlsxPath,
                UseExcelToSoCacheDialect = UsesExcelToSoCacheDialect(table),
                Fields = table.Fields.Select(CloneFieldSpec).ToList(),
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            });
        }

        return tables.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static TableConfig ToCacheDialectTableConfig(SeedTableContract table)
    {
        return new TableConfig
        {
            Id = table.TableId,
            Name = FirstNonEmpty(table.DisplayName, table.TableId),
            Provider = "local",
            Spreadsheet = FirstNonEmpty(table.SpreadsheetToken, table.SpreadsheetUrl),
            SheetId = table.SheetId,
            Range = "",
            LocalSourcePath = table.SourceXlsxPath,
            UseExcelToSoCacheDialect = UsesExcelToSoCacheDialect(table),
            Fields = table.Fields.Select(CloneFieldSpec).ToList(),
            FieldRow = table.FieldRow,
            TypeRow = table.TypeRow,
            DescriptionRow = table.DescriptionRow,
            DataStartRow = table.DataStartRow,
            TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
        };
    }

    private static HashSet<string> SplitTableSelection(params string[] values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            foreach (var part in (value ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var tableId = part.Trim();
                if (!string.IsNullOrWhiteSpace(tableId))
                {
                    set.Add(tableId);
                }
            }
        }

        return set;
    }

    private static string SingleTableSelectionForHydrate(string value)
    {
        return SplitTableSelection(value).Count == 1 ? SplitTableSelection(value).First() : "";
    }

    private static ContractFieldSpec CloneFieldSpec(ContractFieldSpec field)
    {
        return new ContractFieldSpec
        {
            Key = field.Key,
            DisplayName = field.DisplayName,
            ValueKind = field.ValueKind,
            OriginalType = field.OriginalType,
            ExcelToSoType = field.ExcelToSoType,
            Description = field.Description
        };
    }

    private static bool UsesExcelToSoCacheDialect(SeedTableContract table)
    {
        if (table == null)
        {
            return false;
        }

        var unity = table.UnityExcelToSo;
        return unity != null &&
               (!string.IsNullOrWhiteSpace(unity.SettingsPath) ||
                !string.IsNullOrWhiteSpace(unity.ExcelPath) ||
                !string.IsNullOrWhiteSpace(unity.AssetPath) ||
                !string.IsNullOrWhiteSpace(unity.ScriptableObjectType));
    }

    private static void ApplySeedConfirmationFlags(LifecycleContractRequest request, ParsedArgs args)
    {
        request.SeedFromLocalXlsx ??= new SeedFromLocalXlsxContract();
        request.TargetBranchBootstrap ??= new TargetBranchBootstrapContract();

        if (!TargetBranchBootstrapOperationRequested(request.Operation))
        {
            request.SeedFromLocalXlsx.ConfirmApply = request.SeedFromLocalXlsx.ConfirmApply || args.HasFlag("yes") || args.HasFlag("confirm");
        }

        request.SeedFromLocalXlsx.ConfirmCreateOnlineSheets = request.SeedFromLocalXlsx.ConfirmCreateOnlineSheets || args.HasFlag("confirm-create-online-sheets");
        request.SeedFromLocalXlsx.ConfirmRegistryUpsert = request.SeedFromLocalXlsx.ConfirmRegistryUpsert || args.HasFlag("confirm-registry-upsert");
        request.SeedFromLocalXlsx.ConfirmSchemaReviews = request.SeedFromLocalXlsx.ConfirmSchemaReviews || args.HasFlag("confirm-schema-reviews");
        request.SeedFromLocalXlsx.ConfirmWriteLocalCache = request.SeedFromLocalXlsx.ConfirmWriteLocalCache || args.HasFlag("confirm-write-local-cache");
        request.SeedFromLocalXlsx.ConfirmWriteProjectConfig = request.SeedFromLocalXlsx.ConfirmWriteProjectConfig || args.HasFlag("confirm-write-project-config") || args.HasFlag("confirm-project-config");
        request.SeedFromLocalXlsx.ConfirmExcelToSoSettings = request.SeedFromLocalXlsx.ConfirmExcelToSoSettings || args.HasFlag("confirm-excel-to-so");
        request.SeedFromLocalXlsx.ConfirmExcelToSoSettingsUpdate = request.SeedFromLocalXlsx.ConfirmExcelToSoSettingsUpdate || args.HasFlag("confirm-excel-to-so");
        request.SeedFromLocalXlsx.ConfirmProjectConfigUpdate = request.SeedFromLocalXlsx.ConfirmProjectConfigUpdate || args.HasFlag("confirm-project-config") || args.HasFlag("confirm-write-project-config");

        if (TargetBranchBootstrapOperationRequested(request.Operation))
        {
            request.TargetBranchBootstrap.ConfirmCreateOnlineSheets = request.TargetBranchBootstrap.ConfirmCreateOnlineSheets || args.HasFlag("confirm-create-online-sheets");
            request.TargetBranchBootstrap.ConfirmRegistryUpsert = request.TargetBranchBootstrap.ConfirmRegistryUpsert || args.HasFlag("confirm-registry-upsert");
            request.TargetBranchBootstrap.ConfirmSchemaReviews = request.TargetBranchBootstrap.ConfirmSchemaReviews || args.HasFlag("confirm-schema-reviews");
            request.TargetBranchBootstrap.ConfirmWriteLocalCache = request.TargetBranchBootstrap.ConfirmWriteLocalCache || args.HasFlag("confirm-write-local-cache");
            request.TargetBranchBootstrap.ConfirmWriteProjectConfig = request.TargetBranchBootstrap.ConfirmWriteProjectConfig || args.HasFlag("confirm-write-project-config") || args.HasFlag("confirm-project-config");
            request.TargetBranchBootstrap.ConfirmExcelToSoSettings = request.TargetBranchBootstrap.ConfirmExcelToSoSettings || args.HasFlag("confirm-excel-to-so");
        }
    }

    private static void ApplyTargetBranchBootstrapArgs(LifecycleContractRequest request, ParsedArgs args)
    {
        if (!TargetBranchBootstrapOperationRequested(request.Operation))
        {
            return;
        }

        request.TargetBranchBootstrap ??= new TargetBranchBootstrapContract();
        request.MergeInputs ??= new MergeInputsContract();
        request.TargetBranchBootstrap.TargetGitBranch = FirstNonEmpty(args.Get("target-git-branch", ""), args.Get("target-branch", ""), request.TargetBranchBootstrap.TargetGitBranch, request.MergeInputs.TargetBranch);
        request.TargetBranchBootstrap.TargetFeishuProfile = FirstNonEmpty(args.Get("target-profile", ""), args.Get("target-feishu-profile", ""), request.TargetBranchBootstrap.TargetFeishuProfile, request.MergeInputs.TargetFeishuProfile);
        request.TargetBranchBootstrap.TargetBranchWikiNodeTitle = FirstNonEmpty(args.Get("target-branch-wiki-node-title", ""), args.Get("target-node-title", ""), request.TargetBranchBootstrap.TargetBranchWikiNodeTitle, request.MergeInputs.TargetBranchWikiNodeTitle);
        request.TargetBranchBootstrap.SourceMode = FirstNonEmpty(args.Get("source-mode", ""), request.TargetBranchBootstrap.SourceMode, "local-xlsx");
        request.TargetBranchBootstrap.PreviewResultPath = FirstNonEmpty(args.Get("preview-result", ""), args.Get("require-preview", ""), request.TargetBranchBootstrap.PreviewResultPath);
        request.TargetBranchBootstrap.RequiredPreviewFingerprint = FirstNonEmpty(args.Get("required-preview-fingerprint", ""), request.TargetBranchBootstrap.RequiredPreviewFingerprint, request.RequiredPreviewFingerprint);
        request.RequiredPreviewFingerprint = FirstNonEmpty(request.RequiredPreviewFingerprint, request.TargetBranchBootstrap.RequiredPreviewFingerprint);

        var tableIds = args.Get("table-ids", "");
        if (!string.IsNullOrWhiteSpace(tableIds))
        {
            request.TargetBranchBootstrap.TableIds.Clear();
            foreach (var value in tableIds.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var tableId = value.Trim();
                if (!string.IsNullOrWhiteSpace(tableId))
                {
                    request.TargetBranchBootstrap.TableIds.Add(tableId);
                }
            }
        }
    }

    private static async Task RequireMatchingTargetBootstrapPreviewAsync(LifecycleContractRequest request, ParsedArgs args)
    {
        if (!TargetBranchBootstrapOperationRequested(request.Operation) || request.DryRun)
        {
            return;
        }

        request.TargetBranchBootstrap ??= new TargetBranchBootstrapContract();
        var expected = SeedFromLocalXlsxLifecycle.BuildTargetBranchBootstrapInputSummary(request);
        var requiredFingerprint = FirstNonEmpty(
            args.Get("required-preview-fingerprint", ""),
            request.TargetBranchBootstrap.RequiredPreviewFingerprint,
            request.RequiredPreviewFingerprint);
        if (!string.IsNullOrWhiteSpace(requiredFingerprint) &&
            !string.Equals(requiredFingerprint, expected.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                "初始化目标分支 apply 的输入和指定 dry-run fingerprint 不一致，已阻断写入。请重新生成 dry-run，再用新的 result.json 执行 apply。",
                2,
                "expected=" + expected.Fingerprint + "\nprovided=" + requiredFingerprint + "\ntargetBranch=" + expected.TargetGitBranch + "\ntables=" + expected.TableIdsText);
        }

        var previewPath = FirstNonEmpty(
            args.Get("preview-result", ""),
            args.Get("require-preview", ""),
            request.TargetBranchBootstrap.PreviewResultPath);
        if (string.IsNullOrWhiteSpace(previewPath))
        {
            throw new CliException(
                "初始化目标分支 apply 必须带最近一次同输入 dry-run 结果。请先运行 bootstrap-target-branch-from-local-xlsx --dry-run，并在 apply 时传 --preview-result <dry-run-result.json>。",
                2,
                "targetBranch=" + expected.TargetGitBranch + "\ntargetProfile=" + expected.TargetFeishuProfile + "\ntables=" + expected.TableIdsText + "\nfingerprint=" + expected.Fingerprint);
        }

        var fullPreviewPath = Path.GetFullPath(previewPath);
        if (!File.Exists(fullPreviewPath))
        {
            throw new CliException(
                "找不到 dry-run result 文件，初始化目标分支 apply 已阻断。请重新生成 dry-run，并确认 --preview-result 指向正确文件。",
                2,
                fullPreviewPath);
        }

        var preview = await ReadJsonAsync<LifecycleContractResult>(fullPreviewPath);
        if (!string.Equals(preview.Operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("dry-run result 不是目标分支初始化操作，已阻断 apply。请重新生成“初始化目标分支”的 dry-run。", 2, fullPreviewPath);
        }

        if (!preview.DryRun)
        {
            throw new CliException("preview-result 不是 dry-run 结果，不能作为 apply 前置证明。请先生成 dry-run 预览。", 2, fullPreviewPath);
        }

        if (!preview.Success)
        {
            throw new CliException("最近一次目标分支初始化 dry-run 没有通过，不能 apply。请先处理 dry-run 中的失败原因。", 2, string.Join(Environment.NewLine, preview.HumanReadableFailures));
        }

        if (string.IsNullOrWhiteSpace(preview.RequestFingerprint))
        {
            throw new CliException("dry-run result 缺少 requestFingerprint，无法确认 apply 输入是否一致。请使用 v0.4.18 或更新版本重新生成 dry-run。", 2, fullPreviewPath);
        }

        if (!string.Equals(preview.RequestFingerprint, expected.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                "初始化目标分支 apply 的输入和最近一次 dry-run 不一致，已阻断写入。请重新生成 dry-run，再执行 apply。",
                2,
                "dryRunFingerprint=" + preview.RequestFingerprint + "\napplyFingerprint=" + expected.Fingerprint + "\ntargetBranch=" + expected.TargetGitBranch + "\ntables=" + expected.TableIdsText);
        }

        request.RequiredPreviewFingerprint = expected.Fingerprint;
        request.TargetBranchBootstrap.RequiredPreviewFingerprint = expected.Fingerprint;
        request.TargetBranchBootstrap.PreviewResultPath = fullPreviewPath;
    }

    private static void ApplyMergeReviewArgs(LifecycleContractRequest request, ParsedArgs args)
    {
        request.MergeReview ??= new MergeReviewContract();
        request.MergeInputs ??= new MergeInputsContract();
        request.MergeReview.SourceBranch = FirstNonEmpty(args.Get("source-branch", ""), request.MergeReview.SourceBranch, request.MergeInputs.SourceBranch, request.Git.Branch);
        request.MergeReview.TargetBranch = FirstNonEmpty(args.Get("target-branch", ""), request.MergeReview.TargetBranch, request.MergeInputs.TargetBranch, request.BranchWorkspace.MainGitBranch, "main");
        request.MergeReview.PrNumber = FirstNonEmpty(args.Get("pr-number", ""), request.MergeReview.PrNumber, request.MergeInputs.PrNumber);
        request.MergeReview.PrUrl = FirstNonEmpty(args.Get("pr-url", ""), request.MergeReview.PrUrl, request.MergeInputs.PrUrl);
        request.MergeReview.MergeReportPath = FirstNonEmpty(args.Get("merge-report", ""), args.Get("merge-report-path", ""), request.MergeReview.MergeReportPath, request.MergeInputs.MergeReportPath);
        request.MergeReview.MergedPath = FirstNonEmpty(args.Get("merged", ""), args.Get("merged-path", ""), request.MergeReview.MergedPath, request.MergeInputs.MergedPath);
        request.MergeReview.PreviewResultPath = FirstNonEmpty(args.Get("preview-result", ""), args.Get("require-preview", ""), request.MergeReview.PreviewResultPath);
        request.MergeReview.RequestFingerprint = FirstNonEmpty(args.Get("request-fingerprint", ""), request.MergeReview.RequestFingerprint);
        request.MergeReview.RequiredPreviewFingerprint = FirstNonEmpty(args.Get("required-preview-fingerprint", ""), request.MergeReview.RequiredPreviewFingerprint, request.RequiredPreviewFingerprint);
        request.MergeReview.ApproverRole = FirstNonEmpty(args.Get("approver-role", ""), request.MergeReview.ApproverRole, "configOwner");
        request.MergeReview.ReviewComment = FirstNonEmpty(args.Get("review-comment", ""), request.MergeReview.ReviewComment);
        request.MergeReview.TableId = FirstNonEmpty(args.Get("table-id", ""), request.MergeReview.TableId, "__project_pr_gate__");
        request.MergeReview.Status = FirstNonEmpty(args.Get("status", ""), request.MergeReview.Status, "approved");
        request.MergeReview.ConfirmSubmit = request.MergeReview.ConfirmSubmit || args.HasFlag("yes") || args.HasFlag("confirm");

        var tableIds = args.Get("table-ids", "");
        if (!string.IsNullOrWhiteSpace(tableIds))
        {
            request.MergeReview.TableIds.Clear();
            foreach (var value in tableIds.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var tableId = value.Trim();
                if (!string.IsNullOrWhiteSpace(tableId))
                {
                    request.MergeReview.TableIds.Add(tableId);
                }
            }
        }
    }

    private static async Task RequireMatchingCompareMergePreviewAsync(LifecycleContractRequest request, ParsedArgs args)
    {
        if (!MergeReviewOperationRequested(request.Operation) || request.DryRun)
        {
            return;
        }

        request.MergeReview ??= new MergeReviewContract();
        if (args.HasFlag("allow-user-fallback"))
        {
            throw new CliException("提交 MergeReviews 默认使用 strict bot 权限；不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
        }

        var previewPath = FirstNonEmpty(
            args.Get("preview-result", ""),
            args.Get("require-preview", ""),
            request.MergeReview.PreviewResultPath);
        if (string.IsNullOrWhiteSpace(previewPath))
        {
            throw new CliException("提交合并审查记录必须带最近一次同输入 compare-merge dry-run result。请先在 Unity 点“生成合并预览”，再提交审查记录。", 2);
        }

        var fullPreviewPath = Path.GetFullPath(previewPath);
        if (!File.Exists(fullPreviewPath))
        {
            throw new CliException("找不到 compare-merge dry-run result，已阻断 MergeReviews 写入。请重新生成合并预览。", 2, fullPreviewPath);
        }

        var preview = await ReadJsonAsync<LifecycleContractResult>(fullPreviewPath);
        if (!string.Equals(preview.Operation, "compare-merge", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("preview-result 不是合并预览结果，已阻断 MergeReviews 写入。请重新生成“生成合并预览”。", 2, fullPreviewPath);
        }

        if (!preview.DryRun)
        {
            throw new CliException("preview-result 不是 dry-run 结果，不能作为提交审查记录的前置证明。", 2, fullPreviewPath);
        }

        if (!preview.Success)
        {
            throw new CliException("最近一次合并预览没有通过，不能提交合并审查记录。请先处理预览里的失败原因。", 2, string.Join(Environment.NewLine, preview.HumanReadableFailures));
        }

        if (string.IsNullOrWhiteSpace(preview.RequestFingerprint))
        {
            throw new CliException("合并预览结果缺少 requestFingerprint，无法确认审查输入是否一致。请用 v0.4.19 或更新版本重新生成合并预览。", 2, fullPreviewPath);
        }

        BackfillMergeReviewFromPreview(request, preview, fullPreviewPath);
        var expected = LifecycleExecutor.BuildMergeReviewInputSummary(request, request.MergeReview.TableIds);
        var requiredFingerprint = FirstNonEmpty(
            args.Get("required-preview-fingerprint", ""),
            request.MergeReview.RequiredPreviewFingerprint,
            request.MergeReview.RequestFingerprint,
            request.RequiredPreviewFingerprint);
        if (!string.IsNullOrWhiteSpace(requiredFingerprint) &&
            !string.Equals(requiredFingerprint, expected.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("提交合并审查记录的输入和指定 fingerprint 不一致。请重新生成合并预览，再提交审查。", 2, "expected=" + expected.Fingerprint + "\nprovided=" + requiredFingerprint);
        }

        if (!string.Equals(preview.RequestFingerprint, expected.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                "提交合并审查记录的输入和最近一次合并预览不一致，已阻断写入。请重新生成合并预览，再提交审查记录。",
                2,
                "dryRunFingerprint=" + preview.RequestFingerprint + "\nsubmitFingerprint=" + expected.Fingerprint + "\nsourceBranch=" + expected.SourceBranch + "\ntargetBranch=" + expected.TargetBranch + "\ntables=" + expected.TableIdsText);
        }

        request.MergeReview.RequestFingerprint = expected.Fingerprint;
        request.MergeReview.RequiredPreviewFingerprint = expected.Fingerprint;
        request.MergeReview.PreviewResultPath = fullPreviewPath;
        request.RequiredPreviewFingerprint = expected.Fingerprint;
    }

    private static void BackfillMergeReviewFromPreview(LifecycleContractRequest request, LifecycleContractResult preview, string previewPath)
    {
        request.MergeReview ??= new MergeReviewContract();
        var summary = preview.RequestSummary ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        request.MergeReview.SourceBranch = FirstNonEmpty(request.MergeReview.SourceBranch, SummaryValue(summary, "sourceBranch"), preview.Branch, request.Git.Branch);
        request.MergeReview.TargetBranch = FirstNonEmpty(request.MergeReview.TargetBranch, SummaryValue(summary, "targetBranch"), request.MergeInputs != null ? request.MergeInputs.TargetBranch : "", "main");
        request.MergeReview.PrNumber = FirstNonEmpty(request.MergeReview.PrNumber, SummaryValue(summary, "prNumber"));
        request.MergeReview.PrUrl = FirstNonEmpty(request.MergeReview.PrUrl, SummaryValue(summary, "prUrl"));
        request.MergeReview.MergeReportPath = FirstNonEmpty(request.MergeReview.MergeReportPath, SummaryValue(summary, "mergeReportPath"));
        request.MergeReview.MergedPath = FirstNonEmpty(request.MergeReview.MergedPath, SummaryValue(summary, "mergedPath"));
        request.MergeReview.PreviewResultPath = FirstNonEmpty(request.MergeReview.PreviewResultPath, previewPath);
        if (request.MergeReview.TableIds.Count == 0)
        {
            foreach (var tableId in SplitCsv(SummaryValue(summary, "tableIds")))
            {
                request.MergeReview.TableIds.Add(tableId);
            }
        }
    }

    private static string SummaryValue(Dictionary<string, string> summary, string key)
    {
        return summary != null && summary.TryGetValue(key, out var value) ? value : "";
    }

    private static IEnumerable<string> SplitCsv(string csv)
    {
        foreach (var value in (csv ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = value.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private static async Task HydrateSyncCacheRequestFromRegistryAsync(LifecycleContractRequest request, ParsedArgs args, string selectedTable)
    {
        if (request == null || request.Registry == null || string.IsNullOrWhiteSpace(request.Registry.BaseToken))
        {
            return;
        }

        var gateway = new LarkCliGateway(args.Get("lark-cli", "lark-cli"));
        var snapshot = await LoadRegistrySnapshotFromLarkAsync(gateway, request.Registry.BaseToken, request.Locale, args);
        HydrateSyncCacheRequestFromRegistrySnapshot(request, snapshot, selectedTable);
    }

    private static async Task HydrateCompareMergeRequestFromRegistryAsync(LifecycleContractRequest request, ParsedArgs args, string selectedTable)
    {
        if (request == null || request.Registry == null || string.IsNullOrWhiteSpace(request.Registry.BaseToken))
        {
            return;
        }

        var gateway = new LarkCliGateway(args.Get("lark-cli", "lark-cli"));
        var snapshot = await LoadRegistrySnapshotFromLarkAsync(gateway, request.Registry.BaseToken, request.Locale, args);
        HydrateCompareMergeRequestFromRegistrySnapshot(request, snapshot, selectedTable);
    }

    private static async Task HydratePrGateReportFromRegistryAsync(LifecycleContractRequest request, ParsedArgs args)
    {
        if (request == null || request.Registry == null || string.IsNullOrWhiteSpace(request.Registry.BaseToken))
        {
            return;
        }

        try
        {
            var gateway = new LarkCliGateway(args.Get("lark-cli", "lark-cli"));
            var snapshot = await LoadRegistrySnapshotFromLarkAsync(gateway, request.Registry.BaseToken, request.Locale, args);
            HydratePrGateReportFromRegistrySnapshot(request, snapshot);
        }
        catch (CliException ex)
        {
            request.GateReport ??= new PrGateReport();
            request.GateReport.Permissions ??= new GatePermissions();
            request.GateReport.Permissions.CanReadRegistry = false;
            request.GateReport.Permissions.RegistryMessage = "读取在线注册中心失败：" + ex.Message;
        }
    }

    public static void HydratePrGateReportFromRegistrySnapshot(LifecycleContractRequest request, RegistrySnapshot snapshot)
    {
        if (request == null || snapshot == null)
        {
            return;
        }

        request.GateReport ??= new PrGateReport();
        var report = request.GateReport;
        var branch = FirstNonEmpty(report.Branch, request.MergeInputs != null ? request.MergeInputs.SourceBranch : "", request.Git.Branch, request.BranchWorkspace.GitBranch, request.Git.FeishuBranch, request.Git.Profile);
        if (!string.IsNullOrWhiteSpace(branch))
        {
            report.Branch = branch;
        }

        HydrateMergeReviewGateState(request, snapshot, report, branch);
        HydrateSchemaReviewGateState(request, snapshot, report, branch);
        HydrateWaiverGateState(request, snapshot, report, branch);
    }

    private static void HydrateMergeReviewGateState(LifecycleContractRequest request, RegistrySnapshot snapshot, PrGateReport report, string branch)
    {
        var table = FindRegistryTable(snapshot, "MergeReviews", request.Locale);
        if (table == null)
        {
            report.MergeReview.Status = "";
            report.MergeReview.Message = "注册中心没有读到 MergeReviews 表。请先运行 registry-migrate，或检查项目配置表是否包含“合并审查”。";
            return;
        }

        var tableScope = BuildGateTableScope(request, report);
        var matches = table.Records
            .Where(r => !r.IsEmpty)
            .Where(r => string.Equals(GetRegistryRecordValue(r, "GitBranch", request.Locale, request.Registry), branch, StringComparison.OrdinalIgnoreCase))
            .Where(r => TableMatchesGateScope(GetRegistryRecordValue(r, "TableId", request.Locale, request.Registry), tableScope))
            .ToList();

        if (matches.Count == 0)
        {
            report.MergeReview.Status = "";
            report.MergeReview.Message = "缺少 MergeReviews 合并审查记录。请去合并页提交合并审查记录。";
            return;
        }

        var passed = matches.FirstOrDefault(r => PrGateReportEvaluator.ReviewPassed(GetRegistryStatusValue(r, "Status", request.Locale, request.Registry)));
        var selected = passed ?? matches[0];
        report.MergeReview.Status = GetRegistryStatusValue(selected, "Status", request.Locale, request.Registry);
        report.MergeReview.RecordId = selected.RecordId;
        report.MergeReview.ReviewId = GetRegistryRecordValue(selected, "ReviewId", request.Locale, request.Registry);
        report.MergeReview.ApproverRole = FirstNonEmpty(
            GetRegistryRecordValue(selected, "ApproverRole", request.Locale, request.Registry),
            GetRegistryRecordValue(selected, "ApprovedByRole", request.Locale, request.Registry));
        report.MergeReview.GitBranch = GetRegistryRecordValue(selected, "GitBranch", request.Locale, request.Registry);
        report.MergeReview.TableId = GetRegistryRecordValue(selected, "TableId", request.Locale, request.Registry);
        report.MergeReview.Message = PrGateReportEvaluator.ReviewPassed(report.MergeReview.Status)
            ? "已从 MergeReviews 读取到有效合并审查记录。record_id=" + FirstNonEmpty(report.MergeReview.RecordId, "(无 record_id)")
            : "找到了 MergeReviews 记录，但状态不是 approved/completed/passed。请在合并页重新提交或让负责人完成审查。";
    }

    private static void HydrateSchemaReviewGateState(LifecycleContractRequest request, RegistrySnapshot snapshot, PrGateReport report, string branch)
    {
        if (!report.SchemaChangeDetected)
        {
            return;
        }

        var table = FindRegistryTable(snapshot, "SchemaReviews", request.Locale);
        if (table == null)
        {
            report.SchemaReview.Status = "";
            report.SchemaReview.Message = "注册中心没有读到 SchemaReviews 表。请先运行 registry-migrate，或检查项目配置表是否包含“Schema 审查”。";
            return;
        }

        var tableScope = BuildGateTableScope(request, report).Where(t => !IsProjectGateTableId(t)).ToList();
        if (tableScope.Count == 0)
        {
            tableScope.AddRange(report.ChangedTables.Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        var missing = new List<string>();
        var pending = new List<string>();
        var approved = new List<string>();
        foreach (var tableId in tableScope.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matches = table.Records
                .Where(r => !r.IsEmpty)
                .Where(r => string.Equals(GetRegistryRecordValue(r, "TableId", request.Locale, request.Registry), tableId, StringComparison.OrdinalIgnoreCase))
                .Where(r =>
                {
                    var recordBranch = FirstNonEmpty(
                        GetRegistryRecordValue(r, "GitBranch", request.Locale, request.Registry),
                        GetRegistryRecordValue(r, "Branch", request.Locale, request.Registry),
                        GetRegistryRecordValue(r, "Profile", request.Locale, request.Registry));
                    return string.IsNullOrWhiteSpace(recordBranch) || string.Equals(recordBranch, branch, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            if (matches.Count == 0)
            {
                missing.Add(tableId);
                continue;
            }

            var passed = matches.FirstOrDefault(r => PrGateReportEvaluator.ReviewPassed(GetRegistryStatusValue(r, "Status", request.Locale, request.Registry)));
            if (passed == null)
            {
                pending.Add(tableId);
            }
            else
            {
                approved.Add(tableId + ":" + FirstNonEmpty(passed.RecordId, "(无 record_id)"));
                report.SchemaReview.RecordId = FirstNonEmpty(report.SchemaReview.RecordId, passed.RecordId);
                report.SchemaReview.Status = FirstNonEmpty(report.SchemaReview.Status, GetRegistryStatusValue(passed, "Status", request.Locale, request.Registry));
            }
        }

        if (missing.Count == 0 && pending.Count == 0)
        {
            report.SchemaReview.Status = FirstNonEmpty(report.SchemaReview.Status, "approved");
            report.SchemaReview.Message = "已从 SchemaReviews 读取到 schema 审查通过记录：" + string.Join(", ", approved);
        }
        else
        {
            report.SchemaReview.Status = "";
            report.SchemaReview.Message = "Schema 审查未完成。缺少：" + string.Join(", ", missing) + "；待处理：" + string.Join(", ", pending) + "。请去 PR 检查页处理 Schema 审查。";
        }
    }

    private static void HydrateWaiverGateState(LifecycleContractRequest request, RegistrySnapshot snapshot, PrGateReport report, string branch)
    {
        var table = FindRegistryTable(snapshot, "Waivers", request.Locale);
        if (table == null)
        {
            return;
        }

        var tableScope = BuildGateTableScope(request, report);
        var matches = table.Records
            .Where(r => !r.IsEmpty)
            .Where(r =>
            {
                var waiverBranch = FirstNonEmpty(
                    GetRegistryRecordValue(r, "GitBranch", request.Locale, request.Registry),
                    GetRegistryRecordValue(r, "Branch", request.Locale, request.Registry));
                return string.IsNullOrWhiteSpace(waiverBranch) || string.Equals(waiverBranch, branch, StringComparison.OrdinalIgnoreCase);
            })
            .Where(r => TableMatchesGateScope(GetRegistryRecordValue(r, "TableId", request.Locale, request.Registry), tableScope))
            .ToList();
        if (matches.Count == 0)
        {
            return;
        }

        var selected = matches
            .OrderByDescending(r => GetRegistryRecordValue(r, "ExpiresAt", request.Locale, request.Registry), StringComparer.OrdinalIgnoreCase)
            .First();
        report.Waiver.RecordId = selected.RecordId;
        report.Waiver.Status = GetRegistryStatusValue(selected, "Status", request.Locale, request.Registry);
        report.Waiver.ApprovedByRole = FirstNonEmpty(
            GetRegistryRecordValue(selected, "ApprovedByRole", request.Locale, request.Registry),
            GetRegistryRecordValue(selected, "ApproverRole", request.Locale, request.Registry));
        report.Waiver.ExpiresAt = GetRegistryRecordValue(selected, "ExpiresAt", request.Locale, request.Registry);
        report.Waiver.Branch = FirstNonEmpty(
            GetRegistryRecordValue(selected, "GitBranch", request.Locale, request.Registry),
            GetRegistryRecordValue(selected, "Branch", request.Locale, request.Registry));
        report.Waiver.TableId = GetRegistryRecordValue(selected, "TableId", request.Locale, request.Registry);
        report.Waiver.Reason = FirstNonEmpty(
            GetRegistryRecordValue(selected, "Reason", request.Locale, request.Registry),
            GetRegistryRecordValue(selected, "原因", request.Locale, request.Registry));
        report.Waiver.Approved = PrGateReportEvaluator.ReviewPassed(report.Waiver.Status) ||
                                 (string.IsNullOrWhiteSpace(report.Waiver.Status) && string.Equals(report.Waiver.ApprovedByRole, "configOwner", StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> BuildGateTableScope(LifecycleContractRequest request, PrGateReport report)
    {
        var scope = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableId in report.ChangedTables ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(tableId))
            {
                scope.Add(tableId);
            }
        }

        if (request.MergeReview != null)
        {
            foreach (var tableId in request.MergeReview.TableIds ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(tableId))
                {
                    scope.Add(tableId);
                }
            }
        }

        if (request.MergeInputs != null && !string.IsNullOrWhiteSpace(request.MergeInputs.TableId))
        {
            scope.Add(request.MergeInputs.TableId);
        }

        if (request.Table != null && !string.IsNullOrWhiteSpace(request.Table.TableId))
        {
            scope.Add(request.Table.TableId);
        }

        scope.Add("__project_pr_gate__");
        scope.Add("project-config");
        return scope.ToList();
    }

    private static bool TableMatchesGateScope(string recordTableId, IList<string> tableScope)
    {
        if (string.IsNullOrWhiteSpace(recordTableId))
        {
            return false;
        }

        if (IsProjectGateTableId(recordTableId))
        {
            return true;
        }

        return tableScope.Any(t => string.Equals(t, recordTableId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProjectGateTableId(string tableId)
    {
        return string.Equals(tableId, "__project_pr_gate__", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tableId, "project-config", StringComparison.OrdinalIgnoreCase);
    }

    public static void HydrateCompareMergeRequestFromRegistrySnapshot(LifecycleContractRequest request, RegistrySnapshot snapshot, string selectedTable)
    {
        if (request == null || snapshot == null)
        {
            return;
        }

        request.MergeInputs ??= new MergeInputsContract();
        HydrateSyncCacheRequestFromRegistrySnapshot(request, snapshot, selectedTable);

        var originalGit = request.Git ?? new ContractGitSpec();
        var originalWorkspace = request.BranchWorkspace ?? new BranchWorkspaceContract();
        var sourceGit = new ContractGitSpec
        {
            Branch = originalGit.Branch,
            FeishuBranch = originalGit.FeishuBranch,
            Profile = originalGit.Profile,
            Head = originalGit.Head
        };
        var sourceWorkspace = new BranchWorkspaceContract
        {
            Mode = originalWorkspace.Mode,
            RootWikiToken = originalWorkspace.RootWikiToken,
            RootWikiUrl = originalWorkspace.RootWikiUrl,
            RootWikiTitle = originalWorkspace.RootWikiTitle,
            GitBranch = originalWorkspace.GitBranch,
            FeishuBranch = originalWorkspace.FeishuBranch,
            Profile = originalWorkspace.Profile,
            MainGitBranch = originalWorkspace.MainGitBranch,
            MainFeishuBranch = originalWorkspace.MainFeishuBranch,
            ProfileNameTemplate = originalWorkspace.ProfileNameTemplate,
            BranchNodeTitleTemplate = originalWorkspace.BranchNodeTitleTemplate,
            MainNodeTitle = originalWorkspace.MainNodeTitle,
            CreateIfMissing = originalWorkspace.CreateIfMissing,
            RequireOneToOneBinding = originalWorkspace.RequireOneToOneBinding,
            BindingRegistryTable = originalWorkspace.BindingRegistryTable,
            OwnerRole = originalWorkspace.OwnerRole,
            CreatedBy = originalWorkspace.CreatedBy,
            ExistingWikiNodeToken = originalWorkspace.ExistingWikiNodeToken,
            ExistingWikiNodeUrl = originalWorkspace.ExistingWikiNodeUrl
        };

        try
        {
            var normalized = BranchWorkspaceResolver.NormalizeContract(request);
            var targetBranch = FirstNonEmpty(request.MergeInputs.TargetBranch, normalized.MainGitBranch, "main");
            var targetProfile = FirstNonEmpty(
                request.MergeInputs.TargetFeishuProfile,
                string.Equals(targetBranch, FirstNonEmpty(normalized.MainGitBranch, "main"), StringComparison.OrdinalIgnoreCase)
                    ? FirstNonEmpty(normalized.MainFeishuBranch, "main")
                    : "");
            request.Git = new ContractGitSpec
            {
                Branch = targetBranch,
                FeishuBranch = targetProfile,
                Profile = targetProfile,
                Head = sourceGit.Head
            };
            request.BranchWorkspace = new BranchWorkspaceContract
            {
                Mode = normalized.Mode,
                RootWikiToken = normalized.RootWikiToken,
                RootWikiUrl = normalized.RootWikiUrl,
                RootWikiTitle = normalized.RootWikiTitle,
                GitBranch = targetBranch,
                FeishuBranch = targetProfile,
                Profile = targetProfile,
                MainGitBranch = normalized.MainGitBranch,
                MainFeishuBranch = normalized.MainFeishuBranch,
                ProfileNameTemplate = normalized.ProfileNameTemplate,
                BranchNodeTitleTemplate = normalized.BranchNodeTitleTemplate,
                MainNodeTitle = normalized.MainNodeTitle,
                CreateIfMissing = normalized.CreateIfMissing,
                RequireOneToOneBinding = normalized.RequireOneToOneBinding,
                BindingRegistryTable = normalized.BindingRegistryTable,
                OwnerRole = normalized.OwnerRole,
                CreatedBy = normalized.CreatedBy,
                ExistingWikiNodeToken = request.MergeInputs.TargetBranchWikiNodeToken,
                ExistingWikiNodeUrl = request.MergeInputs.TargetBranchWikiNodeUrl
            };
            HydrateSyncCacheRequestFromRegistrySnapshot(request, snapshot, selectedTable);
        }
        finally
        {
            request.Git = sourceGit;
            request.BranchWorkspace = sourceWorkspace;
        }
    }

    public static void HydrateSyncCacheRequestFromRegistrySnapshot(LifecycleContractRequest request, RegistrySnapshot snapshot, string selectedTable)
    {
        if (request == null || snapshot == null)
        {
            return;
        }

        request.BranchBindings ??= new List<BranchBindingContract>();
        request.Registry ??= new RegistryContract();
        request.SeedFromLocalXlsx ??= new SeedFromLocalXlsxContract();
        var resolution = BranchWorkspaceResolver.Resolve(request);
        var effectiveProfile = FirstNonEmpty(resolution.Profile, resolution.FeishuBranch);
        var branchTable = FindRegistryTable(snapshot, "BranchBindings", request.Locale);
        if (branchTable != null)
        {
            var matches = branchTable.Records
                .Where(r => !r.IsEmpty)
                .Where(r => string.Equals(GetRegistryRecordValue(r, "GitBranch", request.Locale, request.Registry), resolution.GitBranch, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(FirstNonEmpty(GetRegistryRecordValue(r, "Profile", request.Locale, request.Registry), GetRegistryRecordValue(r, "FeishuBranch", request.Locale, request.Registry)), effectiveProfile, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var match in matches)
            {
                var binding = new BranchBindingContract
                {
                    RecordId = match.RecordId,
                    GitBranch = GetRegistryRecordValue(match, "GitBranch", request.Locale, request.Registry),
                    FeishuBranch = GetRegistryRecordValue(match, "FeishuBranch", request.Locale, request.Registry),
                    Profile = GetRegistryRecordValue(match, "Profile", request.Locale, request.Registry),
                    WikiNodeToken = GetRegistryRecordValue(match, "WikiNodeToken", request.Locale, request.Registry),
                    WikiNodeUrl = GetRegistryRecordValue(match, "WikiNodeUrl", request.Locale, request.Registry),
                    Status = GetRegistryRecordValue(match, "Status", request.Locale, request.Registry),
                    OwnerRole = GetRegistryRecordValue(match, "OwnerRole", request.Locale, request.Registry),
                    CreatedBy = GetRegistryRecordValue(match, "CreatedBy", request.Locale, request.Registry),
                    CreatedAt = GetRegistryRecordValue(match, "CreatedAt", request.Locale, request.Registry),
                    UpdatedAt = GetRegistryRecordValue(match, "UpdatedAt", request.Locale, request.Registry)
                };
                if (matches.Count > 1 || !request.BranchBindings.Any(b => SameBranchBindingIdentity(b, binding)))
                {
                    request.BranchBindings.Add(binding);
                }
            }
        }

        var configTable = FindRegistryTable(snapshot, "ConfigSheets", request.Locale);
        if (configTable == null)
        {
            return;
        }

        var sheetRows = configTable.Records
            .Where(r => !r.IsEmpty)
            .Select(r => new
            {
                Record = r,
                TableId = GetRegistryRecordValue(r, "TableId", request.Locale, request.Registry),
                Branch = FirstNonEmpty(
                    GetRegistryRecordValue(r, "Branch", request.Locale, request.Registry),
                    GetRegistryRecordValue(r, "Profile", request.Locale, request.Registry),
                    GetRegistryRecordValue(r, "FeishuBranch", request.Locale, request.Registry))
            })
            .Where(r => !string.IsNullOrWhiteSpace(r.TableId) &&
                        string.Equals(r.Branch, effectiveProfile, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(selectedTable) || string.Equals(r.TableId, selectedTable, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var duplicateGroups = sheetRows
            .GroupBy(r => r.TableId + "\n" + r.Branch, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicateGroups.Count > 0)
        {
            var group = duplicateGroups[0];
            var first = group.First();
            var recordIds = string.Join(", ", group.Select(v => v.Record.RecordId).Where(v => !string.IsNullOrWhiteSpace(v)));
            throw new CliException("ConfigSheets 中 TableId “" + first.TableId + "” + Branch/Profile “" + first.Branch + "” 存在 " + group.Count().ToString(CultureInfo.InvariantCulture) + " 条重复记录（record_id: " + recordIds + "）。请先运行 registry-migrate --dry-run 查看 cleanup/migrate 计划，确认后清理重复行。", 2);
        }

        foreach (var row in sheetRows)
        {
            UpsertSeedTableFromRegistryRecord(request.SeedFromLocalXlsx, row.Record, request.Locale, request.Registry);
        }
    }

    private static bool SameBranchBindingIdentity(BranchBindingContract left, BranchBindingContract right)
    {
        if (!string.IsNullOrWhiteSpace(left.RecordId) && !string.IsNullOrWhiteSpace(right.RecordId))
        {
            return string.Equals(left.RecordId, right.RecordId, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(left.GitBranch, right.GitBranch, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(FirstNonEmpty(left.Profile, left.FeishuBranch), FirstNonEmpty(right.Profile, right.FeishuBranch), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.WikiNodeToken, right.WikiNodeToken, StringComparison.OrdinalIgnoreCase);
    }

    private static void UpsertSeedTableFromRegistryRecord(SeedFromLocalXlsxContract seed, RegistryRecordSnapshot record, string locale, RegistryContract registry)
    {
        var tableId = GetRegistryRecordValue(record, "TableId", locale, registry);
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return;
        }

        var recordProfile = FirstNonEmpty(
            GetRegistryRecordValue(record, "Profile", locale, registry),
            GetRegistryRecordValue(record, "Branch", locale, registry),
            GetRegistryRecordValue(record, "FeishuBranch", locale, registry));
        var table = seed.Tables.FirstOrDefault(t =>
            string.Equals(t.TableId, tableId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(FirstNonEmpty(t.Profile, t.Branch), recordProfile, StringComparison.OrdinalIgnoreCase));
        if (table == null)
        {
            table = seed.Tables.FirstOrDefault(t =>
                string.Equals(t.TableId, tableId, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(FirstNonEmpty(t.Profile, t.Branch)));
        }

        if (table == null)
        {
            table = new SeedTableContract { TableId = tableId };
            seed.Tables.Add(table);
        }

        table.RegistryRecordId = FirstNonEmpty(table.RegistryRecordId, record.RecordId);
        table.DisplayName = FirstNonEmpty(table.DisplayName, GetRegistryRecordValue(record, "DisplayName", locale, registry), table.TableId);
        table.CacheXlsxPath = FirstNonEmpty(table.CacheXlsxPath, GetRegistryRecordValue(record, "ExcelPath", locale, registry));
        table.SpreadsheetToken = FirstNonEmpty(GetRegistryRecordValue(record, "SpreadsheetToken", locale, registry), table.SpreadsheetToken);
        table.SpreadsheetUrl = FirstNonEmpty(GetRegistryRecordValue(record, "OnlineSheetUrl", locale, registry), table.SpreadsheetUrl);
        table.SheetId = FirstNonEmpty(GetRegistryRecordValue(record, "SheetId", locale, registry), table.SheetId);
        table.WikiNodeToken = FirstNonEmpty(GetRegistryRecordValue(record, "WikiNodeToken", locale, registry), table.WikiNodeToken);
        table.WikiNodeUrl = FirstNonEmpty(GetRegistryRecordValue(record, "WikiNodeUrl", locale, registry), table.WikiNodeUrl);
        table.Branch = FirstNonEmpty(GetRegistryRecordValue(record, "Branch", locale, registry), GetRegistryRecordValue(record, "FeishuBranch", locale, registry), table.Branch);
        table.Profile = FirstNonEmpty(GetRegistryRecordValue(record, "Profile", locale, registry), table.Profile);
        table.SemanticHash = FirstNonEmpty(GetRegistryRecordValue(record, "SemanticHash", locale, registry), table.SemanticHash);
        table.OwnerRole = FirstNonEmpty(table.OwnerRole, GetRegistryRecordValue(record, "OwnerRole", locale, registry));
        var reviewRequired = GetRegistryRecordValue(record, "SchemaReviewRequired", locale, registry);
        if (!string.IsNullOrWhiteSpace(reviewRequired))
        {
            table.SchemaReviewRequired = !(reviewRequired.Equals("否", StringComparison.OrdinalIgnoreCase) ||
                                           reviewRequired.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                                           reviewRequired.Equals("0", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task<LifecycleContractRequest> BuildSeedRequestAsync(Workspace workspace, ParsedArgs args)
    {
        if (args.TryGet("manifest", out var manifestPath))
        {
            return await ReadSeedManifestAsync(workspace, manifestPath, args);
        }

        var tableId = args.Get("table", "");
        var sourceXlsx = args.Get("source-xlsx", args.Get("xlsx", ""));
        if (string.IsNullOrWhiteSpace(tableId))
        {
            throw new CliException("seed-from-xlsx needs --table <id>, or use --all --manifest <project-config-or-contract>.", 2);
        }

        if (string.IsNullOrWhiteSpace(sourceXlsx))
        {
            throw new CliException("seed-from-xlsx needs --source-xlsx <path> for single-table mode.", 2);
        }

        var registered = workspace.Registry.Tables.FirstOrDefault(t => string.Equals(t.Id, tableId, StringComparison.OrdinalIgnoreCase));
        var request = NewSeedRequestFromWorkspace(workspace, args);
        request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract
        {
            TableId = tableId,
            DisplayName = args.Get("name", registered?.Name ?? tableId),
            SourceXlsxPath = sourceXlsx,
            CacheXlsxPath = args.Get("cache-xlsx", Path.Combine(request.SeedFromLocalXlsx.ExcelCacheDirectory, tableId + ".xlsx")),
            SemanticCachePath = args.Get("semantic-cache", Path.Combine(request.SeedFromLocalXlsx.CacheDirectory, tableId + ".semantic.json")),
            HashCachePath = args.Get("hash-cache", Path.Combine(request.SeedFromLocalXlsx.CacheDirectory, tableId + ".sha256")),
            ProjectConfigPath = args.Get("project-config", request.SeedFromLocalXlsx.ProjectConfigPath),
            SpreadsheetToken = args.Get("spreadsheet", registered?.Spreadsheet ?? ""),
            SpreadsheetUrl = args.Get("url", ""),
            SheetId = args.Get("sheet-id", registered?.SheetId ?? ""),
            SheetName = args.Get("sheet-name", args.Get("sheet", tableId)),
            WikiRootToken = args.Get("wiki-root", request.SeedFromLocalXlsx.WikiRootToken),
            OwnerRole = args.Get("owner-role", ""),
            FieldRow = args.GetInt("field-row", registered?.FieldRow ?? 0),
            TypeRow = args.GetInt("type-row", registered?.TypeRow ?? -1),
            DescriptionRow = args.GetInt("description-row", registered?.DescriptionRow ?? -1),
            DataStartRow = args.GetInt("data-start-row", registered?.DataStartRow ?? -1),
            TreatUnknownTypesAsEnum = args.GetBool("treat-unknown-types-as-enum", registered?.TreatUnknownTypesAsEnum ?? false),
            UnityExcelToSo = new UnityExcelToSoContract
            {
                SettingsPath = args.Get("excel-to-so-settings", ""),
                TableId = tableId,
                ExcelPath = args.Get("excel-to-so-cache-path", Path.Combine(request.SeedFromLocalXlsx.ExcelCacheDirectory, tableId + ".xlsx")),
                ScriptableObjectType = args.Get("scriptable-object-type", ""),
                AssetPath = args.Get("asset-path", "")
            }
        });

        return request;
    }

    private static LifecycleContractRequest NewSeedRequestFromWorkspace(Workspace workspace, ParsedArgs args)
    {
        var cacheDirectory = args.Get("cache-dir", workspace.Paths.CacheDirectory);
        var excelCacheDirectory = args.Get("excel-cache-dir", Path.Combine(workspace.Paths.StateDirectory, "excel-cache"));
        var request = new LifecycleContractRequest
        {
            Operation = "seed-from-local-xlsx",
            Locale = args.Get("locale", "zh-Hans"),
            Registry = new RegistryContract
            {
                BaseToken = args.Get("base", ""),
                BaseUrl = args.Get("base-url", "")
            },
            Git = new ContractGitSpec
            {
                Branch = FirstNonEmpty(args.Get("branch", ""), TryRunGitAsync("branch", "--show-current").GetAwaiter().GetResult()),
                Head = FirstNonEmpty(args.Get("git-head", ""), TryRunGitAsync("rev-parse", "HEAD").GetAwaiter().GetResult()),
                FeishuBranch = args.Get("feishu-branch", ""),
                Profile = args.Get("profile", "")
            },
            SeedFromLocalXlsx = new SeedFromLocalXlsxContract
            {
                CacheDirectory = cacheDirectory,
                ExcelCacheDirectory = excelCacheDirectory,
                ProjectConfigPath = args.Get("project-config", ""),
                WikiRootToken = FirstNonEmpty(args.Get("wiki-root", ""), workspace.Config.RootToken),
                WikiParentTitle = args.Get("wiki-parent-title", "项目配置表"),
                BaselineStrategy = args.Get("baseline-strategy", "pending"),
                PreferDriveImport = !args.HasFlag("no-drive-import"),
                CleanupDefaultRows = args.HasFlag("cleanup-default-rows")
            }
        };

        request.BranchWorkspace = new BranchWorkspaceContract
        {
            Mode = args.Get("branch-binding-mode", "git-branch-to-feishu-branch-profile"),
            RootWikiToken = request.SeedFromLocalXlsx.WikiRootToken,
            RootWikiUrl = FirstNonEmpty(args.Get("branch-workspace-root-wiki-url", ""), args.Get("root-wiki-url", ""), args.Get("wiki-root-url", ""), workspace.Config.RootUrl),
            RootWikiTitle = request.SeedFromLocalXlsx.WikiParentTitle,
            GitBranch = request.Git.Branch,
            FeishuBranch = request.Git.FeishuBranch,
            Profile = request.Git.Profile,
            MainGitBranch = args.Get("main-git-branch", "main"),
            MainFeishuBranch = args.Get("main-feishu-branch", "main"),
            ProfileNameTemplate = args.Get("profile-name-template", "{gitBranch}"),
            BranchNodeTitleTemplate = args.Get("branch-node-title-template", "branch-{slug}"),
            MainNodeTitle = args.Get("main-node-title", "main"),
            CreateIfMissing = !args.HasFlag("no-create-branch-node"),
            RequireOneToOneBinding = !args.HasFlag("no-one-to-one-binding"),
            BindingRegistryTable = args.Get("binding-registry-table", "BranchBindings"),
            OwnerRole = args.Get("owner-role", "")
        };

        return request;
    }

    private static async Task<LifecycleContractRequest> ReadSeedManifestAsync(Workspace workspace, string manifestPath, ParsedArgs args)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            throw new CliException("The seed manifest does not exist.", 2, manifestPath);
        }

        var json = StripUtf8Bom(await File.ReadAllTextAsync(manifestPath, Utf8NoBom));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var operation = GetJsonString(root, "operation");
        if ((SeedOperationRequested(operation) || root.TryGetProperty("seedFromLocalXlsx", out _)) &&
            !root.TryGetProperty("seedTables", out _))
        {
            var request = JsonSerializer.Deserialize<LifecycleContractRequest>(json, JsonOptions);
            if (request == null)
            {
                throw new CliException("Could not read seed lifecycle contract.", 2, manifestPath);
            }

            return request;
        }

        var seedRequest = NewSeedRequestFromWorkspace(workspace, args);
        seedRequest.SeedFromLocalXlsx.ProjectConfigPath = Path.GetFullPath(manifestPath);
        seedRequest.Registry.BaseToken = FirstNonEmpty(seedRequest.Registry.BaseToken, FindStringDeep(root, "baseToken", "registryBaseToken"));
        seedRequest.Registry.BaseUrl = FirstNonEmpty(seedRequest.Registry.BaseUrl, FindStringDeep(root, "baseUrl", "registryBaseUrl"));
        MergeRegistryTableIds(seedRequest.Registry.TableIds, root);
        seedRequest.SeedFromLocalXlsx.WikiRootToken = FirstNonEmpty(seedRequest.SeedFromLocalXlsx.WikiRootToken, FindStringDeep(root, "wikiRootToken", "feishuRootToken", "rootToken"));
        seedRequest.SeedFromLocalXlsx.WikiParentTitle = FirstNonEmpty(FindStringDeep(root, "wikiRootTitle", "rootWikiTitle", "wikiParentTitle"), seedRequest.SeedFromLocalXlsx.WikiParentTitle);
        seedRequest.SeedFromLocalXlsx.CacheDirectory = FirstNonEmpty(FindStringDeep(root, "semanticCacheDirectory", "cacheDirectory"), seedRequest.SeedFromLocalXlsx.CacheDirectory);
        seedRequest.SeedFromLocalXlsx.ExcelCacheDirectory = FirstNonEmpty(FindStringDeep(root, "excelCacheDirectory", "xlsxCacheDirectory"), seedRequest.SeedFromLocalXlsx.ExcelCacheDirectory);
        seedRequest.SeedFromLocalXlsx.BaselineStrategy = FirstNonEmpty(FindStringDeep(root, "baselineStrategy", "schemaReviewBaselineStrategy"), seedRequest.SeedFromLocalXlsx.BaselineStrategy);
        seedRequest.BranchWorkspace.Mode = FirstNonEmpty(FindStringDeep(root, "branchBindingMode", "mode"), seedRequest.BranchWorkspace.Mode);
        seedRequest.BranchWorkspace.RootWikiToken = FirstNonEmpty(FindStringDeep(root, "branchWorkspaceRootWikiToken", "wikiRootToken", "feishuRootToken", "rootToken"), seedRequest.SeedFromLocalXlsx.WikiRootToken);
        seedRequest.BranchWorkspace.RootWikiUrl = FirstNonEmpty(FindStringDeep(root, "branchWorkspaceRootWikiUrl", "rootWikiUrl", "wikiRootUrl"), seedRequest.BranchWorkspace.RootWikiUrl);
        seedRequest.BranchWorkspace.RootWikiTitle = FirstNonEmpty(FindStringDeep(root, "branchWorkspaceRootWikiTitle", "wikiRootTitle", "rootWikiTitle", "wikiParentTitle"), seedRequest.SeedFromLocalXlsx.WikiParentTitle);
        seedRequest.BranchWorkspace.GitBranch = FirstNonEmpty(FindStringDeep(root, "gitBranch", "currentGitBranch"), seedRequest.Git.Branch);
        seedRequest.BranchWorkspace.FeishuBranch = FirstNonEmpty(FindStringDeep(root, "feishuBranch", "larkBranch"), seedRequest.Git.FeishuBranch);
        seedRequest.BranchWorkspace.Profile = FirstNonEmpty(FindStringDeep(root, "profile", "feishuProfile", "larkProfile"), seedRequest.Git.Profile);
        seedRequest.BranchWorkspace.MainGitBranch = FirstNonEmpty(FindStringDeep(root, "mainGitBranch"), seedRequest.BranchWorkspace.MainGitBranch);
        seedRequest.BranchWorkspace.MainFeishuBranch = FirstNonEmpty(FindStringDeep(root, "mainFeishuBranch"), seedRequest.BranchWorkspace.MainFeishuBranch);
        seedRequest.BranchWorkspace.ProfileNameTemplate = FirstNonEmpty(FindStringDeep(root, "profileNameTemplate"), seedRequest.BranchWorkspace.ProfileNameTemplate);
        seedRequest.BranchWorkspace.BranchNodeTitleTemplate = FirstNonEmpty(FindStringDeep(root, "branchNodeTitleTemplate"), seedRequest.BranchWorkspace.BranchNodeTitleTemplate);
        seedRequest.BranchWorkspace.MainNodeTitle = FirstNonEmpty(FindStringDeep(root, "mainNodeTitle"), seedRequest.BranchWorkspace.MainNodeTitle);
        seedRequest.BranchWorkspace.BindingRegistryTable = FirstNonEmpty(FindStringDeep(root, "bindingRegistryTable"), seedRequest.BranchWorkspace.BindingRegistryTable);
        seedRequest.BranchWorkspace.RequireOneToOneBinding = FindBoolDeep(root, "requireOneToOneBinding", seedRequest.BranchWorkspace.RequireOneToOneBinding);
        seedRequest.BranchWorkspace.CreateIfMissing = FindBoolDeep(root, "createIfMissing", seedRequest.BranchWorkspace.CreateIfMissing);
        seedRequest.Git.Branch = FirstNonEmpty(seedRequest.BranchWorkspace.GitBranch, seedRequest.Git.Branch);
        seedRequest.Git.FeishuBranch = FirstNonEmpty(seedRequest.BranchWorkspace.FeishuBranch, seedRequest.Git.FeishuBranch);
        seedRequest.Git.Profile = FirstNonEmpty(seedRequest.BranchWorkspace.Profile, seedRequest.Git.Profile);
        seedRequest.BranchBindings.AddRange(ParseBranchBindings(root));
        var defaultExcelToSoSettings = FindStringDeep(root, "excelToSoSettingsPath", "excelToScriptableObjectSettingsPath");

        foreach (var tableElement in FindTableArray(root))
        {
            if (tableElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var tableId = FirstNonEmpty(GetJsonString(tableElement, "tableId", "id", "key"), "");
            if (string.IsNullOrWhiteSpace(tableId))
            {
                continue;
            }

            if (!args.HasFlag("all") && args.TryGet("table", out var selected) && !string.Equals(selected, tableId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cacheXlsx = FirstNonEmpty(GetJsonString(tableElement, "cacheXlsxPath", "excelCachePath", "localCachePath", "cachePath"), Path.Combine(seedRequest.SeedFromLocalXlsx.ExcelCacheDirectory, tableId + ".xlsx"));
            var feishuElement = GetJsonProperty(tableElement, "feishu", "lark");
            var table = new SeedTableContract
            {
                TableId = tableId,
                DisplayName = FirstNonEmpty(GetJsonString(tableElement, "displayName", "name", "title"), tableId),
                SourceXlsxPath = FirstNonEmpty(GetJsonString(tableElement, "sourceXlsxPath", "sourceXlsx", "oldExcelPath", "localSourcePath"), ""),
                CacheXlsxPath = cacheXlsx,
                SemanticCachePath = FirstNonEmpty(GetJsonString(tableElement, "semanticCachePath"), Path.Combine(seedRequest.SeedFromLocalXlsx.CacheDirectory, tableId + ".semantic.json")),
                HashCachePath = FirstNonEmpty(GetJsonString(tableElement, "hashCachePath", "sha256Path"), Path.Combine(seedRequest.SeedFromLocalXlsx.CacheDirectory, tableId + ".sha256")),
                ProjectConfigPath = Path.GetFullPath(manifestPath),
                SpreadsheetToken = FirstNonEmpty(GetJsonString(feishuElement, "spreadsheetToken", "spreadsheet"), GetJsonString(tableElement, "spreadsheetToken", "spreadsheet")),
                SpreadsheetUrl = FirstNonEmpty(GetJsonString(feishuElement, "spreadsheetUrl", "url", "onlineSheetUrl"), GetJsonString(tableElement, "spreadsheetUrl", "url", "onlineSheetUrl")),
                SheetId = FirstNonEmpty(GetJsonString(feishuElement, "sheetId"), GetJsonString(tableElement, "sheetId")),
                SheetName = FirstNonEmpty(GetJsonString(tableElement, "sheetName"), tableId),
                WikiRootToken = FirstNonEmpty(GetJsonString(feishuElement, "wikiRootToken"), GetJsonString(tableElement, "wikiRootToken"), seedRequest.SeedFromLocalXlsx.WikiRootToken),
                WikiNodeUrl = FirstNonEmpty(GetJsonString(feishuElement, "wikiNodeUrl", "branchWikiNodeUrl"), GetJsonString(tableElement, "wikiNodeUrl", "branchWikiNodeUrl")),
                Branch = FirstNonEmpty(GetJsonString(feishuElement, "branch", "feishuBranch"), GetJsonString(tableElement, "branch", "feishuBranch")),
                Profile = FirstNonEmpty(GetJsonString(feishuElement, "profile", "feishuProfile"), GetJsonString(tableElement, "profile", "feishuProfile")),
                OwnerRole = GetJsonString(tableElement, "ownerRole"),
                RegistryRecordId = GetJsonString(tableElement, "registryRecordId"),
                SchemaReviewRequired = GetJsonBool(tableElement, true, "schemaReviewRequired"),
                FieldRow = GetJsonInt(tableElement, 0, "fieldRow"),
                TypeRow = GetJsonInt(tableElement, -1, "typeRow"),
                DescriptionRow = GetJsonInt(tableElement, -1, "descriptionRow"),
                DataStartRow = GetJsonInt(tableElement, -1, "dataStartRow"),
                TreatUnknownTypesAsEnum = GetJsonBool(tableElement, false, "treatUnknownTypesAsEnum"),
                UnityExcelToSo = new UnityExcelToSoContract
                {
                    SettingsPath = FirstNonEmpty(GetJsonString(tableElement, "excelToSoSettingsPath", "excelToScriptableObjectSettingsPath"), defaultExcelToSoSettings),
                    TableId = tableId,
                    ExcelPath = FirstNonEmpty(GetJsonString(tableElement, "excelToSoCachePath"), cacheXlsx),
                    ScriptableObjectType = GetJsonString(tableElement, "scriptableObjectType"),
                    AssetPath = GetJsonString(tableElement, "assetPath")
                }
            };

            foreach (var field in ParseFieldSpecs(tableElement))
            {
                table.Fields.Add(field);
            }

            seedRequest.SeedFromLocalXlsx.Tables.Add(table);
        }

        if (seedRequest.SeedFromLocalXlsx.Tables.Count == 0)
        {
            throw new CliException("The seed manifest did not contain any matching tables.", 2, manifestPath);
        }

        return seedRequest;
    }

    private static async Task EmitLifecycleResultAsync(ParsedArgs args, LifecycleContractResult result)
    {
        PrepareLifecycleResultForOutput(result);
        var outPath = args.Get("out", "");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            await WriteJsonAsync(outPath, result);
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
    }

    private static void PrepareLifecycleResultForOutput(LifecycleContractResult result)
    {
        if (result == null)
        {
            return;
        }

        var summary = result.SyncCacheSummary;
        var hasMeaningfulSyncSummary =
            summary != null &&
            (summary.Tables.Count > 0 ||
             summary.ChangedTables.Count > 0 ||
             summary.MissingCacheTables.Count > 0 ||
             summary.UpToDateTables.Count > 0 ||
             summary.BlockedTables.Count > 0 ||
             summary.ResolvedOnlineTables.Count > 0 ||
             !string.IsNullOrWhiteSpace(summary.NextAction) ||
             !string.IsNullOrWhiteSpace(summary.PreviewFingerprint) ||
             (!string.IsNullOrWhiteSpace(summary.CacheStatus) && !string.Equals(summary.CacheStatus, "unknown", StringComparison.OrdinalIgnoreCase)));

        if (SyncCacheOperationRequested(result.Operation) ||
            string.Equals(result.Operation, "sync-status", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.Operation, "repair-cache-dialect", StringComparison.OrdinalIgnoreCase) ||
            hasMeaningfulSyncSummary)
        {
            result.SyncCacheSummary ??= new SyncCacheSummary();
            var previewFingerprint = FirstNonEmpty(result.SyncCacheSummary.PreviewFingerprint, result.PreviewFingerprint, result.RequestFingerprint);
            result.SyncCacheSummary.PreviewFingerprint = previewFingerprint;
            result.PreviewFingerprint = previewFingerprint;
            MirrorSyncCacheSummary(result);
        }
    }

    private static async Task EmitProgressEventAsync(ParsedArgs args, string operation, string phase, string tableId, int current, int total, string message, string severity)
    {
        if (!args.HasFlag("progress-stdout") && !args.TryGet("progress", out var progressPath))
        {
            return;
        }

        var progress = new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["phase"] = phase,
            ["tableId"] = tableId,
            ["current"] = current,
            ["total"] = total,
            ["elapsedMs"] = 0,
            ["message"] = message,
            ["severity"] = severity
        };
        var line = JsonSerializer.Serialize(progress, CompactJsonOptions);
        if (args.HasFlag("progress-stdout"))
        {
            Console.WriteLine(line);
        }

        if (args.TryGet("progress", out progressPath) && !string.IsNullOrWhiteSpace(progressPath))
        {
            try
            {
                var normalizedProgressPath = NormalizeFilePathArgument(progressPath);
                var directory = Path.GetDirectoryName(normalizedProgressPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.AppendAllTextAsync(normalizedProgressPath, line + Environment.NewLine, Utf8NoBom);
            }
            catch (Exception ex) when (IsPathFailure(ex))
            {
                throw new CliException("写入进度文件失败：路径格式不合法。请升级 Desktop/CLI 或重试。", 1, progressPath + Environment.NewLine + ex.Message);
            }
        }
    }

    private static string ComputeRequestFingerprint(object value)
    {
        var json = JsonSerializer.Serialize(value, CompactJsonOptions);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static async Task<int> MergeAsync(ParsedArgs args)
    {
        var basePath = args.Get("base", "");
        var oursPath = args.Get("ours", "");
        var theirsPath = args.Get("theirs", "");
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(oursPath) || string.IsNullOrWhiteSpace(theirsPath))
        {
            throw new CliException("merge needs --base, --ours, and --theirs semantic workbook JSON files.", 2);
        }

        var baseWorkbook = await ReadJsonAsync<WorkbookDocument>(basePath);
        var ours = await ReadJsonAsync<WorkbookDocument>(oursPath);
        var theirs = await ReadJsonAsync<WorkbookDocument>(theirsPath);
        var report = ThreeWayMerger.Merge(baseWorkbook, ours, theirs);

        var reportPath = args.Get("out", "merge-report.md");
        var mergedPath = args.Get("merged", "merged.semantic.json");
        await File.WriteAllTextAsync(reportPath, RenderMergeReport(report), Utf8NoBom);
        await WriteJsonAsync(mergedPath, report.MergedWorkbook);

        Console.WriteLine("Merge report: " + Path.GetFullPath(reportPath));
        Console.WriteLine("Merged workbook: " + Path.GetFullPath(mergedPath));
        Console.WriteLine("Conflicts: " + report.Entries.Count(e => e.Status == MergeEntryStatus.Conflict));
        return report.HasConflicts ? 1 : 0;
    }

    private static async Task<int> GateAsync(ParsedArgs args)
    {
        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        var cache = args.Get("cache", workspace.Paths.CacheDirectory);
        var annotations = args.Get("annotations", args.Get("annotation-format", ""));
        if (!Directory.Exists(cache))
        {
            throw new CliException("The cache directory does not exist. Run sync first.", 2, cache);
        }

        var files = Directory.GetFiles(cache, "*.semantic.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            throw new CliException("No semantic workbook files were found. Run sync before gate.", 2, cache);
        }

        var hasError = false;
        var gateReport = new PrGateReport
        {
            GitHead = FirstNonEmpty(await TryRunGitAsync("rev-parse", "HEAD"), "unknown"),
            Branch = FirstNonEmpty(await TryRunGitAsync("branch", "--show-current"), "unknown"),
            Permissions = new GatePermissions { CanReadRegistry = true, CanReadSheets = true },
            MergeReview = new GateReviewState { Status = args.Get("merge-review-status", "not-required-local") },
            PortableSubset = new GateCheckState { Passed = true },
            Triangulation = new GateCheckState { Passed = true },
            SchemaReview = new GateReviewState { Status = args.Get("schema-review-status", "not-required-local") }
        };
        foreach (var file in files)
        {
            Console.WriteLine("Checking " + file);
            var workbook = await ReadJsonAsync<WorkbookDocument>(file);
            var tableId = Path.GetFileName(file).Replace(".semantic.json", "", StringComparison.OrdinalIgnoreCase);
            gateReport.ChangedTables.Add(tableId);
            gateReport.CacheHashes[tableId] = SemanticHasher.ComputeHash(workbook);
            var report = SchemaReviewer.Review(workbook);
            foreach (var finding in report.Findings)
            {
                PrintValidation(finding, args.HasFlag("details"));
                gateReport.PortableSubset.Findings.Add(finding);
                if (string.Equals(annotations, "github", StringComparison.OrdinalIgnoreCase))
                {
                    PrintGitHubAnnotation(file, finding);
                }
            }

            if (report.HasErrors)
            {
                hasError = true;
                gateReport.PortableSubset.Passed = false;
                gateReport.HumanReadableFailures.Add("配表 “" + tableId + "” 没有通过 portable subset 检查。请按上面的单元格位置修正后重新同步。");
            }
        }

        gateReport.Passed = !hasError;
        var reportPath = args.Get("report", Path.Combine(workspace.Root, "Temp", "ConfigSheetForge", "pr-gate-report.json"));
        await WriteJsonAsync(reportPath, gateReport);
        Console.WriteLine("PR gate report: " + Path.GetFullPath(reportPath));
        return hasError ? 1 : 0;
    }

    private static async Task<int> ApplyContractAsync(ParsedArgs args)
    {
        var requestPath = args.Get("request", "");
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            throw new CliException("apply-contract needs --request <contract.json>.", 2);
        }

        var request = await ReadJsonAsync<LifecycleContractRequest>(requestPath);
        request.Operation = FirstNonEmpty(args.Get("operation", ""), request.Operation);
        if (args.HasFlag("dry-run"))
        {
            request.DryRun = true;
        }

        if (CurrentBranchBootstrapOperationRequested(request.Operation))
        {
            if (args.HasFlag("allow-user-fallback"))
            {
                throw new CliException(request.Operation + " 默认使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
            }

            if (args.HasFlag("apply"))
            {
                request.DryRun = false;
            }

            request.SyncCache ??= new SyncCacheContract();
            request.MergeInputs ??= new MergeInputsContract();
            request.TargetBranchBootstrap ??= new TargetBranchBootstrapContract();
            request.MergeInputs.TargetBranch = FirstNonEmpty(args.Get("target-branch", ""), request.MergeInputs.TargetBranch, request.BranchWorkspace.MainGitBranch, "main");
            request.MergeInputs.TargetFeishuProfile = FirstNonEmpty(args.Get("target-profile", ""), args.Get("target-feishu-profile", ""), request.MergeInputs.TargetFeishuProfile, request.BranchWorkspace.MainFeishuBranch, request.MergeInputs.TargetBranch);
            request.TargetBranchBootstrap.ConfirmCreateOnlineSheets = request.TargetBranchBootstrap.ConfirmCreateOnlineSheets || args.HasFlag("confirm-create-online-sheets");
            request.TargetBranchBootstrap.ConfirmRegistryUpsert = request.TargetBranchBootstrap.ConfirmRegistryUpsert || args.HasFlag("confirm-registry-upsert");
            request.TargetBranchBootstrap.ConfirmSchemaReviews = request.TargetBranchBootstrap.ConfirmSchemaReviews || args.HasFlag("confirm-schema-reviews");
            request.TargetBranchBootstrap.ConfirmWriteLocalCache = request.TargetBranchBootstrap.ConfirmWriteLocalCache || args.HasFlag("confirm-write-local-cache");
            request.TargetBranchBootstrap.ConfirmWriteProjectConfig = request.TargetBranchBootstrap.ConfirmWriteProjectConfig || args.HasFlag("confirm-project-config") || args.HasFlag("confirm-write-project-config");
            request.TargetBranchBootstrap.ConfirmExcelToSoSettings = request.TargetBranchBootstrap.ConfirmExcelToSoSettings || args.HasFlag("confirm-excel-to-so");
            await HydrateCurrentBranchBootstrapRequestFromRegistryAsync(request, args, request.SyncCache.TableId);
            if (!request.DryRun)
            {
                var workspace = await LoadWorkspaceAsync(requireConfig: false);
                var currentBranchResult = await ApplyCurrentBranchBootstrapFromTargetAsync(workspace, args, request, request.Operation);
                await EmitLifecycleResultAsync(args, currentBranchResult);
                foreach (var failure in currentBranchResult.HumanReadableFailures)
                {
                    Console.Error.WriteLine("[error] " + failure);
                }

                return currentBranchResult.Success ? 0 : 1;
            }
        }

        if (SeedOperationRequested(request.Operation))
        {
            if (args.HasFlag("allow-user-fallback"))
            {
                throw new CliException("seed-from-local-xlsx 默认且固定使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
            }

            ApplySeedConfirmationFlags(request, args);
            ApplyTargetBranchBootstrapArgs(request, args);
            await RequireMatchingTargetBootstrapPreviewAsync(request, args);
        }

        if (SyncCacheOperationRequested(request.Operation))
        {
            request.SyncCache ??= new SyncCacheContract();
            if (args.HasFlag("allow-user-fallback") && !(args.HasFlag("interactive-desktop") && request.DryRun))
            {
                throw new CliException("sync-cache 默认使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
            }

            request.SyncCache.ConfirmApply = request.SyncCache.ConfirmApply || args.HasFlag("yes") || args.HasFlag("confirm");
            if (!request.DryRun && !request.SyncCache.ConfirmApply)
            {
                throw new CliException("sync-cache apply 会更新本地 cache，必须显式传 --yes，或在 contract.syncCache.confirmApply=true。", 2);
            }

            await HydrateSyncCacheRequestFromRegistryAsync(request, args, SingleTableSelectionForHydrate(request.SyncCache.TableId));
        }

        if (BranchStatusOperationRequested(request.Operation))
        {
            request.SyncCache ??= new SyncCacheContract();
            if (args.HasFlag("allow-user-fallback") && !args.HasFlag("interactive-desktop"))
            {
                throw new CliException(request.Operation + " 默认使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
            }

            await HydrateSyncCacheRequestFromRegistryAsync(request, args, SingleTableSelectionForHydrate(request.SyncCache.TableId));
        }

        if (CompareMergeOperationRequested(request.Operation))
        {
            request.MergeInputs ??= new MergeInputsContract();
            request.MergePolicy ??= new MergePolicyContract();
            if (args.HasFlag("allow-user-fallback") && !(args.HasFlag("interactive-desktop") && request.DryRun))
            {
                throw new CliException("compare-merge 写入或非交互式 gate 默认使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
            }

            request.MergePolicy.ConfirmWriteMain = request.MergePolicy.ConfirmWriteMain ||
                                                   request.MergeInputs.ConfirmWriteMain ||
                                                   args.HasFlag("yes") ||
                                                   args.HasFlag("confirm");
            if (!request.DryRun && !request.MergePolicy.ConfirmWriteMain)
            {
                throw new CliException("compare-merge 写回 main 必须先生成预览并显式确认；请传 --yes，或在 contract.mergeInputs.confirmWriteMain=true。", 2);
            }

            await HydrateCompareMergeRequestFromRegistryAsync(request, args, FirstNonEmpty(request.MergeInputs.TableId, request.SyncCache != null ? request.SyncCache.TableId : "", request.Table != null ? request.Table.TableId : ""));
        }

        if (MergeReviewOperationRequested(request.Operation))
        {
            ApplyMergeReviewArgs(request, args);
            await RequireMatchingCompareMergePreviewAsync(request, args);
        }

        if (SchemaReviewApprovalOperationRequested(request.Operation))
        {
            request.SchemaReviewApproval ??= new SchemaReviewApprovalContract();
            request.SchemaReviewApproval.ConfirmSubmit = request.SchemaReviewApproval.ConfirmSubmit || args.HasFlag("yes") || args.HasFlag("confirm");
        }

        if (WaiverApprovalOperationRequested(request.Operation))
        {
            request.WaiverApproval ??= new WaiverApprovalContract();
            request.WaiverApproval.ConfirmApprove = request.WaiverApproval.ConfirmApprove || args.HasFlag("yes") || args.HasFlag("confirm");
        }

        if (string.Equals(request.Operation, "pr-gate-report", StringComparison.OrdinalIgnoreCase))
        {
            if (args.HasFlag("allow-user-fallback"))
            {
                throw new CliException("PR hard gate 默认 strict bot；不会用用户身份伪装 CI 通过。请修复 bot 权限，或由项目配置显式允许 user fallback for gate。", 2);
            }

            await HydratePrGateReportFromRegistryAsync(request, args);
        }

        ILifecyclePlatform platform = request.DryRun && !SeedOperationRequested(request.Operation)
            ? new PreviewLifecyclePlatform()
            : new CliLifecyclePlatform(args, request);
        var result = await LifecycleExecutor.ExecuteAsync(request, platform, CancellationToken.None);
        if (string.Equals(request.Operation, "sync-status", StringComparison.OrdinalIgnoreCase))
        {
            ApplyReadOnlySyncStatus(result, request);
        }

        if (SyncCacheOperationRequested(request.Operation) && result.Success)
        {
            request.SyncCache ??= new SyncCacheContract();
            var workspace = await LoadWorkspaceAsync(requireConfig: false);
            var tables = BuildSyncCacheTables(request, request.SyncCache.TableId);
            if (tables.Count == 0)
            {
                result.AddFailure("sync-cache apply 找不到当前 branch/profile 的在线 Sheet 定位信息。请确认 ConfigSheets/ProjectSettings 已包含 spreadsheetToken、sheetId 和 TableId + Branch/Profile。");
            }
            else
            {
                var summary = await SyncTableConfigsAsync(workspace, args, tables, request.SyncCache.CacheDirectory, request.SyncCache.ExcelCacheDirectory, writeFormalCache: !request.DryRun);
                ApplySyncCacheSummary(result, summary);
                if (string.Equals(summary.CacheStatus, "blocked", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddFailure((request.DryRun ? "sync-cache dry-run" : "sync-cache apply") + " 没有通过在线读取 / xlsx 导出 / 三方一致性检查。" + (request.DryRun ? "本次没有写本地 cache。" : "已阻断 cache 更新。"));
                }
            }
        }

        if (string.Equals(request.Operation, "pr-gate-report", StringComparison.OrdinalIgnoreCase))
        {
            var gateReportPath = ResolveGateReportPath(args, request);
            result.GateReportPath = Path.GetFullPath(gateReportPath);
            foreach (var action in result.Actions.Where(a => string.Equals(a.Action, "pr-gate-report.write", StringComparison.OrdinalIgnoreCase)))
            {
                action.Details["path"] = result.GateReportPath;
            }

            await WriteJsonAsync(gateReportPath, result.PrGateReport);
            Console.WriteLine("PR gate report: " + result.GateReportPath);
            Console.WriteLine("passed: " + result.PrGateReport.Passed.ToString().ToLowerInvariant());
            if (result.PrGateReport.HumanReadableFailures.Count > 0)
            {
                Console.WriteLine("failures:");
                foreach (var failure in result.PrGateReport.HumanReadableFailures)
                {
                    Console.WriteLine("- " + failure);
                }
            }
        }

        await EmitLifecycleResultAsync(args, result);

        foreach (var failure in result.HumanReadableFailures)
        {
            Console.Error.WriteLine("[error] " + failure);
        }

        return result.Success ? 0 : 1;
    }

    private static string ResolveGateReportPath(ParsedArgs args, LifecycleContractRequest request)
    {
        var path = FirstNonEmpty(
            request.GateReportPath,
            request.ReportPath,
            args.Get("gate-report", ""),
            args.Get("report", ""),
            Path.Combine("Temp", "ConfigSheetForge", "pr-gate-report.json"));
        return Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
    }

    private static async Task<int> RegistryMigrateAsync(ParsedArgs args)
    {
        var baseToken = args.Get("base", "");
        if (string.IsNullOrWhiteSpace(baseToken))
        {
            throw new CliException("registry-migrate needs --base <token>.", 2);
        }

        var locale = args.Get("locale", "zh-Hans");
        var dryRun = args.HasFlag("dry-run");
        var snapshot = new RegistrySnapshot();
        if (!args.HasFlag("offline-plan"))
        {
            var gateway = new LarkCliGateway(args.Get("lark-cli", "lark-cli"));
            snapshot = await LoadRegistrySnapshotFromLarkAsync(gateway, baseToken, locale, args);
        }

        var plan = RegistryMigrator.Plan(snapshot, new RegistryMigrationOptions
        {
            Locale = locale,
            Only = args.Get("only", ""),
            CleanupDefaultRows = args.HasFlag("cleanup-default-rows"),
            CleanupDefaultFields = args.HasFlag("cleanup-default-fields"),
            CleanupDuplicateBranchBindings = args.HasFlag("cleanup-duplicate-branch-bindings") || args.HasFlag("cleanup-duplicates")
        });
        var result = new LifecycleContractResult
        {
            Operation = "registry-migrate",
            DryRun = dryRun,
            DisplayNameMapping = plan.DisplayNameMapping,
            Success = true
        };
        result.Actions.AddRange(plan.Actions);
        if (dryRun)
        {
            result.Actions.Add(new LifecycleActionResult
            {
                Action = "registry.migration.apply",
                Status = "planned",
                Message = string.Equals(args.Get("only", ""), "review-status-options", StringComparison.OrdinalIgnoreCase)
                    ? "预览：只检查 MergeReviews / SchemaReviews / Waivers 的状态选项，不会写入 Base。"
                    : "预览：不会写入 Base。"
            });
        }
        else
        {
            if (plan.Actions.Any(IsWritableRegistryMigrationAction) && !args.HasFlag("yes"))
            {
                throw new CliException("registry-migrate apply 会写入在线 Base 注册中心，必须显式传 --yes。建议先运行 --dry-run 审计字段和 record_id 后再确认。", 2);
            }

            var gateway = new LarkCliGateway(args.Get("lark-cli", "lark-cli"));
            await ApplyRegistryMigrationToLarkAsync(gateway, baseToken, plan, args, result);
        }

        var outPath = args.Get("out", "");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            await WriteJsonAsync(outPath, result);
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }

        return 0;
    }

    private static bool IsDestructiveRegistryMigrationAction(LifecycleActionResult action)
    {
        return string.Equals(action.Action, "registry.record.delete_empty", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.Action, "registry.field.delete_default", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.Action, "registry.record.delete_duplicate_branch_binding", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWritableRegistryMigrationAction(LifecycleActionResult action)
    {
        return IsDestructiveRegistryMigrationAction(action) ||
               string.Equals(action.Action, "registry.table.rename", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.Action, "registry.field.rename", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.Action, "registry.field.options.ensure", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<RegistrySnapshot> LoadRegistrySnapshotFromLarkAsync(LarkCliGateway gateway, string baseToken, string locale, ParsedArgs args)
    {
        var snapshot = new RegistrySnapshot();
        var tableResult = await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+table-list", "--base-token", baseToken, "--offset", "0", "--limit", "100" });
        foreach (var table in ParseLarkBaseTableListJson(CombinedJsonOutput(tableResult), locale))
        {
            snapshot.Tables.Add(table);

            var fieldResult = await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+field-list", "--base-token", baseToken, "--table-id", table.TableId, "--offset", "0", "--limit", "200" });
            table.Fields.AddRange(ParseLarkBaseFieldListJson(CombinedJsonOutput(fieldResult), locale));

            var recordResult = await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+record-list", "--base-token", baseToken, "--table-id", table.TableId, "--offset", "0", "--limit", "200", "--format", "json" });
            table.Records.AddRange(ParseLarkBaseRecordListJson(CombinedJsonOutput(recordResult)));
        }

        return snapshot;
    }

    private static async Task ApplyRegistryMigrationToLarkAsync(LarkCliGateway gateway, string baseToken, RegistryMigrationPlan plan, ParsedArgs args, LifecycleContractResult result)
    {
        foreach (var action in plan.Actions)
        {
            try
            {
                switch (action.Action)
                {
                    case "registry.table.rename":
                        await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+table-update", "--base-token", baseToken, "--table-id", GetDetail(action, "tableId"), "--name", GetDetail(action, "displayName") });
                        action.Status = "done";
                        break;
                    case "registry.field.rename":
                        var fieldType = FirstNonEmpty(GetDetail(action, "fieldType"), "text");
                        var fieldJson = JsonSerializer.Serialize(new Dictionary<string, string>
                        {
                            ["name"] = GetDetail(action, "displayName"),
                            ["type"] = fieldType
                        }, CompactJsonOptions);
                        await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+field-update", "--base-token", baseToken, "--table-id", GetDetail(action, "tableId"), "--field-id", GetDetail(action, "fieldId"), "--json", fieldJson, "--yes" });
                        action.Status = "done";
                        break;
                    case "registry.field.options.ensure":
                        var optionsJson = BuildSelectFieldUpdateJson(
                            FirstNonEmpty(GetDetail(action, "fieldName"), "状态"),
                            SplitCsv(GetDetail(action, "allOptions")).ToList());
                        await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+field-update", "--base-token", baseToken, "--table-id", GetDetail(action, "tableId"), "--field-id", GetDetail(action, "fieldId"), "--json", optionsJson, "--yes" });
                        action.Status = "done";
                        action.Message = "已补齐状态单选选项：" + GetDetail(action, "missingOptions") + "。";
                        break;
                    case "registry.field.status_select_mismatch":
                        action.Status = "blocked";
                        action.Message = FirstNonEmpty(action.Message, "状态字段不是单选字段；registry-migrate 不会自动改字段类型。");
                        break;
                    case "registry.record.delete_empty":
                    case "registry.record.delete_duplicate_branch_binding":
                        await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+record-delete", "--base-token", baseToken, "--table-id", GetDetail(action, "tableId"), "--record-id", GetDetail(action, "recordId"), "--yes" });
                        action.Status = "done";
                        break;
                    case "registry.field.delete_default":
                        await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+field-delete", "--base-token", baseToken, "--table-id", GetDetail(action, "tableId"), "--field-id", GetDetail(action, "fieldId"), "--yes" });
                        action.Status = "done";
                        break;
                }
            }
            catch (CliException ex)
            {
                action.Status = "failed";
                action.Details["error"] = ex.Message;
                result.AddFailure(ex.Message);
            }
        }
    }

    private static string BuildSelectFieldUpdateJson(string fieldName, IReadOnlyList<string> options)
    {
        var palette = new[]
        {
            new { hue = "Green", lightness = "Light" },
            new { hue = "Blue", lightness = "Light" },
            new { hue = "Orange", lightness = "Light" },
            new { hue = "Purple", lightness = "Light" },
            new { hue = "Red", lightness = "Light" },
            new { hue = "Grey", lightness = "Light" }
        };
        var optionObjects = new List<Dictionary<string, string>>();
        for (var i = 0; i < options.Count; i++)
        {
            var name = options[i];
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var color = palette[i % palette.Length];
            optionObjects.Add(new Dictionary<string, string>
            {
                ["name"] = name,
                ["hue"] = color.hue,
                ["lightness"] = color.lightness
            });
        }

        var body = new Dictionary<string, object>
        {
            ["name"] = fieldName,
            ["type"] = "select",
            ["multiple"] = false,
            ["options"] = optionObjects
        };
        return JsonSerializer.Serialize(body, CompactJsonOptions);
    }

    private static async Task<LarkCliResult> RunLarkCliStrictAsync(LarkCliGateway gateway, ParsedArgs args, IEnumerable<string> commandArgs)
    {
        var commandList = commandArgs.ToList();
        var doctor = await gateway.RunAsync(new[] { "doctor" }, Directory.GetCurrentDirectory(), CancellationToken.None);
        if (!doctor.Success)
        {
            throw new CliException("lark-cli doctor 没有通过。请先修复本地 Feishu CLI 配置和权限。命令类别：" + LarkCommandCategory(commandList) + "。", 1, Trim(doctor.Stderr + "\n" + doctor.Stdout));
        }

        var identity = args.Get("lark-identity", "bot");
        var first = await gateway.RunAsync(WithLarkIdentity(commandList, identity), Directory.GetCurrentDirectory(), CancellationToken.None);
        if (first.Success || !string.Equals(identity, "bot", StringComparison.OrdinalIgnoreCase) || !args.HasFlag("allow-user-fallback"))
        {
            if (!first.Success)
            {
                throw BuildLarkCliFailure(commandList, identity, first, args.HasFlag("allow-user-fallback"));
            }

            return first;
        }

        var fallback = await gateway.RunAsync(WithLarkIdentity(commandList, "user"), Directory.GetCurrentDirectory(), CancellationToken.None);
        if (!fallback.Success)
        {
            throw BuildLarkCliFailure(commandList, "user", fallback, allowUserFallback: true);
        }

        return fallback;
    }

    private static CliException BuildLarkCliFailure(IReadOnlyList<string> commandArgs, string identity, LarkCliResult result, bool allowUserFallback)
    {
        var category = LarkCommandCategory(commandArgs);
        var summary = SanitizeLarkCommand(commandArgs);
        var raw = Trim(result.Stderr + "\n" + result.Stdout);
        if (LooksLikeMissingLarkCli(result, raw))
        {
            var missingMessage = "本机没有找到 lark-cli，无法用 " + identity + " 身份执行飞书操作：" + category +
                                 "。请重启 Unity，或设置 CONFIG_SHEET_FORGE_LARK_CLI / LARK_CLI_PATH 指向 lark-cli，也可以确认 %APPDATA%\\npm 已在 PATH 中。";
            var missingDetail = "category=" + category + Environment.NewLine +
                                "identity=" + identity + Environment.NewLine +
                                "command=" + summary + Environment.NewLine +
                                "exitCode=" + result.ExitCode.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                                "resolved=" + result.ResolvedCommand.DisplayPath + Environment.NewLine +
                                "source=" + result.ResolvedCommand.Source + Environment.NewLine +
                                "stderr/stdout=" + raw;
            return new CliException(missingMessage, 1, missingDetail);
        }

        var strict = string.Equals(identity, "bot", StringComparison.OrdinalIgnoreCase) && !allowUserFallback
            ? "当前是 bot 严格模式，不会静默切换到 user。"
            : "当前执行身份：" + identity + "。";
        var message = "飞书操作失败：" + category + "。" + strict +
                      " 参数摘要：" + summary +
                      "。lark-cli 返回：" + FirstNonEmpty(ExtractLarkError(raw), raw, "无 stderr/stdout") +
                      "。修复建议：请检查该步骤所需 scope、bot 是否有目标 Base/Wiki/Sheet 权限，以及 tableId、sheetId、range 是否正确。";
        var detail = "category=" + category + Environment.NewLine +
                     "identity=" + identity + Environment.NewLine +
                     "command=" + summary + Environment.NewLine +
                     "exitCode=" + result.ExitCode.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                     "resolved=" + result.ResolvedCommand.DisplayPath + Environment.NewLine +
                     "stderr/stdout=" + raw;
        return new CliException(message, 1, detail);
    }

    private static bool LooksLikeMissingLarkCli(LarkCliResult result, string raw)
    {
        if (result.ResolvedCommand.Source.Equals("unresolved", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        raw ??= "";
        return raw.Contains("ApplicationName='lark-cli'", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("lark-cli", StringComparison.OrdinalIgnoreCase) &&
               (raw.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("not recognized", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("The system cannot find", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("系统找不到", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("找不到", StringComparison.OrdinalIgnoreCase));
    }

    private static string LarkCommandCategory(IReadOnlyList<string> args)
    {
        if (args == null || args.Count == 0)
        {
            return "lark-cli";
        }

        return args.Count == 1 ? args[0] : args[0] + " " + args[1];
    }

    private static string SanitizeLarkCommand(IReadOnlyList<string> args)
    {
        var sensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--base-token",
            "--spreadsheet-token",
            "--node-token",
            "--parent-node-token",
            "--folder-token",
            "--parent-token",
            "--url",
            "--wiki-url",
            "--root-wiki-url"
        };
        var builder = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i] ?? "";
            builder.Add(arg);
            if (sensitive.Contains(arg) && i + 1 < args.Count)
            {
                builder.Add(Mask(args[++i]));
            }
            else if ((arg.Equals("--json", StringComparison.OrdinalIgnoreCase) ||
                      arg.Equals("--data", StringComparison.OrdinalIgnoreCase) ||
                      arg.Equals("--values", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Count)
            {
                var value = args[++i] ?? "";
                builder.Add(value.Length <= 120 ? value : value.Substring(0, 120) + "...");
            }
        }

        return string.Join(" ", builder);
    }

    private static string ExtractLarkError(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        foreach (var marker in new[] { "missing_scope", "permission", "forbidden", "unauthorized", "unsafe output path", "command line is too long", "range in request is wrong", "data exceeded", "invalid JSON" })
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var start = Math.Max(0, index - 80);
                var length = Math.Min(text.Length - start, 500);
                return text.Substring(start, length).Trim();
            }
        }

        return text.Length <= 500 ? text : text.Substring(0, 500);
    }

    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        text = text.Trim();
        return text.Length <= 4000 ? text : text.Substring(0, 4000);
    }

    private static IEnumerable<string> WithLarkIdentity(IEnumerable<string> args, string identity)
    {
        foreach (var arg in args)
        {
            yield return arg;
        }

        if (!string.Equals(identity, "default", StringComparison.OrdinalIgnoreCase))
        {
            yield return "--as";
            yield return identity;
        }
    }

    private static List<JsonElement> FindJsonObjects(string text, string requiredProperty)
    {
        var result = new List<JsonElement>();
        foreach (var candidate in JsonCandidates(text))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                CollectJsonObjects(document.RootElement, requiredProperty, result);
                if (result.Count > 0)
                {
                    return result;
                }
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    private static void CollectJsonObjects(JsonElement element, string requiredProperty, ICollection<JsonElement> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.EnumerateObject().Any(p => string.Equals(p.Name, requiredProperty, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(element.Clone());
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectJsonObjects(property.Value, requiredProperty, result);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectJsonObjects(child, requiredProperty, result);
            }
        }
    }

    private static IEnumerable<string> JsonCandidates(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var trimmed = text.Trim();
        yield return trimmed;
        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
        {
            yield return trimmed.Substring(objectStart, objectEnd - objectStart + 1);
        }

        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            yield return trimmed.Substring(arrayStart, arrayEnd - arrayStart + 1);
        }
    }

    private static string CombinedJsonOutput(LarkCliResult result)
    {
        return string.IsNullOrWhiteSpace(result.Stdout) ? result.Stderr : result.Stdout;
    }

    public static string ParseLarkRecordId(string json)
    {
        foreach (var candidate in JsonCandidates(json))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                var exact = FindJsonStringRecursive(document.RootElement, requireRecordIdPrefix: false, "record_id", "recordId");
                if (!string.IsNullOrWhiteSpace(exact))
                {
                    return exact;
                }

                var id = FindJsonStringRecursive(document.RootElement, requireRecordIdPrefix: true, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }
            catch (JsonException)
            {
            }
        }

        return "";
    }

    private static string FindJsonStringRecursive(JsonElement element, bool requireRecordIdPrefix, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var value = JsonScalarToText(property.Value).Trim();
                if (!string.IsNullOrWhiteSpace(value) &&
                    (!requireRecordIdPrefix || value.StartsWith("rec", StringComparison.OrdinalIgnoreCase)))
                {
                    return value;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindJsonStringRecursive(property.Value, requireRecordIdPrefix, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindJsonStringRecursive(child, requireRecordIdPrefix, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return "";
    }

    private static string JsonScalarToText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? "";
        }

        return element.ValueKind == JsonValueKind.Number ? element.ToString() : "";
    }

    public static RegistrySnapshot ParseLarkBaseRegistrySnapshotJson(string tableListJson, IDictionary<string, string> fieldListJsonByTableId, IDictionary<string, string> recordListJsonByTableId, string locale)
    {
        var snapshot = new RegistrySnapshot();
        foreach (var table in ParseLarkBaseTableListJson(tableListJson, locale))
        {
            if (fieldListJsonByTableId != null && fieldListJsonByTableId.TryGetValue(table.TableId, out var fieldJson))
            {
                table.Fields.AddRange(ParseLarkBaseFieldListJson(fieldJson, locale));
            }

            if (recordListJsonByTableId != null && recordListJsonByTableId.TryGetValue(table.TableId, out var recordJson))
            {
                table.Records.AddRange(ParseLarkBaseRecordListJson(recordJson));
            }

            snapshot.Tables.Add(table);
        }

        return snapshot;
    }

    public static IReadOnlyList<RegistryTableSnapshot> ParseLarkBaseTableListJson(string json, string locale)
    {
        var tables = new List<RegistryTableSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in JsonCandidates(json))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                CollectTableSnapshots(document.RootElement, tables, seen, locale);
                if (tables.Count > 0)
                {
                    return tables;
                }
            }
            catch (JsonException)
            {
            }
        }

        return tables;
    }

    public static IReadOnlyList<RegistryFieldSnapshot> ParseLarkBaseFieldListJson(string json, string locale)
    {
        var fields = new List<RegistryFieldSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in JsonCandidates(json))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                CollectFieldSnapshots(document.RootElement, fields, seen, locale);
                if (fields.Count > 0)
                {
                    return fields;
                }
            }
            catch (JsonException)
            {
            }
        }

        return fields;
    }

    public static IReadOnlyList<RegistryRecordSnapshot> ParseLarkBaseRecordListJson(string json)
    {
        var records = new List<RegistryRecordSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in JsonCandidates(json))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                CollectMatrixRegistryRecords(document.RootElement, records, seen);
                CollectObjectRegistryRecords(document.RootElement, records, seen);
                if (records.Count > 0)
                {
                    return records;
                }
            }
            catch (JsonException)
            {
            }
        }

        return records;
    }

    private static void CollectTableSnapshots(JsonElement element, ICollection<RegistryTableSnapshot> tables, ISet<string> seen, string locale)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetJsonProperty(element, "tables", out var tableArray) && tableArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tableArray.EnumerateArray())
                {
                    AddTableSnapshot(item, tables, seen, locale, allowPlainIdName: true);
                }
            }

            AddTableSnapshot(element, tables, seen, locale, allowPlainIdName: false);
            foreach (var property in element.EnumerateObject())
            {
                CollectTableSnapshots(property.Value, tables, seen, locale);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectTableSnapshots(child, tables, seen, locale);
            }
        }
    }

    private static void AddTableSnapshot(JsonElement element, ICollection<RegistryTableSnapshot> tables, ISet<string> seen, string locale, bool allowPlainIdName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var tableId = GetJsonString(element, "table_id", "tableId");
        if (string.IsNullOrWhiteSpace(tableId) && allowPlainIdName)
        {
            tableId = GetJsonString(element, "id");
        }

        if (string.IsNullOrWhiteSpace(tableId) || !seen.Add(tableId))
        {
            return;
        }

        var displayName = GetJsonString(element, "table_name", "tableName");
        if (string.IsNullOrWhiteSpace(displayName) && allowPlainIdName)
        {
            displayName = GetJsonString(element, "name");
        }

        tables.Add(new RegistryTableSnapshot
        {
            TableId = tableId,
            DisplayName = displayName,
            MachineKey = ResolveMachineKey(displayName, RegistryLocalization.Default(locale).Tables)
        });
    }

    private static void CollectFieldSnapshots(JsonElement element, ICollection<RegistryFieldSnapshot> fields, ISet<string> seen, string locale)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetJsonProperty(element, "fields", out var fieldArray) && fieldArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in fieldArray.EnumerateArray())
                {
                    AddFieldSnapshot(item, fields, seen, locale, allowPlainIdName: true);
                }
            }

            AddFieldSnapshot(element, fields, seen, locale, allowPlainIdName: false);
            foreach (var property in element.EnumerateObject())
            {
                CollectFieldSnapshots(property.Value, fields, seen, locale);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectFieldSnapshots(child, fields, seen, locale);
            }
        }
    }

    private static void AddFieldSnapshot(JsonElement element, ICollection<RegistryFieldSnapshot> fields, ISet<string> seen, string locale, bool allowPlainIdName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var fieldId = GetJsonString(element, "field_id", "fieldId");
        if (string.IsNullOrWhiteSpace(fieldId) && allowPlainIdName)
        {
            fieldId = GetJsonString(element, "id");
        }

        var displayName = GetJsonString(element, "field_name", "fieldName");
        if (string.IsNullOrWhiteSpace(displayName) && allowPlainIdName)
        {
            displayName = GetJsonString(element, "name");
        }

        if (string.IsNullOrWhiteSpace(fieldId) && string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var key = FirstNonEmpty(fieldId, displayName);
        if (!seen.Add(key))
        {
            return;
        }

        fields.Add(new RegistryFieldSnapshot
        {
            FieldId = fieldId,
            DisplayName = displayName,
            MachineKey = ResolveMachineKey(displayName, RegistryLocalization.Default(locale).Fields),
            Type = GetJsonString(element, "type", "field_type", "fieldType"),
            Options = ExtractFieldOptionNames(element),
            IsDefaultField = IsDefaultBaseField(displayName)
        });
    }

    private static List<string> ExtractFieldOptionNames(JsonElement element)
    {
        var options = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFieldOptionNames(element, options, seen);
        return options;
    }

    private static void CollectFieldOptionNames(JsonElement element, ICollection<string> options, ISet<string> seen)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "options", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var option in property.Value.EnumerateArray())
                    {
                        var name = option.ValueKind == JsonValueKind.String
                            ? option.GetString() ?? ""
                            : GetJsonString(option, "name", "text", "value", "label", "option_name", "optionName");
                        if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                        {
                            options.Add(name);
                        }
                    }
                }
                else
                {
                    CollectFieldOptionNames(property.Value, options, seen);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectFieldOptionNames(child, options, seen);
            }
        }
    }

    public static IReadOnlyList<RegistryRecordSnapshot> FindMatchingRegistryRecords(IEnumerable<RegistryRecordSnapshot> records, IDictionary<string, string> keys, string locale)
    {
        return FindMatchingRegistryRecords(records, keys, locale, new RegistryContract());
    }

    private static IReadOnlyList<RegistryRecordSnapshot> FindMatchingRegistryRecords(IEnumerable<RegistryRecordSnapshot> records, IDictionary<string, string> keys, string locale, RegistryContract registry)
    {
        if (records == null || keys == null || keys.Count == 0 || keys.Values.Any(string.IsNullOrWhiteSpace))
        {
            return Array.Empty<RegistryRecordSnapshot>();
        }

        return records
            .Where(record => keys.All(key =>
            {
                var value = GetRegistryRecordValue(record, key.Key, locale, registry);
                return string.Equals(value, key.Value, StringComparison.OrdinalIgnoreCase);
            }))
            .ToList();
    }

    private static void CollectMatrixRegistryRecords(JsonElement element, ICollection<RegistryRecordSnapshot> records, ISet<string> seen)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetJsonProperty(element, "fields", out var fieldsElement) && fieldsElement.ValueKind == JsonValueKind.Array &&
                TryGetJsonProperty(element, "data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array &&
                TryGetJsonProperty(element, "record_id_list", out var recordIdsElement) && recordIdsElement.ValueKind == JsonValueKind.Array)
            {
                var fieldNames = fieldsElement.EnumerateArray()
                    .Select(RegistryMatrixFieldName)
                    .ToList();
                var recordIds = recordIdsElement.EnumerateArray()
                    .Select(JsonElementToText)
                    .ToList();
                var rows = dataElement.EnumerateArray().ToList();
                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var recordId = rowIndex < recordIds.Count ? recordIds[rowIndex] : "";
                    var record = new RegistryRecordSnapshot { RecordId = recordId };
                    if (rows[rowIndex].ValueKind == JsonValueKind.Array)
                    {
                        var cells = rows[rowIndex].EnumerateArray().ToList();
                        for (var column = 0; column < cells.Count && column < fieldNames.Count; column++)
                        {
                            if (!string.IsNullOrWhiteSpace(fieldNames[column]))
                            {
                                record.Values[fieldNames[column]] = JsonElementToText(cells[column]);
                            }
                        }
                    }
                    else if (rows[rowIndex].ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in rows[rowIndex].EnumerateObject())
                        {
                            record.Values[property.Name] = JsonElementToText(property.Value);
                        }
                    }

                    AddRegistryRecord(records, seen, record);
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectMatrixRegistryRecords(property.Value, records, seen);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectMatrixRegistryRecords(child, records, seen);
            }
        }
    }

    private static void CollectObjectRegistryRecords(JsonElement element, ICollection<RegistryRecordSnapshot> records, ISet<string> seen)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var recordId = GetJsonString(element, "record_id", "recordId");
            if (!string.IsNullOrWhiteSpace(recordId) && TryGetJsonProperty(element, "fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
            {
                var record = new RegistryRecordSnapshot { RecordId = recordId };
                foreach (var field in fields.EnumerateObject())
                {
                    record.Values[field.Name] = JsonElementToText(field.Value);
                }

                AddRegistryRecord(records, seen, record);
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectObjectRegistryRecords(property.Value, records, seen);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectObjectRegistryRecords(child, records, seen);
            }
        }
    }

    private static void AddRegistryRecord(ICollection<RegistryRecordSnapshot> records, ISet<string> seen, RegistryRecordSnapshot record)
    {
        var key = FirstNonEmpty(record.RecordId, string.Join("\n", record.Values.Select(v => v.Key + "=" + v.Value)));
        if (!string.IsNullOrWhiteSpace(key) && !seen.Add(key))
        {
            return;
        }

        records.Add(record);
    }

    private static string RegistryMatrixFieldName(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? "";
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            return FirstNonEmpty(GetJsonString(element, "field_name", "name", "fieldName", "title", "key"), GetJsonString(element, "field_id", "fieldId", "id"));
        }

        return JsonElementToText(element);
    }

    private static RegistryTableSnapshot? FindRegistryTable(RegistrySnapshot snapshot, string machineKey, string locale)
    {
        if (snapshot == null)
        {
            return null;
        }

        var mapping = RegistryLocalization.Default(locale).Tables;
        var display = mapping.TryGetValue(machineKey, out var mapped) ? mapped : machineKey;
        return snapshot.Tables.FirstOrDefault(t =>
            string.Equals(t.MachineKey, machineKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.DisplayName, machineKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.DisplayName, display, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRegistryRecordValue(RegistryRecordSnapshot record, string fieldName, string locale, RegistryContract? registry)
    {
        if (record == null || string.IsNullOrWhiteSpace(fieldName))
        {
            return "";
        }

        var fallback = "";
        foreach (var alias in RegistryFieldAliases(fieldName, locale, registry))
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

    private static string GetRegistryStatusValue(RegistryRecordSnapshot record, string fieldName, string locale, RegistryContract? registry)
    {
        return PrGateReportEvaluator.NormalizeReviewStatus(GetRegistryRecordValue(record, fieldName, locale, registry));
    }

    private static IEnumerable<string> RegistryFieldAliases(string fieldName, string locale, RegistryContract? registry)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                aliases.Add(value);
            }
        }

        Add(fieldName);
        foreach (var pair in RegistryLocalization.Default(locale).Fields)
        {
            if (string.Equals(fieldName, pair.Key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fieldName, pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                Add(pair.Key);
                Add(pair.Value);
            }
        }

        if (registry != null)
        {
            foreach (var tableFields in registry.FieldDisplayNames.Values)
            {
                foreach (var pair in tableFields)
                {
                    if (string.Equals(fieldName, pair.Key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fieldName, pair.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        Add(pair.Key);
                        Add(pair.Value);
                    }
                }
            }
        }

        return aliases;
    }

    private static bool TryGetJsonProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var pair in element.EnumerateObject())
        {
            if (string.Equals(pair.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        return false;
    }

    private static string JsonElementToText(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.ToString();
    }

    private static string GetJsonString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
            }

            foreach (var pair in element.EnumerateObject())
            {
                if (string.Equals(pair.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString() ?? "" : pair.Value.ToString();
                }
            }
        }

        return "";
    }

    private static bool SeedOperationRequested(string operation)
    {
        return string.Equals(operation, "seed-from-local-xlsx", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation, "bootstrap-from-local-xlsx", StringComparison.OrdinalIgnoreCase) ||
               TargetBranchBootstrapOperationRequested(operation);
    }

    private static bool TargetBranchBootstrapOperationRequested(string operation)
    {
        return string.Equals(operation, "bootstrap-target-branch-from-local-xlsx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CurrentBranchBootstrapOperationRequested(string operation)
    {
        return string.Equals(operation, "bootstrap-current-branch-from-target", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation, "branch-workspace-bootstrap-from-target", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SyncCacheOperationRequested(string operation)
    {
        return string.Equals(operation, "sync-cache", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation, "sync-from-online-sheet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool BranchStatusOperationRequested(string operation)
    {
        return string.Equals(operation, "registry-status", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation, "branch-status", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation, "sync-status", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompareMergeOperationRequested(string operation)
    {
        return string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MergeReviewOperationRequested(string operation)
    {
        return string.Equals(operation, "submit-merge-review", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation, "approve-merge-review", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SchemaReviewApprovalOperationRequested(string operation)
    {
        return string.Equals(operation, "approve-schema-review", StringComparison.OrdinalIgnoreCase);
    }

    private static bool WaiverApprovalOperationRequested(string operation)
    {
        return string.Equals(operation, "approve-waiver", StringComparison.OrdinalIgnoreCase);
    }

    private static bool GetJsonBool(JsonElement element, bool fallback, params string[] names)
    {
        var value = GetJsonProperty(element, names);
        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? "";
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        return fallback;
    }

    private static int GetJsonInt(JsonElement element, int fallback, params string[] names)
    {
        var value = GetJsonProperty(element, names);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static JsonElement GetJsonProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                return property;
            }

            foreach (var pair in element.EnumerateObject())
            {
                if (string.Equals(pair.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        return default;
    }

    private static string FindStringDeep(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var direct = GetJsonString(element, names);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindStringDeep(property.Value, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
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

    private static bool FindBoolDeep(JsonElement element, string name, bool fallback)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var direct = GetJsonProperty(element, name);
            if (direct.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (direct.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindBoolDeep(property.Value, name, fallback);
                if (nested != fallback)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindBoolDeep(item, name, fallback);
                if (nested != fallback)
                {
                    return nested;
                }
            }
        }

        return fallback;
    }

    private static void MergeRegistryTableIds(IDictionary<string, string> tableIds, JsonElement root)
    {
        foreach (var property in new[] { "tableIds", "tables" })
        {
            var registry = GetJsonProperty(root, "registry");
            if (registry.ValueKind == JsonValueKind.Object)
            {
                AddRegistryTableIds(tableIds, GetJsonProperty(registry, property));
            }
        }

        var feishu = GetJsonProperty(root, "feishu", "lark");
        if (feishu.ValueKind == JsonValueKind.Object)
        {
            var registryBase = GetJsonProperty(feishu, "registryBase", "baseRegistry");
            if (registryBase.ValueKind == JsonValueKind.Object)
            {
                AddRegistryTableIds(tableIds, GetJsonProperty(registryBase, "tableIds", "tables"));
            }
        }

        AddRegistryTableIds(tableIds, GetJsonProperty(root, "registryTableIds"));
    }

    private static void AddRegistryTableIds(IDictionary<string, string> tableIds, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var value = "";
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? "";
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                value = GetJsonString(property.Value, "tableId", "id", "token");
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                tableIds[property.Name] = value;
            }
        }
    }

    private static IEnumerable<BranchBindingContract> ParseBranchBindings(JsonElement root)
    {
        var property = GetJsonProperty(root, "branchBindings", "bindings");
        if (property.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var binding = new BranchBindingContract
            {
                RecordId = GetJsonString(item, "recordId", "record_id"),
                GitBranch = GetJsonString(item, "gitBranch", "branch"),
                FeishuBranch = GetJsonString(item, "feishuBranch", "larkBranch"),
                Profile = GetJsonString(item, "profile", "feishuProfile", "larkProfile"),
                Slug = GetJsonString(item, "slug"),
                NodeTitle = GetJsonString(item, "nodeTitle", "wikiNodeTitle"),
                WikiNodeToken = GetJsonString(item, "wikiNodeToken"),
                WikiNodeUrl = GetJsonString(item, "wikiNodeUrl", "url"),
                Status = GetJsonString(item, "status"),
                OwnerRole = GetJsonString(item, "ownerRole"),
                CreatedBy = GetJsonString(item, "createdBy"),
                CreatedAt = GetJsonString(item, "createdAt"),
                UpdatedAt = GetJsonString(item, "updatedAt")
            };

            if (!string.IsNullOrWhiteSpace(binding.GitBranch) || !string.IsNullOrWhiteSpace(binding.Profile) || !string.IsNullOrWhiteSpace(binding.FeishuBranch))
            {
                yield return binding;
            }
        }
    }

    private static IEnumerable<JsonElement> FindTableArray(JsonElement root)
    {
        foreach (var name in new[] { "tables", "configSheets", "tableMappings", "excelTables", "excelToSoTables", "seedTables" })
        {
            var property = GetJsonProperty(root, name);
            if (property.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.EnumerateArray())
                {
                    yield return item;
                }

                yield break;
            }
        }
    }

    private static IEnumerable<ContractFieldSpec> ParseFieldSpecs(JsonElement tableElement)
    {
        var property = GetJsonProperty(tableElement, "fields", "columns", "schema");
        if (property.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var key = item.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(key))
                {
                    yield return new ContractFieldSpec { Key = key, DisplayName = key, ValueKind = "string" };
                }
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var key = FirstNonEmpty(GetJsonString(item, "key", "id", "name"), GetJsonString(item, "displayName", "title"));
                if (!string.IsNullOrWhiteSpace(key))
                {
                    yield return new ContractFieldSpec
                    {
                        Key = key,
                        DisplayName = FirstNonEmpty(GetJsonString(item, "displayName", "title"), key),
                        ValueKind = FirstNonEmpty(GetJsonString(item, "valueKind", "type", "kind"), "string"),
                        OriginalType = GetJsonString(item, "originalType", "sourceType", "oldType", "excelOriginalType"),
                        ExcelToSoType = GetJsonString(item, "excelToSoType", "excelType", "unityType", "cacheType"),
                        Description = GetJsonString(item, "description", "desc")
                    };
                }
            }
        }
    }

    private static string ResolveMachineKey(string displayName, IDictionary<string, string> mapping)
    {
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

    private static bool IsDefaultBaseField(string displayName)
    {
        return string.Equals(displayName, "Text", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(displayName, "Single option", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(displayName, "Date", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(displayName, "Attachment", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDetail(LifecycleActionResult action, string key)
    {
        return action.Details.TryGetValue(key, out var value) ? value : "";
    }

    private static IWorkbookProvider CreateProvider(string provider)
    {
        if (string.Equals(provider, "lark", StringComparison.OrdinalIgnoreCase) || string.Equals(provider, "feishu", StringComparison.OrdinalIgnoreCase))
        {
            return new LarkCliWorkbookProvider();
        }

        throw new CliException("Unknown provider '" + provider + "'. v0.1 supports the lark provider and keeps the provider boundary open for future implementations.", 2);
    }

    private static ProviderContext CreateProviderContext(Workspace workspace, ParsedArgs? args = null)
    {
        var context = new ProviderContext { WorkspaceRoot = workspace.Root };
        foreach (var pair in workspace.Config.ProviderSettings)
        {
            context.Settings[pair.Key] = pair.Value;
        }

        context.Settings["rootUrl"] = workspace.Config.RootUrl;
        context.Settings["rootToken"] = workspace.Config.RootToken;
        context.Settings["rootObjectType"] = workspace.Config.RootObjectType;
        if (args != null && args.HasFlag("allow-user-fallback"))
        {
            context.Settings["larkAllowUserFallback"] = "true";
        }

        return context;
    }

    private static async Task<Workspace> LoadWorkspaceAsync(bool requireConfig)
    {
        var root = Directory.GetCurrentDirectory();
        var paths = WorkspacePaths.For(root);
        ForgeConfig config;
        TableRegistry registry;

        if (File.Exists(paths.ConfigPath))
        {
            config = await ReadJsonAsync<ForgeConfig>(paths.ConfigPath);
        }
        else if (requireConfig)
        {
            throw new CliException("No local config was found. Run config-sheet-forge init in the project root.", 2, paths.ConfigPath);
        }
        else
        {
            config = new ForgeConfig();
        }

        if (File.Exists(paths.RegistryPath))
        {
            registry = await ReadJsonAsync<TableRegistry>(paths.RegistryPath);
        }
        else
        {
            registry = new TableRegistry();
        }

        return new Workspace(root, paths, config, registry);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Config Sheet Forge");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  config-sheet-forge init [--root <url>] [--force]");
        Console.WriteLine("  config-sheet-forge doctor [--details]");
        Console.WriteLine("  config-sheet-forge discover-root --query <name>");
        Console.WriteLine("  config-sheet-forge new-table --id <id> --name <name> [--spreadsheet <url-or-token>] [--sheet-id <id>] [--range <A1>] [--field-row <0>] [--type-row <1>] [--description-row <2>] [--data-start-row <3>]");
        Console.WriteLine("  config-sheet-forge sync [--table <id>] [--input <semantic.json>]");
        Console.WriteLine("  config-sheet-forge registry-status [--manifest <project-config-or-contract>] [--details]");
        Console.WriteLine("  config-sheet-forge sync-cache [--table <id>|--tables A,B] [--manifest <project-config-or-contract>] [--dry-run] [--yes]");
        Console.WriteLine("  config-sheet-forge repair-cache-dialect [--manifest <project-config-or-contract>] [--tables A,B] [--dry-run] [--yes]");
        Console.WriteLine("  config-sheet-forge bootstrap-current-branch-from-target --manifest <project-config-or-contract> --target-branch main --dry-run");
        Console.WriteLine("  config-sheet-forge seed-from-xlsx --table <id> --source-xlsx <path> --dry-run");
        Console.WriteLine("  config-sheet-forge seed-from-xlsx --all --manifest <project-config-or-contract> --dry-run");
        Console.WriteLine("  config-sheet-forge bootstrap-target-branch-from-local-xlsx --all --manifest <project-config-or-contract> --target-branch main --dry-run");
        Console.WriteLine("  config-sheet-forge bootstrap-target-branch-from-local-xlsx --all --manifest <project-config-or-contract> --target-branch main --preview-result <dry-run-result.json> --confirm-create-online-sheets --confirm-registry-upsert --confirm-schema-reviews");
        Console.WriteLine("    apply flags: --confirm-create-online-sheets --confirm-registry-upsert --confirm-schema-reviews [--confirm-write-local-cache] [--confirm-write-project-config] [--confirm-excel-to-so]");
        Console.WriteLine("  config-sheet-forge merge --base <file> --ours <file> --theirs <file> [--out <report.md>]");
        Console.WriteLine("  config-sheet-forge submit-merge-review --request <contract.json> --preview-result <compare-merge.result.json> --confirm");
        Console.WriteLine("  config-sheet-forge gate [--cache <dir>] [--details] [--annotations github]");
        Console.WriteLine("  config-sheet-forge apply-contract --request <contract.json> [--out <result.json>] [--dry-run]");
        Console.WriteLine("  config-sheet-forge registry-migrate --base <token> [--only review-status-options] [--locale zh-Hans] [--dry-run] [--cleanup-default-rows] [--cleanup-default-fields] [--cleanup-duplicate-branch-bindings] [--yes]");
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine("Unknown command: " + command);
        PrintHelp();
        return 2;
    }

    private static void PrintCheck(bool ok, string okMessage, string repairMessage, ref bool hasError, bool warningOnly = false)
    {
        if (ok)
        {
            Console.WriteLine("[ok] " + okMessage);
            return;
        }

        Console.WriteLine((warningOnly ? "[warn] " : "[error] ") + repairMessage);
        if (!warningOnly)
        {
            hasError = true;
        }
    }

    private static void PrintFinding(ProviderDoctorFinding finding, bool details)
    {
        Console.WriteLine("[" + finding.Severity.ToString().ToLowerInvariant() + "] " + finding.Message + " (" + finding.Code + ")");
        if (details)
        {
            foreach (var pair in finding.Details)
            {
                Console.WriteLine("  " + pair.Key + ": " + MaskIfSensitive(pair.Key, pair.Value));
            }
        }
    }

    private static string FormatProviderFindingForSummary(string tableId, ProviderDoctorFinding finding)
    {
        var text = tableId + ": " + finding.Code + " " + finding.Message;
        if (finding.Details == null || finding.Details.Count == 0)
        {
            return text;
        }

        var interesting = new[]
        {
            "attemptedRange",
            "retryRange",
            "finalRange",
            "onlineRows",
            "onlineColumns",
            "xlsxRows",
            "xlsxColumns",
            "xlsxDimensionRows",
            "xlsxDimensionColumns",
            "xlsxCellRows",
            "xlsxCellColumns",
            "larkErrorCode",
            "larkErrorMessage"
        };
        var details = interesting
            .Where(finding.Details.ContainsKey)
            .Select(key => key + "=" + finding.Details[key])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        return details.Count == 0 ? text : text + " [" + string.Join("; ", details) + "]";
    }

    private static IList<ProviderDoctorFinding> CollapseNoisyProviderFindings(string tableId, IEnumerable<ProviderDoctorFinding> findings)
    {
        var result = new List<ProviderDoctorFinding>();
        foreach (var group in (findings ?? Enumerable.Empty<ProviderDoctorFinding>())
            .GroupBy(f => f.Code == "cell.bool_invalid" ? "cell.bool_invalid:" + ExtractColumnFromLocation(GetDetail(f, "location")) : Guid.NewGuid().ToString("N"), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            if (!string.Equals(first.Code, "cell.bool_invalid", StringComparison.OrdinalIgnoreCase) || group.Count() == 1)
            {
                result.AddRange(group);
                continue;
            }

            var column = FirstNonEmpty(ExtractColumnFromLocation(GetDetail(first, "location")), "布尔字段");
            var examples = group.Select(f => ExtractRowFromLocation(GetDetail(f, "location")))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
            var collapsed = new ProviderDoctorFinding
            {
                Severity = FindingSeverity.Warning,
                Code = "cell.bool_invalid",
                Message = tableId + ": " + column + " 列有 " + group.Count().ToString(CultureInfo.InvariantCulture) + " 个布尔值无法识别，示例行 " + (examples.Count > 0 ? string.Join(", ", examples) : "未知") + "，已保留原文。"
            };
            collapsed.Details["count"] = group.Count().ToString(CultureInfo.InvariantCulture);
            collapsed.Details["column"] = column;
            collapsed.Details["exampleRows"] = string.Join(", ", examples);
            collapsed.Details["rawExamples"] = string.Join(", ", group.Select(f => GetDetail(f, "raw")).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Take(5));
            result.Add(collapsed);
        }

        return result;
    }

    private static void PrintValidation(ValidationFinding finding, bool details)
    {
        Console.WriteLine("[" + finding.Severity.ToString().ToLowerInvariant() + "] " + finding.Message + " (" + finding.Code + ") " + finding.Location);
        if (details)
        {
            foreach (var pair in finding.Details)
            {
                Console.WriteLine("  " + pair.Key + ": " + MaskIfSensitive(pair.Key, pair.Value));
            }
        }
    }

    private static IList<ValidationFinding> CollapseNoisyValidationFindings(string tableId, IEnumerable<ValidationFinding> findings)
    {
        var result = new List<ValidationFinding>();
        foreach (var group in (findings ?? Enumerable.Empty<ValidationFinding>())
            .GroupBy(f => f.Code == "cell.bool_invalid" ? "cell.bool_invalid:" + ExtractColumnFromLocation(f.Location) : Guid.NewGuid().ToString("N"), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            if (!string.Equals(first.Code, "cell.bool_invalid", StringComparison.OrdinalIgnoreCase) || group.Count() == 1)
            {
                result.AddRange(group);
                continue;
            }

            var column = FirstNonEmpty(ExtractColumnFromLocation(first.Location), "布尔字段");
            var examples = group.Select(f => ExtractRowFromLocation(f.Location))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
            var collapsed = new ValidationFinding
            {
                Severity = FindingSeverity.Warning,
                Code = "cell.bool_invalid",
                Message = tableId + ": " + column + " 列有 " + group.Count().ToString(CultureInfo.InvariantCulture) + " 个布尔值无法识别，示例行 " + (examples.Count > 0 ? string.Join(", ", examples) : "未知") + "，已保留原文。",
                Location = tableId
            };
            collapsed.Details["count"] = group.Count().ToString(CultureInfo.InvariantCulture);
            collapsed.Details["column"] = column;
            collapsed.Details["exampleRows"] = string.Join(", ", examples);
            collapsed.Details["rawExamples"] = string.Join(", ", group.Select(f => GetDetail(f, "raw")).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Take(5));
            result.Add(collapsed);
        }

        return result;
    }

    private static string ExtractColumnFromLocation(string location)
    {
        var text = location ?? "";
        var marker = ".cells[";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return "";
        }

        start += marker.Length;
        var end = text.IndexOf(']', start);
        return end > start ? text.Substring(start, end - start) : "";
    }

    private static string ExtractRowFromLocation(string location)
    {
        var text = location ?? "";
        var marker = ".rows[";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return "";
        }

        start += marker.Length;
        var end = text.IndexOf(']', start);
        return end > start ? text.Substring(start, end - start) : "";
    }

    private static string GetDetail(ProviderDoctorFinding finding, string key)
    {
        return finding != null && finding.Details.TryGetValue(key, out var value) ? value : "";
    }

    private static string GetDetail(ValidationFinding finding, string key)
    {
        return finding != null && finding.Details.TryGetValue(key, out var value) ? value : "";
    }

    private static void PrintGitHubAnnotation(string file, ValidationFinding finding)
    {
        var command = finding.Severity == FindingSeverity.Error ? "error" : "warning";
        var title = EscapeGitHubAnnotation(finding.Code);
        var message = EscapeGitHubAnnotation(finding.Message + " " + finding.Location);
        Console.WriteLine("::" + command + " file=" + EscapeGitHubAnnotation(file) + ",line=1,title=" + title + "::" + message);
    }

    private static string RenderMergeReport(MergeReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Config Sheet Merge Report");
        builder.AppendLine();
        builder.AppendLine("- conflicts: " + report.Entries.Count(e => e.Status == MergeEntryStatus.Conflict));
        builder.AppendLine("- took ours: " + report.Entries.Count(e => e.Status == MergeEntryStatus.TookOurs));
        builder.AppendLine("- took theirs: " + report.Entries.Count(e => e.Status == MergeEntryStatus.TookTheirs));
        builder.AppendLine();
        builder.AppendLine("| Status | Sheet | Row | Column | Message |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var entry in report.Entries)
        {
            builder.Append("| ")
                .Append(entry.Status).Append(" | ")
                .Append(EscapeMarkdown(entry.Sheet)).Append(" | ")
                .Append(EscapeMarkdown(entry.RowId)).Append(" | ")
                .Append(EscapeMarkdown(entry.ColumnKey)).Append(" | ")
                .Append(EscapeMarkdown(entry.Message)).AppendLine(" |");
        }

        return builder.ToString();
    }

    private static string EscapeMarkdown(string value)
    {
        return (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    private static string EscapeGitHubAnnotation(string value)
    {
        return (value ?? "")
            .Replace("%", "%25")
            .Replace("\r", "%0D")
            .Replace("\n", "%0A")
            .Replace(":", "%3A")
            .Replace(",", "%2C");
    }

    private static string StripUtf8Bom(string value)
    {
        return !string.IsNullOrEmpty(value) && value[0] == '\uFEFF'
            ? value.Substring(1)
            : value;
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        try
        {
            var normalizedPath = NormalizeFilePathArgument(path);
            var json = StripUtf8Bom(await File.ReadAllTextAsync(normalizedPath, Utf8NoBom));
            var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (value == null)
            {
                throw new CliException("Could not read JSON from " + normalizedPath, 2);
            }

            return value;
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            throw new CliException("读取 JSON 文件失败：路径格式不合法。请升级 Desktop/CLI 或重试。", 2, path + Environment.NewLine + ex.Message);
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        try
        {
            var normalizedPath = NormalizeFilePathArgument(path);
            var directory = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
            await File.WriteAllTextAsync(normalizedPath, json, Utf8NoBom);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            throw new CliException("生成结果文件失败：路径格式不合法。请升级 Desktop/CLI 或重试。", 1, path + Environment.NewLine + ex.Message);
        }
    }

    private static string NormalizeFilePathArgument(string path)
    {
        var normalized = (path ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        if (OperatingSystem.IsWindows())
        {
            const string VerbatimUncPrefix = @"\\?\UNC\";
            const string VerbatimPrefix = @"\\?\";
            if (normalized.StartsWith(VerbatimUncPrefix, StringComparison.Ordinal))
            {
                normalized = @"\\" + normalized.Substring(VerbatimUncPrefix.Length);
            }
            else if (normalized.StartsWith(VerbatimPrefix, StringComparison.Ordinal))
            {
                normalized = normalized.Substring(VerbatimPrefix.Length);
            }

            normalized = normalized.Replace('/', Path.DirectorySeparatorChar);
        }

        return Path.GetFullPath(normalized);
    }

    private static bool IsPathFailure(Exception ex)
    {
        return ex is IOException ||
               ex is UnauthorizedAccessException ||
               ex is NotSupportedException ||
               ex is ArgumentException ||
               ex is System.Security.SecurityException;
    }

    private static async Task<bool> WriteCacheIfChangedAsync(string cacheDirectory, string tableId, WorkbookDocument workbook, string hash, string? tempXlsxPath, string? xlsxDirectory = null)
    {
        var table = new TableConfig { Id = tableId };
        return await WriteCacheIfChangedAsync(null, cacheDirectory, table, workbook, hash, tempXlsxPath, xlsxDirectory).ConfigureAwait(false);
    }

    private static async Task<bool> WriteCacheIfChangedAsync(Workspace? workspace, string cacheDirectory, TableConfig table, WorkbookDocument workbook, string hash, string? tempXlsxPath, string? xlsxDirectory = null)
    {
        Directory.CreateDirectory(cacheDirectory);
        var xlsxRoot = FirstNonEmpty(xlsxDirectory ?? "", cacheDirectory);
        Directory.CreateDirectory(xlsxRoot);
        var tableId = table.Id;
        var semanticPath = Path.Combine(cacheDirectory, tableId + ".semantic.json");
        var shaPath = Path.Combine(cacheDirectory, tableId + ".sha256");
        var xlsxPath = Path.Combine(xlsxRoot, MakeSafeFileName(tableId) + ".xlsx");
        var existingHash = await ReadExistingHashAsync(shaPath);
        var hasRequiredFiles = File.Exists(semanticPath) && (string.IsNullOrWhiteSpace(tempXlsxPath) || File.Exists(xlsxPath));
        var needsDialectRewrite = table.UseExcelToSoCacheDialect && hasRequiredFiles && CacheXlsxNeedsExcelToSoDialectRewrite(xlsxPath, table, workspace == null ? Directory.GetCurrentDirectory() : workspace.Root);
        if (string.Equals(existingHash, hash, StringComparison.Ordinal) && hasRequiredFiles && !needsDialectRewrite)
        {
            return false;
        }

        await WriteJsonAsync(semanticPath, workbook);
        await File.WriteAllTextAsync(shaPath, hash + Environment.NewLine, Utf8NoBom);
        if (!string.IsNullOrWhiteSpace(tempXlsxPath) && File.Exists(tempXlsxPath))
        {
            if (table.UseExcelToSoCacheDialect)
            {
                var plan = BuildExcelToSoCacheDialectPlan(workbook, table, workspace == null ? Directory.GetCurrentDirectory() : workspace.Root);
                if (plan.Errors.Count > 0)
                {
                    throw new CliException("无法写入 ExcelToSO cache xlsx：" + string.Join("；", plan.Errors), 2);
                }

                WriteExcelToSoCacheXlsx(xlsxPath, workbook, table, plan.TypeRow, workspace == null ? Directory.GetCurrentDirectory() : workspace.Root);
            }
            else
            {
                File.Copy(tempXlsxPath, xlsxPath, overwrite: true);
            }
        }

        return true;
    }

    private sealed class ExcelToSoCacheDialectPlan
    {
        public List<string> TypeRow { get; } = new();
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    private sealed class XlsxTypeHint
    {
        public int ColumnIndex { get; set; }
        public string Key { get; set; } = "";
        public string TypeText { get; set; } = "";
    }

    private sealed class ExcelToSoPhysicalTemplate
    {
        public bool Available { get; set; }
        public string SheetName { get; set; } = "";
        public List<string> Fields { get; } = new();
        public List<string> Types { get; } = new();
        public List<string> Descriptions { get; } = new();
    }

    private sealed class ExcelToSoPhysicalColumn
    {
        public string FieldName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string Description { get; set; } = "";
        public int SemanticColumnIndex { get; set; } = -1;
        public string SemanticKey { get; set; } = "";
    }

    private sealed class XlsxSheetPathInfo
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }

    private static ExcelToSoCacheDialectPlan BuildExcelToSoCacheDialectPlan(WorkbookDocument workbook, TableConfig table, string workspaceRoot)
    {
        var plan = new ExcelToSoCacheDialectPlan();
        var sheet = workbook.Sheets.FirstOrDefault();
        if (sheet == null)
        {
            plan.Errors.Add("没有可写入 ExcelToSO cache 的工作表。");
            return plan;
        }

        var sourceHints = ReadExcelToSoTypeHintsFromSource(table, workspaceRoot);
        var fieldSpecsByKey = table.Fields
            .Where(f => !string.IsNullOrWhiteSpace(FirstNonEmpty(f.Key, f.DisplayName)))
            .GroupBy(f => NormalizeFieldKey(FirstNonEmpty(f.Key, f.DisplayName)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            var column = sheet.Columns[i];
            var key = NormalizeFieldKey(FirstNonEmpty(column.Key, column.DisplayName));
            fieldSpecsByKey.TryGetValue(key, out var fieldSpec);
            var configuredType = fieldSpec == null ? "" : FirstNonEmpty(fieldSpec.ExcelToSoType, fieldSpec.OriginalType);
            var sourceType = FindSourceTypeHint(sourceHints, key, i);
            var preferredType = FirstNonEmpty(configuredType, sourceType);
            if (TryResolveExcelToSoDialectType(column.ValueKind, preferredType, out var excelToSoType, out var error))
            {
                plan.TypeRow.Add(excelToSoType);
                continue;
            }

            var columnLabel = FirstNonEmpty(column.Key, column.DisplayName, "第 " + (i + 1).ToString(CultureInfo.InvariantCulture) + " 列");
            plan.TypeRow.Add("");
            plan.Errors.Add("字段 " + columnLabel + " 的 Source of Truth 类型是 " + FirstNonEmpty(column.ValueKind, "(空)") + "，无法还原成 ExcelToSO 可导入类型。" + error + " 请在 project config/adapter schema 中为该字段声明 excelToSoType/originalType，例如 int[]、float[]、string[] 或 string。");
        }

        return plan;
    }

    private static bool TryResolveExcelToSoDialectType(string canonicalKind, string rawTypeHint, out string excelToSoType, out string error)
    {
        excelToSoType = "";
        error = "";
        if (!string.IsNullOrWhiteSpace(rawTypeHint))
        {
            if (TryNormalizeExcelToSoTypeToken(rawTypeHint, out excelToSoType, out error))
            {
                return true;
            }

            if (IsJsonType(rawTypeHint))
            {
                error = "当前提示仍是 json，ExcelToSO 不能自动判断它是 int[]、float[]、string[] 还是 string。";
                return false;
            }

            if (!IsPortableOnlyType(rawTypeHint))
            {
                excelToSoType = rawTypeHint.Trim();
                return true;
            }
        }

        switch ((canonicalKind ?? "").Trim().ToLowerInvariant())
        {
            case "string":
                excelToSoType = "string";
                return true;
            case "integer":
                excelToSoType = "int";
                return true;
            case "number":
                excelToSoType = "float";
                return true;
            case "bool":
                excelToSoType = "bool";
                return true;
            case "json":
                error = "json 必须由旧 Excel 类型、registry schema 或字段 originalType/excelToSoType 显式定型。";
                return false;
            case "date":
            case "datetime":
            case "enum":
                error = "ExcelToSO 默认不支持 " + canonicalKind + "；请显式选择 string 或项目内可用类型。";
                return false;
            default:
                error = "未识别类型。";
                return false;
        }
    }

    private static bool TryNormalizeExcelToSoTypeToken(string rawType, out string excelToSoType, out string error)
    {
        excelToSoType = "";
        error = "";
        var normalized = (rawType ?? "").Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "bool":
            case "boolean":
                excelToSoType = "bool";
                return true;
            case "int":
            case "int32":
            case "integer":
                excelToSoType = "int";
                return true;
            case "ints":
            case "int[]":
            case "[int]":
            case "int32s":
            case "int32[]":
            case "[int32]":
            case "integers":
            case "integer[]":
            case "[integer]":
                excelToSoType = "int[]";
                return true;
            case "float":
            case "double":
            case "decimal":
            case "number":
                excelToSoType = "float";
                return true;
            case "floats":
            case "float[]":
            case "[float]":
            case "doubles":
            case "double[]":
            case "[double]":
            case "numbers":
            case "number[]":
            case "[number]":
                excelToSoType = "float[]";
                return true;
            case "long":
            case "int64":
                excelToSoType = "long";
                return true;
            case "longs":
            case "long[]":
            case "[long]":
            case "int64s":
            case "int64[]":
            case "[int64]":
                excelToSoType = "long[]";
                return true;
            case "string":
            case "str":
            case "text":
                excelToSoType = "string";
                return true;
            case "strings":
            case "string[]":
            case "[string]":
            case "texts":
            case "text[]":
            case "[text]":
                excelToSoType = "string[]";
                return true;
            case "lang":
            case "language":
                excelToSoType = "lang";
                return true;
            case "langs":
            case "lang[]":
            case "[lang]":
            case "languages":
            case "language[]":
            case "[language]":
                excelToSoType = "lang[]";
                return true;
            case "rich":
                excelToSoType = "rich";
                return true;
            case "richs":
            case "riches":
            case "rich[]":
            case "[rich]":
                excelToSoType = "rich[]";
                return true;
            case "vector2":
            case "vector3":
            case "vector4":
            case "rect":
            case "rectangle":
            case "color":
            case "colour":
                excelToSoType = normalized == "rectangle" ? "rect" : normalized == "colour" ? "color" : normalized;
                return true;
            case "json":
                error = "json 不能直接写入 ExcelToSO cache。";
                return false;
            default:
                return false;
        }
    }

    private static bool IsPortableOnlyType(string rawType)
    {
        switch ((rawType ?? "").Trim().ToLowerInvariant())
        {
            case "date":
            case "datetime":
            case "date_time":
            case "timestamp":
            case "enum":
                return true;
            default:
                return false;
        }
    }

    private static bool IsJsonType(string rawType)
    {
        return string.Equals((rawType ?? "").Trim(), "json", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindSourceTypeHint(IList<XlsxTypeHint> hints, string key, int columnIndex)
    {
        var byKey = hints.FirstOrDefault(h => !string.IsNullOrWhiteSpace(key) && string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));
        if (byKey != null)
        {
            return byKey.TypeText;
        }

        var byIndex = hints.FirstOrDefault(h => h.ColumnIndex == columnIndex);
        return byIndex == null ? "" : byIndex.TypeText;
    }

    private static List<XlsxTypeHint> ReadExcelToSoTypeHintsFromSource(TableConfig table, string workspaceRoot)
    {
        var hints = new List<XlsxTypeHint>();
        var sourcePath = ResolvePathAgainstRoot(workspaceRoot, table.LocalSourcePath);
        var typeRowIndex = EffectiveTypeRow(table);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath) || typeRowIndex < 0)
        {
            return hints;
        }

        try
        {
            var matrix = ReadFirstWorksheetMatrix(sourcePath);
            if (typeRowIndex >= matrix.Count)
            {
                return hints;
            }

            var fieldRow = table.FieldRow >= 0 && table.FieldRow < matrix.Count ? matrix[table.FieldRow] : new List<string>();
            var typeRow = matrix[typeRowIndex];
            for (var i = 0; i < typeRow.Count; i++)
            {
                var key = i < fieldRow.Count ? NormalizeFieldKey(fieldRow[i]) : "";
                var typeText = typeRow[i] ?? "";
                if (string.IsNullOrWhiteSpace(typeText))
                {
                    continue;
                }

                hints.Add(new XlsxTypeHint { ColumnIndex = i, Key = key, TypeText = typeText.Trim() });
            }
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
        {
            // Source xlsx hints are best effort. The sync result will block only if a json column cannot be typed.
        }

        return hints;
    }

    private static ExcelToSoPhysicalTemplate ReadExcelToSoPhysicalTemplate(TableConfig table, string workspaceRoot)
    {
        var template = new ExcelToSoPhysicalTemplate();
        var sourcePath = ResolvePathAgainstRoot(workspaceRoot, table.LocalSourcePath);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return template;
        }

        try
        {
            template.SheetName = ReadFirstWorksheetName(sourcePath);
            var matrix = ReadFirstWorksheetMatrix(sourcePath);
            var fieldRow = table.FieldRow >= 0 && table.FieldRow < matrix.Count ? matrix[table.FieldRow] : new List<string>();
            var typeRowIndex = EffectiveTypeRow(table);
            var typeRow = typeRowIndex >= 0 && typeRowIndex < matrix.Count ? matrix[typeRowIndex] : new List<string>();
            var descriptionRowIndex = table.DescriptionRow >= 0 ? table.DescriptionRow : typeRowIndex + 1;
            var descriptionRow = descriptionRowIndex >= 0 && descriptionRowIndex < matrix.Count ? matrix[descriptionRowIndex] : new List<string>();
            for (var i = 0; i < fieldRow.Count; i++)
            {
                var field = fieldRow[i] ?? "";
                template.Fields.Add(field);
                template.Types.Add(i < typeRow.Count ? typeRow[i] : "");
                template.Descriptions.Add(i < descriptionRow.Count ? descriptionRow[i] : "");
                if (string.IsNullOrWhiteSpace(field))
                {
                    break;
                }
            }

            template.Available = template.Fields.Any(field => !string.IsNullOrWhiteSpace(field));
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is XmlException)
        {
            // Source xlsx templates are best effort. The import preflight will block if the final cache is not compatible.
        }

        return template;
    }

    private static bool CacheXlsxNeedsExcelToSoDialectRewrite(string path, TableConfig table, string workspaceRoot)
    {
        var typeRowIndex = EffectiveTypeRow(table);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || typeRowIndex < 0)
        {
            return false;
        }

        try
        {
            if (CacheXlsxNeedsExcelToSoOpenXmlRewrite(path))
            {
                return true;
            }

            var matrix = ReadFirstWorksheetMatrix(path);
            if (typeRowIndex >= matrix.Count)
            {
                return false;
            }

            foreach (var token in matrix[typeRowIndex])
            {
                if (TypeTokenShouldBeRewrittenForExcelToSo(token))
                {
                    return true;
                }
            }

            if (CacheXlsxNeedsExcelToSoPhysicalTemplateRewrite(path, table, workspaceRoot))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool CacheXlsxNeedsExcelToSoPhysicalTemplateRewrite(string path, TableConfig table, string workspaceRoot)
    {
        var template = ReadExcelToSoPhysicalTemplate(table, workspaceRoot);
        try
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                var sharedStrings = ReadSharedStrings(archive);
                var sheets = ReadWorkbookSheetInfos(archive);
                if (sheets.Count == 0)
                {
                    return true;
                }

                if (template.Available && !string.IsNullOrWhiteSpace(template.SheetName) &&
                    !string.Equals(sheets[0].Name, SanitizeSheetName(template.SheetName), StringComparison.Ordinal))
                {
                    return true;
                }

                var matrix = ReadWorksheetMatrix(archive, sheets[0].Path, sharedStrings);
                var fieldRowIndex = table.FieldRow < 0 ? 0 : table.FieldRow;
                if (fieldRowIndex >= matrix.Count)
                {
                    return template.Available;
                }

                var fieldRow = matrix[fieldRowIndex];
                if (fieldRow.Any(IsGeneratedColumnName))
                {
                    return true;
                }

                if (template.Available)
                {
                    var expectedFields = template.Fields.TakeWhile(field => !string.IsNullOrWhiteSpace(field)).ToList();
                    for (var i = 0; i < expectedFields.Count; i++)
                    {
                        var actual = i < fieldRow.Count ? fieldRow[i] : "";
                        if (!string.Equals(actual, expectedFields[i], StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }

                var dataStartRow = table.DataStartRow < 0
                    ? Math.Max(fieldRowIndex, Math.Max(EffectiveTypeRow(table), table.DescriptionRow < 0 ? EffectiveTypeRow(table) + 1 : table.DescriptionRow)) + 1
                    : table.DataStartRow;
                return dataStartRow >= 0 && dataStartRow < matrix.Count && LooksLikeTypeTokenRow(matrix[dataStartRow]);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is XmlException)
        {
            return false;
        }
    }

    private static bool CacheXlsxNeedsExcelToSoOpenXmlRewrite(string path)
    {
        try
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                if (archive.GetEntry("xl/sharedStrings.xml") == null || archive.GetEntry("xl/styles.xml") == null)
                {
                    return true;
                }

                var workbook = ReadWorkbookSheets(archive);
                var sheetPath = workbook.Count == 0 ? "xl/worksheets/sheet1.xml" : workbook[0];
                var entry = archive.GetEntry(sheetPath);
                if (entry == null)
                {
                    return true;
                }

                var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                return ReadXml(entry).Descendants(ns + "c")
                    .Any(cell => string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is XmlException)
        {
            return true;
        }
    }

    private static int EffectiveTypeRow(TableConfig table)
    {
        if (table.TypeRow >= 0)
        {
            return table.TypeRow;
        }

        return (table.FieldRow < 0 ? 0 : table.FieldRow) + 1;
    }

    private static bool TypeTokenShouldBeRewrittenForExcelToSo(string token)
    {
        switch ((token ?? "").Trim().ToLowerInvariant())
        {
            case "integer":
            case "integers":
            case "integer[]":
            case "[integer]":
            case "number":
            case "numbers":
            case "number[]":
            case "[number]":
            case "boolean":
            case "text":
            case "texts":
            case "text[]":
            case "[text]":
            case "json":
            case "date":
            case "datetime":
            case "date_time":
            case "timestamp":
            case "enum":
                return true;
            default:
                return false;
        }
    }

    private static void WriteExcelToSoCacheXlsx(string path, WorkbookDocument workbook, TableConfig table, IList<string> typeRow, string workspaceRoot)
    {
        var sheet = workbook.Sheets.FirstOrDefault();
        if (sheet == null)
        {
            throw new CliException("无法写入 ExcelToSO cache xlsx：workbook 没有 sheet。", 2);
        }

        var template = ReadExcelToSoPhysicalTemplate(table, workspaceRoot);
        var rows = BuildExcelToSoCacheRows(sheet, table, typeRow, template);
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
        {
            var sharedStrings = BuildSharedStringTable(rows);
            WriteZipText(archive, "[Content_Types].xml", BuildContentTypesXml());
            WriteZipText(archive, "_rels/.rels", BuildRootRelsXml());
            WriteZipText(archive, "xl/workbook.xml", BuildWorkbookXml(SanitizeSheetName(FirstNonEmpty(template.SheetName, sheet.Name, table.Name, table.Id))));
            WriteZipText(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelsXml());
            WriteZipText(archive, "xl/styles.xml", BuildStylesXml());
            WriteZipText(archive, "xl/sharedStrings.xml", BuildSharedStringsXml(sharedStrings.Values));
            WriteZipText(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(rows, sharedStrings.Indexes));
        }

        if (!ValidateExcelToSoCompatibleXlsx(tempPath, table, workspaceRoot, out var validationError))
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup; the caller reports the validation failure.
            }

            throw new CliException("生成的 cache xlsx 不能被 ExcelToSO 读取：" + validationError, 2);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }

    private static List<List<string>> BuildExcelToSoCacheRows(SheetDocument sheet, TableConfig table, IList<string> typeRow, ExcelToSoPhysicalTemplate template)
    {
        var fieldRow = table.FieldRow < 0 ? 0 : table.FieldRow;
        var typeRowIndex = table.TypeRow < 0 ? fieldRow + 1 : table.TypeRow;
        var descriptionRow = table.DescriptionRow < 0 ? typeRowIndex + 1 : table.DescriptionRow;
        var dataStartRow = table.DataStartRow < 0 ? Math.Max(Math.Max(fieldRow, typeRowIndex), descriptionRow) + 1 : table.DataStartRow;
        var totalHeaderRows = Math.Max(dataStartRow, Math.Max(Math.Max(fieldRow, typeRowIndex), descriptionRow) + 1);
        var columns = BuildExcelToSoPhysicalColumns(sheet, typeRow, template);
        var rows = new List<List<string>>();
        for (var i = 0; i < totalHeaderRows; i++)
        {
            rows.Add(Enumerable.Repeat("", columns.Count).ToList());
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            rows[fieldRow][i] = column.FieldName;
            rows[typeRowIndex][i] = column.TypeName;
            rows[descriptionRow][i] = column.Description;
        }

        foreach (var row in sheet.Rows.OrderBy(r => r.SourceIndex <= 0 ? int.MaxValue : r.SourceIndex))
        {
            if (ShouldSkipExcelToSoCacheDataRow(row, sheet, columns, dataStartRow, rows[typeRowIndex], rows[descriptionRow]))
            {
                continue;
            }

            var output = new List<string>();
            foreach (var column in columns)
            {
                if (!string.IsNullOrWhiteSpace(column.SemanticKey) && row.Cells.TryGetValue(column.SemanticKey, out var cell))
                {
                    output.Add(ConvertCellToExcelToSoPhysicalText(cell, column.TypeName));
                }
                else
                {
                    output.Add("");
                }
            }

            rows.Add(output);
        }

        return rows;
    }

    private static List<ExcelToSoPhysicalColumn> BuildExcelToSoPhysicalColumns(SheetDocument sheet, IList<string> typeRow, ExcelToSoPhysicalTemplate template)
    {
        var semanticByKey = sheet.Columns
            .Select((column, index) => new { Column = column, Index = index, Key = NormalizeFieldKey(FirstNonEmpty(column.Key, column.DisplayName)) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var columns = new List<ExcelToSoPhysicalColumn>();
        if (template.Available && template.Fields.Count > 0)
        {
            for (var i = 0; i < template.Fields.Count; i++)
            {
                var fieldName = template.Fields[i];
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    break;
                }

                if (fieldName.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                semanticByKey.TryGetValue(NormalizeFieldKey(fieldName), out var semantic);
                var semanticIndex = semantic?.Index ?? -1;
                var plannedType = semanticIndex >= 0 && semanticIndex < typeRow.Count ? typeRow[semanticIndex] : "";
                var templateType = i < template.Types.Count ? template.Types[i] : "";
                columns.Add(new ExcelToSoPhysicalColumn
                {
                    FieldName = fieldName,
                    TypeName = ChoosePhysicalTypeName(templateType, plannedType),
                    Description = i < template.Descriptions.Count ? template.Descriptions[i] : "",
                    SemanticColumnIndex = semanticIndex,
                    SemanticKey = semantic?.Column.Key ?? ""
                });
            }

            return columns;
        }

        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            var column = sheet.Columns[i];
            columns.Add(new ExcelToSoPhysicalColumn
            {
                FieldName = FirstNonEmpty(column.SourceColumn, column.DisplayName, column.Key),
                TypeName = i < typeRow.Count ? typeRow[i] : "string",
                Description = column.Details.TryGetValue("description", out var description) ? description : "",
                SemanticColumnIndex = i,
                SemanticKey = column.Key
            });
        }

        return columns;
    }

    private static string ChoosePhysicalTypeName(string templateType, string plannedType)
    {
        if (!string.IsNullOrWhiteSpace(templateType) &&
            TryNormalizeExcelToSoTypeToken(templateType, out var templateNormalized, out _) &&
            (string.IsNullOrWhiteSpace(plannedType) ||
             !TryNormalizeExcelToSoTypeToken(plannedType, out var plannedNormalized, out _) ||
             string.Equals(templateNormalized, plannedNormalized, StringComparison.OrdinalIgnoreCase)))
        {
            return templateType;
        }

        return FirstNonEmpty(plannedType, templateType, "string");
    }

    private static bool ShouldSkipExcelToSoCacheDataRow(RowDocument row, SheetDocument sheet, IList<ExcelToSoPhysicalColumn> columns, int dataStartRow, IList<string> typeRow, IList<string> descriptionRow)
    {
        if (row.SourceIndex > 0 && row.SourceIndex <= dataStartRow)
        {
            return true;
        }

        var values = columns
            .Select(column => !string.IsNullOrWhiteSpace(column.SemanticKey) && row.Cells.TryGetValue(column.SemanticKey, out var cell)
                ? FirstNonEmpty(cell.RawText, cell.NormalizedText, cell.SemanticText)
                : "")
            .ToList();
        var nonEmpty = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (nonEmpty.Count == 0)
        {
            return true;
        }

        if (LooksLikeTypeTokenRow(nonEmpty))
        {
            return true;
        }

        var descriptionMatches = 0;
        for (var i = 0; i < Math.Min(values.Count, descriptionRow.Count); i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]) &&
                !string.IsNullOrWhiteSpace(descriptionRow[i]) &&
                string.Equals(values[i].Trim(), descriptionRow[i].Trim(), StringComparison.OrdinalIgnoreCase))
            {
                descriptionMatches++;
            }
        }

        return descriptionMatches > 0 && descriptionMatches >= Math.Max(1, nonEmpty.Count / 2);
    }

    private static bool LooksLikeTypeTokenRow(IList<string> values)
    {
        var nonEmpty = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (nonEmpty.Count == 0)
        {
            return false;
        }

        var typeLike = nonEmpty.Count(IsExcelToSoOrPortableTypeToken);
        return typeLike == nonEmpty.Count || (nonEmpty.Count >= 3 && typeLike >= nonEmpty.Count - 1);
    }

    private static bool IsExcelToSoOrPortableTypeToken(string value)
    {
        if (TryNormalizeExcelToSoTypeToken(value, out _, out _))
        {
            return true;
        }

        return IsPortableOnlyType(value) || IsJsonType(value);
    }

    private static string ConvertCellToExcelToSoPhysicalText(CellValue cell, string physicalType)
    {
        var raw = FirstNonEmpty(cell.RawText, cell.NormalizedText, cell.SemanticText);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var normalizedType = "";
        TryNormalizeExcelToSoTypeToken(physicalType, out normalizedType, out _);
        if (!IsExcelToSoArrayType(normalizedType))
        {
            return TryUnquoteJsonScalar(raw, out var scalar) ? scalar : raw;
        }

        if (TryParseJsonArrayAsExcelToSoList(raw, out var listText))
        {
            return listText;
        }

        return raw;
    }

    private static bool IsExcelToSoArrayType(string type)
    {
        switch ((type ?? "").Trim().ToLowerInvariant())
        {
            case "int[]":
            case "float[]":
            case "string[]":
            case "bool[]":
            case "long[]":
            case "lang[]":
            case "rich[]":
                return true;
            default:
                return false;
        }
    }

    private static bool TryUnquoteJsonScalar(string raw, out string scalar)
    {
        scalar = "";
        try
        {
            var node = JsonNode.Parse(raw);
            if (node is JsonValue value)
            {
                scalar = value.ToString();
                return true;
            }
        }
        catch
        {
            // Not a JSON scalar; keep original cell text.
        }

        return false;
    }

    private static bool TryParseJsonArrayAsExcelToSoList(string raw, out string listText)
    {
        listText = "";
        try
        {
            var node = JsonNode.Parse(raw);
            if (node is not JsonArray array)
            {
                return false;
            }

            listText = string.Join(",", array.Select(JsonArrayItemToExcelToSoText));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string JsonArrayItemToExcelToSoText(JsonNode? item)
    {
        if (item == null)
        {
            return "";
        }

        if (item is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return text ?? "";
            }

            if (value.TryGetValue<bool>(out var boolean))
            {
                return boolean ? "true" : "false";
            }

            if (value.TryGetValue<long>(out var integer))
            {
                return integer.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<double>(out var number))
            {
                return number.ToString(CultureInfo.InvariantCulture);
            }
        }

        return item.ToJsonString();
    }

    private sealed class SharedStringTable
    {
        public Dictionary<string, int> Indexes { get; } = new(StringComparer.Ordinal);
        public List<string> Values { get; } = new();
    }

    private static SharedStringTable BuildSharedStringTable(IList<List<string>> rows)
    {
        var table = new SharedStringTable();
        foreach (var row in rows)
        {
            foreach (var value in row)
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (table.Indexes.ContainsKey(value))
                {
                    continue;
                }

                table.Indexes[value] = table.Values.Count;
                table.Values.Add(value);
            }
        }

        return table;
    }

    private static string BuildContentTypesXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
               "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
               "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
               "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
               "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
               "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
               "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
               "</Types>";
    }

    private static string BuildRootRelsXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
               "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
               "</Relationships>";
    }

    private static string BuildWorkbookXml(string sheetName)
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
               "<sheets><sheet name=\"" + EscapeXmlAttribute(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
               "</workbook>";
    }

    private static string BuildWorkbookRelsXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
               "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
               "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
               "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
               "</Relationships>";
    }

    private static string BuildStylesXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
               "<fonts count=\"1\"><font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font></fonts>" +
               "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
               "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
               "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
               "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
               "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
               "<dxfs count=\"0\"/><tableStyles count=\"0\" defaultTableStyle=\"TableStyleMedium9\" defaultPivotStyle=\"PivotStyleLight16\"/>" +
               "</styleSheet>";
    }

    private static string BuildSharedStringsXml(IList<string> values)
    {
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var root = new XElement(ns + "sst",
            new XAttribute("count", values.Count.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("uniqueCount", values.Count.ToString(CultureInfo.InvariantCulture)));
        foreach (var value in values)
        {
            var text = new XElement(ns + "t", value ?? "");
            if (!string.IsNullOrEmpty(value) && value.Length != value.Trim().Length)
            {
                text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }

            root.Add(new XElement(ns + "si", text));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildWorksheetXml(IList<List<string>> rows, IReadOnlyDictionary<string, int> sharedStringIndexes)
    {
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var sheetData = new XElement(ns + "sheetData");
        var rowCount = Math.Max(1, rows.Count);
        var columnCount = Math.Max(1, rows.Select(r => r.Count).DefaultIfEmpty(1).Max());
        for (var r = 0; r < rows.Count; r++)
        {
            var rowElement = new XElement(ns + "row", new XAttribute("r", (r + 1).ToString(CultureInfo.InvariantCulture)));
            var row = rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                var value = row[c] ?? "";
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!sharedStringIndexes.TryGetValue(value, out var sharedIndex))
                {
                    sharedIndex = 0;
                }
                rowElement.Add(new XElement(ns + "c",
                    new XAttribute("r", ToA1(c, r)),
                    new XAttribute("t", "s"),
                    new XElement(ns + "v", sharedIndex.ToString(CultureInfo.InvariantCulture))));
            }

            sheetData.Add(rowElement);
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ns + "worksheet",
                new XElement(ns + "dimension", new XAttribute("ref", "A1:" + ToA1(columnCount - 1, rowCount - 1))),
                sheetData)).ToString(SaveOptions.DisableFormatting);
    }

    private static bool ValidateExcelToSoCompatibleXlsx(string path, TableConfig table, string workspaceRoot, out string error)
    {
        error = "";
        try
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                var required = new[]
                {
                    "[Content_Types].xml",
                    "_rels/.rels",
                    "xl/workbook.xml",
                    "xl/_rels/workbook.xml.rels",
                    "xl/worksheets/sheet1.xml",
                    "xl/sharedStrings.xml",
                    "xl/styles.xml"
                };
                var missing = required.Where(entry => archive.GetEntry(entry) == null).ToList();
                if (missing.Count > 0)
                {
                    error = "xlsx 缺少 ExcelDataReader 需要的 OpenXML 部件：" + string.Join(", ", missing);
                    return false;
                }

                var sharedStrings = ReadSharedStrings(archive);
                var sheets = ReadWorkbookSheets(archive);
                if (sheets.Count == 0)
                {
                    error = "xlsx workbook 没有声明 worksheet。";
                    return false;
                }

                var matrix = ReadWorksheetMatrix(archive, sheets[0], sharedStrings);
                if (matrix.Count == 0)
                {
                    error = "xlsx 第一张 worksheet 没有可读取的单元格。";
                    return false;
                }
            }

            if (!ValidateExcelToSoPhysicalTemplate(path, table, workspaceRoot, out var physicalError))
            {
                error = physicalError;
                return false;
            }

            if (!TryValidateWithLegacyExcelReader(path, workspaceRoot, out var readerError))
            {
                error = readerError;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is XmlException || ex is TargetInvocationException || ex is BadImageFormatException)
        {
            error = ex is TargetInvocationException target && target.InnerException != null ? target.InnerException.Message : ex.Message;
            return false;
        }
    }

    private static bool ValidateExcelToSoPhysicalTemplate(string path, TableConfig table, string workspaceRoot, out string error)
    {
        error = "";
        var template = ReadExcelToSoPhysicalTemplate(table, workspaceRoot);
        using (var archive = ZipFile.OpenRead(path))
        {
            var sharedStrings = ReadSharedStrings(archive);
            var sheets = ReadWorkbookSheetInfos(archive);
            if (sheets.Count == 0)
            {
                error = "cache xlsx 没有 worksheet，ExcelToSO 无法导入。";
                return false;
            }

            var sheetName = sheets[0].Name;
            var matrix = ReadWorksheetMatrix(archive, sheets[0].Path, sharedStrings);
            if (template.Available && !string.IsNullOrWhiteSpace(template.SheetName) &&
                !string.Equals(sheetName, SanitizeSheetName(template.SheetName), StringComparison.Ordinal))
            {
                error = "cache xlsx 的工作表名为 " + sheetName + "，但 ExcelToSO 模板工作表名是 " + template.SheetName + "。这会导致 Unity 查找 _" + sheetName + "Items 失败。";
                return false;
            }

            var fieldRowIndex = table.FieldRow < 0 ? 0 : table.FieldRow;
            if (fieldRowIndex >= matrix.Count)
            {
                error = "cache xlsx 缺少字段行。";
                return false;
            }

            var fieldRow = matrix[fieldRowIndex];
            if (fieldRow.Any(field => IsGeneratedColumnName(field)))
            {
                error = "cache xlsx 字段行包含 column_4/column_N 这类自动生成字段，请使用旧 ExcelToSO 模板的真实字段行。";
                return false;
            }

            if (template.Available)
            {
                var expectedFields = template.Fields.TakeWhile(field => !string.IsNullOrWhiteSpace(field)).ToList();
                for (var i = 0; i < expectedFields.Count; i++)
                {
                    var actual = i < fieldRow.Count ? fieldRow[i] : "";
                    if (!string.Equals(actual, expectedFields[i], StringComparison.Ordinal))
                    {
                        error = "cache xlsx 字段行第 " + (i + 1).ToString(CultureInfo.InvariantCulture) + " 列为 “" + actual + "”，但 ExcelToSO 模板要求 “" + expectedFields[i] + "”。字段大小写会影响 Unity serialized field，例如 ID 不能写成 id。";
                        return false;
                    }
                }
            }

            var dataStartRow = table.DataStartRow < 0
                ? Math.Max(fieldRowIndex, Math.Max(EffectiveTypeRow(table), table.DescriptionRow < 0 ? EffectiveTypeRow(table) + 1 : table.DescriptionRow)) + 1
                : table.DataStartRow;
            if (dataStartRow >= 0 && dataStartRow < matrix.Count && LooksLikeTypeTokenRow(matrix[dataStartRow]))
            {
                error = "cache xlsx 的 data_from_row 第一行看起来仍是 canonical type row（例如 integer/string/json），ExcelToSO 会把它当作真实数据。";
                return false;
            }
        }

        return true;
    }

    private static bool IsGeneratedColumnName(string value)
    {
        var text = (value ?? "").Trim().ToLowerInvariant();
        if (!text.StartsWith("column_", StringComparison.Ordinal))
        {
            return false;
        }

        return text.Skip("column_".Length).All(char.IsDigit);
    }

    private static bool TryValidateWithLegacyExcelReader(string path, string workspaceRoot, out string error)
    {
        error = "";
        var excelDll = FindLegacyExcelDll(workspaceRoot);
        if (string.IsNullOrWhiteSpace(excelDll))
        {
            return true;
        }

        ResolveEventHandler? resolver = null;
        var excelDir = Path.GetDirectoryName(excelDll) ?? "";
        try
        {
            resolver = (_, args) =>
            {
                var name = new AssemblyName(args.Name).Name + ".dll";
                var candidate = Path.Combine(excelDir, name);
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            var assembly = Assembly.LoadFrom(excelDll);
            var factory = assembly.GetType("Excel.ExcelReaderFactory", throwOnError: false);
            var method = factory == null
                ? null
                : factory.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => string.Equals(m.Name, "CreateOpenXmlReader", StringComparison.Ordinal) &&
                                         m.GetParameters().Length == 1 &&
                                         typeof(Stream).IsAssignableFrom(m.GetParameters()[0].ParameterType));
            if (method == null)
            {
                return true;
            }

            using (var stream = File.OpenRead(path))
            {
                var reader = method.Invoke(null, new object[] { stream });
                try
                {
                    reader?.GetType().GetMethod("Read", Type.EmptyTypes)?.Invoke(reader, Array.Empty<object>());
                }
                finally
                {
                    (reader as IDisposable)?.Dispose();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            var root = ex is TargetInvocationException target && target.InnerException != null ? target.InnerException : ex;
            error = "ExcelToSO/ExcelDataReader 无法打开 cache xlsx：" + root.GetType().Name + " " + root.Message;
            return false;
        }
        finally
        {
            if (resolver != null)
            {
                AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            }
        }
    }

    private static string FindLegacyExcelDll(string workspaceRoot)
    {
        var env = Environment.GetEnvironmentVariable("CONFIG_SHEET_FORGE_EXCEL_DLL");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return env;
        }

        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            roots.Add(Path.Combine(workspaceRoot, "Packages"));
            roots.Add(Path.Combine(workspaceRoot, "Library", "PackageCache"));
        }

        roots.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "excel_to_scriptableobject", "Editor")));
        roots.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "excel_to_scriptableobject", "Editor")));

        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                var candidate = Directory.EnumerateFiles(root, "Excel.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Best effort only. Structural OpenXML validation still runs when Excel.dll is unavailable.
            }
        }

        return "";
    }

    private static List<List<string>> ReadFirstWorksheetMatrix(string path)
    {
        using (var archive = ZipFile.OpenRead(path))
        {
            var sharedStrings = ReadSharedStrings(archive);
            var workbook = ReadWorkbookSheets(archive);
            var sheetPath = workbook.Count == 0 ? "xl/worksheets/sheet1.xml" : workbook[0];
            return ReadWorksheetMatrix(archive, sheetPath, sharedStrings);
        }
    }

    private static string ReadFirstWorksheetName(string path)
    {
        using (var archive = ZipFile.OpenRead(path))
        {
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry == null)
            {
                return "";
            }

            var document = ReadXml(workbookEntry);
            var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            return document.Descendants(ns + "sheet").Select(sheet => sheet.Attribute("name")?.Value ?? "").FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
        }
    }

    private static IList<string> ReadSharedStrings(ZipArchive archive)
    {
        var strings = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
        {
            return strings;
        }

        var document = ReadXml(entry);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        foreach (var item in document.Descendants(ns + "si"))
        {
            strings.Add(string.Concat(item.Descendants(ns + "t").Select(t => t.Value)));
        }

        return strings;
    }

    private static List<string> ReadWorkbookSheets(ZipArchive archive)
    {
        return ReadWorkbookSheetInfos(archive).Select(sheet => sheet.Path).ToList();
    }

    private static List<XlsxSheetPathInfo> ReadWorkbookSheetInfos(ZipArchive archive)
    {
        var result = new List<XlsxSheetPathInfo>();
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry == null)
        {
            return result;
        }

        var rels = ReadRelationships(archive, "xl/_rels/workbook.xml.rels");
        var document = ReadXml(workbookEntry);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var relNs = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        foreach (var sheet in document.Descendants(ns + "sheet"))
        {
            var relationId = sheet.Attribute(relNs + "id")?.Value ?? "";
            rels.TryGetValue(relationId, out var target);
            result.Add(new XlsxSheetPathInfo
            {
                Name = sheet.Attribute("name")?.Value ?? "",
                Path = NormalizeWorkbookTarget(target ?? "")
            });
        }

        return result;
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
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
        foreach (var relationship in document.Descendants(ns + "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value ?? "";
            var target = relationship.Attribute("Target")?.Value ?? "";
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

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var rows = new SortedDictionary<int, SortedDictionary<int, string>>();
        foreach (var cell in ReadXml(entry).Descendants(ns + "c"))
        {
            var reference = cell.Attribute("r")?.Value ?? "";
            var position = ParseCellReference(reference);
            if (position.Row < 0 || position.Column < 0)
            {
                continue;
            }

            if (!rows.TryGetValue(position.Row, out var row))
            {
                row = new SortedDictionary<int, string>();
                rows[position.Row] = row;
            }

            row[position.Column] = ReadXlsxCellValue(cell, sharedStrings);
        }

        var matrix = new List<List<string>>();
        if (rows.Count == 0)
        {
            return matrix;
        }

        var maxRow = rows.Keys.Max();
        var maxColumn = rows.Values.SelectMany(r => r.Keys).DefaultIfEmpty(0).Max();
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

    private static string ReadXlsxCellValue(XElement cell, IList<string> sharedStrings)
    {
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var type = cell.Attribute("t")?.Value ?? "";
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(ns + "t").Select(t => t.Value));
        }

        var raw = cell.Element(ns + "v")?.Value ?? "";
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

    private static CellReference ParseCellReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new CellReference(-1, -1);
        }

        var column = 0;
        var index = 0;
        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            column = column * 26 + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }

        var rowText = reference.Substring(index);
        return column > 0 && int.TryParse(rowText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) && row > 0
            ? new CellReference(row - 1, column - 1)
            : new CellReference(-1, -1);
    }

    private static void WriteZipText(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(text);
        }
    }

    private static string ToA1(int columnIndex, int rowIndex)
    {
        var columnNumber = columnIndex + 1;
        var name = "";
        while (columnNumber > 0)
        {
            var modulo = (columnNumber - 1) % 26;
            name = Convert.ToChar('A' + modulo) + name;
            columnNumber = (columnNumber - modulo) / 26;
        }

        return name + (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string SanitizeSheetName(string value)
    {
        var invalid = new HashSet<char>(new[] { ':', '\\', '/', '?', '*', '[', ']' });
        var name = new string((value ?? "Sheet1").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('\'');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Sheet1";
        }

        return name.Length <= 31 ? name : name.Substring(0, 31);
    }

    private static string EscapeXmlAttribute(string value)
    {
        return (value ?? "").Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string ResolvePathAgainstRoot(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return path ?? "";
        }

        return Path.GetFullPath(Path.Combine(FirstNonEmpty(root, Directory.GetCurrentDirectory()), path.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string NormalizeFieldKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static async Task<string> ReadExistingHashAsync(string path)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        var text = await File.ReadAllTextAsync(path, Utf8NoBom);
        return text.Trim();
    }

    private static string FindExportedXlsx(string directory, string tableId)
    {
        var expected = Path.Combine(directory, MakeSafeFileName(tableId) + ".xlsx");
        if (File.Exists(expected))
        {
            return expected;
        }

        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.xlsx", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? ""
            : "";
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();
        foreach (var c in value ?? "")
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        return builder.Length == 0 ? "workbook" : builder.ToString();
    }

    private static async Task<string> TryRunGitAsync(params string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return "";
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? stdout.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string MaskIfSensitive(string key, string value)
    {
        if (key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return Mask(value);
        }

        return value;
    }

    private static string Mask(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        return value.Length <= 8 ? "********" : value.Substring(0, 4) + "..." + value.Substring(value.Length - 4);
    }

    private sealed class CliLifecyclePlatform : ILifecyclePlatform, ISeedFromLocalXlsxPlatform, IBranchWorkspacePlatform, ITargetBranchBootstrapPostflightPlatform, IReviewRegistryPlatform
    {
        private readonly ParsedArgs _args;
        private readonly LifecycleContractRequest _request;
        private readonly LarkCliGateway _gateway;

        public CliLifecyclePlatform(ParsedArgs args, LifecycleContractRequest request)
        {
            _args = args;
            _request = request;
            _gateway = new LarkCliGateway(args.Get("lark-cli", "lark-cli"));
        }

        public async Task<RegistrySnapshot> GetRegistrySnapshotAsync(RegistryContract registry, CancellationToken cancellationToken)
        {
            return await LoadRegistrySnapshotFromLarkAsync(_gateway, registry.BaseToken, _request.Locale, _args);
        }

        public async Task<LifecycleActionResult> ValidateTargetBranchBootstrapPostflightAsync(LifecycleContractRequest request, BranchWorkspaceResolution branchWorkspace, IList<SeedTableLifecycleResult> seedTables, CancellationToken cancellationToken)
        {
            var action = new LifecycleActionResult
            {
                Action = "target_branch.bootstrap.postflight",
                Status = "running",
                Message = "正在重新读取 BranchBindings / ConfigSheets / SchemaReviews，确认目标分支初始化结果。"
            };

            var bootstrap = request.TargetBranchBootstrap ?? new TargetBranchBootstrapContract();
            var targetBranch = FirstNonEmpty(branchWorkspace.GitBranch, bootstrap.TargetGitBranch, request.Git.Branch, "main");
            var targetProfile = FirstNonEmpty(branchWorkspace.Profile, branchWorkspace.FeishuBranch, bootstrap.TargetFeishuProfile, request.Git.Profile, targetBranch);
            var tableIds = seedTables
                .Where(t => !string.IsNullOrWhiteSpace(t.TableId))
                .Select(t => t.TableId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var failures = new List<string>();

            action.Details["targetBranch"] = targetBranch;
            action.Details["targetProfile"] = targetProfile;
            action.Details["branchNode"] = FirstNonEmpty(branchWorkspace.RootWikiTitle, "项目配置表") + "/" + FirstNonEmpty(branchWorkspace.NodeTitle, targetProfile);
            action.Details["branchWikiNodeToken"] = branchWorkspace.WikiNodeToken;
            action.Details["branchWikiNodeUrl"] = branchWorkspace.WikiNodeUrl;
            action.Details["tableCount"] = tableIds.Count.ToString(CultureInfo.InvariantCulture);
            action.Details["tableIds"] = string.Join(", ", tableIds);

            if (string.IsNullOrWhiteSpace(request.Registry.BaseToken))
            {
                failures.Add("postflight 无法读取注册中心：ProjectSettings/contract 没有 registry.baseToken。请先补项目配置里的 live registry。");
            }
            else
            {
                RegistrySnapshot snapshot;
                try
                {
                    snapshot = await LoadRegistrySnapshotFromLarkAsync(_gateway, request.Registry.BaseToken, request.Locale, _args);
                }
                catch (CliException ex)
                {
                    failures.Add("postflight 读取注册中心失败：" + ex.Message + "。请确认 lark-cli、bot scope、Base 共享权限都正常。");
                    snapshot = new RegistrySnapshot();
                }

                ValidatePostflightBranchBinding(snapshot, request, targetBranch, targetProfile, action, failures);
                ValidatePostflightConfigSheets(snapshot, request, targetProfile, tableIds, action, failures);
                ValidatePostflightSchemaReviews(snapshot, request, targetProfile, tableIds, action, failures);
            }

            var failedOnlineRead = seedTables
                .Where(t => !HasTableAction(t, "seed.online_read", "done") ||
                            !HasTableAction(t, "seed.export_xlsx", "done") ||
                            !HasTableAction(t, "seed.triangulation_compare", "passed") ||
                            string.IsNullOrWhiteSpace(t.SpreadsheetToken) ||
                            string.IsNullOrWhiteSpace(t.SheetId))
                .Select(t => t.TableId)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (failedOnlineRead.Count > 0)
            {
                failures.Add("postflight 发现这些表没有完成在线回读、xlsx 导出或三方一致性检查：" + string.Join(", ", failedOnlineRead) + "。不会把本次初始化视为完成，请检查对应表的 bot 读取/导出权限后重跑。");
            }

            action.Details["onlineReadAndExportVerified"] = (failedOnlineRead.Count == 0).ToString().ToLowerInvariant();
            action.Details["postflightPassed"] = (failures.Count == 0).ToString().ToLowerInvariant();
            if (failures.Count == 0)
            {
                action.Status = "passed";
                action.Message = "postflight 通过：已重新读取注册中心，目标分支 “" + targetBranch + "” 下 " + tableIds.Count.ToString(CultureInfo.InvariantCulture) + " 张表都有在线 Sheet 定位，并且 apply 阶段已完成在线回读、导出和三方一致性检查。";
            }
            else
            {
                action.Status = "failed";
                action.Message = string.Join(" ", failures);
                action.Details["failureCount"] = failures.Count.ToString(CultureInfo.InvariantCulture);
            }

            return action;
        }

        private static bool HasTableAction(SeedTableLifecycleResult table, string actionName, string expectedStatus)
        {
            return table.Actions.Any(a =>
                string.Equals(a.Action, actionName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Status, expectedStatus, StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidatePostflightBranchBinding(RegistrySnapshot snapshot, LifecycleContractRequest request, string targetBranch, string targetProfile, LifecycleActionResult action, IList<string> failures)
        {
            var branchTable = FindRegistryTable(snapshot, "BranchBindings", request.Locale);
            if (branchTable == null)
            {
                failures.Add("postflight 没有读到 BranchBindings 表。请确认 registry.tableIds.BranchBindings 配置正确，或先运行 registry-migrate。");
                return;
            }

            var bindings = branchTable.Records
                .Where(r => !r.IsEmpty)
                .Where(r => string.Equals(GetRegistryRecordValue(r, "GitBranch", request.Locale, request.Registry), targetBranch, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(FirstNonEmpty(GetRegistryRecordValue(r, "Profile", request.Locale, request.Registry), GetRegistryRecordValue(r, "FeishuBranch", request.Locale, request.Registry)), targetProfile, StringComparison.OrdinalIgnoreCase))
                .ToList();
            action.Details["branchBindingRecordIds"] = string.Join(", ", bindings.Select(r => FirstNonEmpty(r.RecordId, "(无 record_id)")));
            if (bindings.Count == 0)
            {
                failures.Add("postflight 没有读到目标分支 BranchBindings：GitBranch=" + targetBranch + "，Profile=" + targetProfile + "。请确认 registry upsert 成功。");
                return;
            }

            if (bindings.Count > 1)
            {
                failures.Add("postflight 发现目标分支 BranchBindings 重复（record_id: " + action.Details["branchBindingRecordIds"] + "）。请先清理重复记录后重跑，工具不会静默任选一条。");
                return;
            }

            var binding = bindings[0];
            action.Details["branchBindingRecordId"] = binding.RecordId;
            action.Details["verifiedBranchWikiNodeToken"] = GetRegistryRecordValue(binding, "WikiNodeToken", request.Locale, request.Registry);
            action.Details["verifiedBranchWikiNodeUrl"] = GetRegistryRecordValue(binding, "WikiNodeUrl", request.Locale, request.Registry);
        }

        private static void ValidatePostflightConfigSheets(RegistrySnapshot snapshot, LifecycleContractRequest request, string targetProfile, IList<string> tableIds, LifecycleActionResult action, IList<string> failures)
        {
            var configTable = FindRegistryTable(snapshot, "ConfigSheets", request.Locale);
            if (configTable == null)
            {
                failures.Add("postflight 没有读到 ConfigSheets 表。请确认 registry.tableIds.ConfigSheets 配置正确。");
                return;
            }

            var missing = new List<string>();
            var missingLocators = new List<string>();
            var duplicates = new List<string>();
            var recordIds = new List<string>();
            foreach (var tableId in tableIds)
            {
                var matches = FindPostflightTableRows(configTable, request, tableId, targetProfile);
                if (matches.Count == 0)
                {
                    missing.Add(tableId);
                    continue;
                }

                if (matches.Count > 1)
                {
                    duplicates.Add(tableId + "(" + string.Join(", ", matches.Select(m => FirstNonEmpty(m.RecordId, "(无 record_id)"))) + ")");
                    continue;
                }

                var row = matches[0];
                var spreadsheetToken = GetRegistryRecordValue(row, "SpreadsheetToken", request.Locale, request.Registry);
                var sheetId = GetRegistryRecordValue(row, "SheetId", request.Locale, request.Registry);
                if (string.IsNullOrWhiteSpace(spreadsheetToken) || string.IsNullOrWhiteSpace(sheetId))
                {
                    missingLocators.Add(tableId + "(record_id: " + FirstNonEmpty(row.RecordId, "(无 record_id)") + ")");
                    continue;
                }

                recordIds.Add(tableId + ":" + FirstNonEmpty(row.RecordId, "(无 record_id)"));
            }

            action.Details["configSheetRecordIds"] = string.Join(", ", recordIds);
            action.Details["verifiedConfigSheetCount"] = recordIds.Count.ToString(CultureInfo.InvariantCulture);
            action.Details["missingConfigSheets"] = string.Join(", ", missing);
            action.Details["missingConfigSheetLocators"] = string.Join(", ", missingLocators);
            action.Details["duplicateConfigSheets"] = string.Join(", ", duplicates);
            if (missing.Count > 0)
            {
                failures.Add("postflight 发现目标分支缺少 ConfigSheets 记录：" + string.Join(", ", missing) + "。请确认 registry upsert 成功后重跑。");
            }

            if (missingLocators.Count > 0)
            {
                failures.Add("postflight 发现目标分支这些 ConfigSheets 缺 SpreadsheetToken 或 SheetId：" + string.Join(", ", missingLocators) + "。请检查在线 Sheet 创建/导入是否成功。");
            }

            if (duplicates.Count > 0)
            {
                failures.Add("postflight 发现 ConfigSheets 重复记录：" + string.Join(", ", duplicates) + "。请先清理重复记录后重跑，工具不会静默任选一条。");
            }
        }

        private static void ValidatePostflightSchemaReviews(RegistrySnapshot snapshot, LifecycleContractRequest request, string targetProfile, IList<string> tableIds, LifecycleActionResult action, IList<string> failures)
        {
            if (request.TargetBranchBootstrap != null && !request.TargetBranchBootstrap.ConfirmSchemaReviews)
            {
                action.Details["schemaReviewsPostflight"] = "skipped";
                return;
            }

            var schemaTable = FindRegistryTable(snapshot, "SchemaReviews", request.Locale);
            if (schemaTable == null)
            {
                failures.Add("postflight 没有读到 SchemaReviews 表。请确认 registry.tableIds.SchemaReviews 配置正确。");
                return;
            }

            var missing = new List<string>();
            var duplicates = new List<string>();
            var recordIds = new List<string>();
            foreach (var tableId in tableIds)
            {
                var matches = FindPostflightTableRows(schemaTable, request, tableId, targetProfile);
                if (matches.Count == 0)
                {
                    missing.Add(tableId);
                    continue;
                }

                if (matches.Count > 1)
                {
                    duplicates.Add(tableId + "(" + string.Join(", ", matches.Select(m => FirstNonEmpty(m.RecordId, "(无 record_id)"))) + ")");
                    continue;
                }

                recordIds.Add(tableId + ":" + FirstNonEmpty(matches[0].RecordId, "(无 record_id)"));
            }

            action.Details["schemaReviewRecordIds"] = string.Join(", ", recordIds);
            action.Details["verifiedSchemaReviewCount"] = recordIds.Count.ToString(CultureInfo.InvariantCulture);
            action.Details["missingSchemaReviews"] = string.Join(", ", missing);
            action.Details["duplicateSchemaReviews"] = string.Join(", ", duplicates);
            if (missing.Count > 0)
            {
                failures.Add("postflight 发现目标分支缺少 SchemaReviews baseline：" + string.Join(", ", missing) + "。请确认 confirmSchemaReviews=true 并重跑。");
            }

            if (duplicates.Count > 0)
            {
                failures.Add("postflight 发现 SchemaReviews 重复记录：" + string.Join(", ", duplicates) + "。请先清理重复记录后重跑。");
            }
        }

        private static List<RegistryRecordSnapshot> FindPostflightTableRows(RegistryTableSnapshot table, LifecycleContractRequest request, string tableId, string targetProfile)
        {
            return table.Records
                .Where(r => !r.IsEmpty)
                .Where(r => string.Equals(GetRegistryRecordValue(r, "TableId", request.Locale, request.Registry), tableId, StringComparison.OrdinalIgnoreCase))
                .Where(r => string.Equals(FirstNonEmpty(
                    GetRegistryRecordValue(r, "Profile", request.Locale, request.Registry),
                    GetRegistryRecordValue(r, "Branch", request.Locale, request.Registry),
                    GetRegistryRecordValue(r, "FeishuBranch", request.Locale, request.Registry)), targetProfile, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public Task<LifecycleActionResult> EnsureRegistryAsync(RegistryContract registry, RegistryDisplayMapping mapping, CancellationToken cancellationToken)
        {
            return Task.FromResult(new LifecycleActionResult
            {
                Action = "registry.ensure",
                Status = "ready",
                Message = "注册中心结构由 registry-migrate/bootstrap contract 管理；当前命令已保留 machine key 到显示名映射。"
            });
        }

        public async Task<SheetCreationResult> CreateOnlineSheetAsync(ContractTableSpec table, CancellationToken cancellationToken)
        {
            var title = FirstNonEmpty(table.DisplayName, table.TableId, table.SheetName, "Config Sheet");
            var command = new List<string> { "sheets", "+create", "--title", title };
            if (!string.IsNullOrWhiteSpace(table.WikiRootToken))
            {
                command.Add("--folder-token");
                command.Add(table.WikiRootToken);
            }

            var create = await RunLarkCliStrictAsync(_gateway, _args, command);
            var sheet = ParseSheetCreationResult(CombinedJsonOutput(create));
            if (string.IsNullOrWhiteSpace(sheet.SpreadsheetToken))
            {
                sheet.SpreadsheetToken = table.SpreadsheetToken;
            }

            if (string.IsNullOrWhiteSpace(sheet.SpreadsheetUrl))
            {
                sheet.SpreadsheetUrl = table.SpreadsheetUrl;
            }

            if (string.IsNullOrWhiteSpace(sheet.SheetId) && !string.IsNullOrWhiteSpace(sheet.SpreadsheetToken))
            {
                var info = await RunLarkCliStrictAsync(_gateway, _args, new[] { "sheets", "+info", "--spreadsheet-token", sheet.SpreadsheetToken });
                var infoSheet = ParseSheetCreationResult(CombinedJsonOutput(info));
                sheet.SheetId = FirstNonEmpty(infoSheet.SheetId, table.SheetId);
                sheet.SpreadsheetUrl = FirstNonEmpty(sheet.SpreadsheetUrl, infoSheet.SpreadsheetUrl);
            }

            if (string.IsNullOrWhiteSpace(sheet.SheetId))
            {
                sheet.SheetId = FirstNonEmpty(table.SheetId, "Sheet1");
            }

            return sheet;
        }

        public async Task<LifecycleActionResult> WriteSheetTemplateAsync(SheetCreationResult sheet, IList<IList<string>> templateRows, CancellationToken cancellationToken)
        {
            await WriteSheetValuesInChunksAsync(sheet.SpreadsheetToken, sheet.SheetId, templateRows, cancellationToken);
            var action = new LifecycleActionResult { Action = "sheet.template.write", Status = "done", Message = "已写入 ExcelToSO 模板三行。" };
            action.Details["rows"] = templateRows.Count.ToString(CultureInfo.InvariantCulture);
            return action;
        }

        public async Task<LifecycleActionResult> UpsertRegistryRecordAsync(RegistryContract registry, ContractTableSpec table, SheetCreationResult sheet, CancellationToken cancellationToken)
        {
            var tableId = ResolveRegistryTableId(registry, "ConfigSheets");
            var mapping = RegistryLocalization.Default(_request.Locale);
            var body = new Dictionary<string, object>
            {
                [mapping.Fields["TableId"]] = table.TableId,
                [mapping.Fields["DisplayName"]] = FirstNonEmpty(table.DisplayName, table.TableId),
                [mapping.Fields["Branch"]] = FirstNonEmpty(table.Branch, table.Profile, _request.BranchWorkspace.FeishuBranch, _request.Git.FeishuBranch, _request.Git.Profile, _request.Git.Branch),
                [mapping.Fields["Profile"]] = FirstNonEmpty(table.Profile, _request.BranchWorkspace.Profile, _request.Git.Profile),
                [mapping.Fields["WikiNodeToken"]] = FirstNonEmpty(table.WikiNodeToken, _request.BranchWorkspace.ExistingWikiNodeToken),
                [mapping.Fields["WikiNodeUrl"]] = FirstNonEmpty(table.WikiNodeUrl, _request.BranchWorkspace.ExistingWikiNodeUrl),
                [mapping.Fields["ExcelPath"]] = FirstNonEmpty(table.ExcelPath, table.LocalCachePath),
                [mapping.Fields["SpreadsheetToken"]] = sheet.SpreadsheetToken,
                [mapping.Fields["SheetId"]] = sheet.SheetId,
                [mapping.Fields["SemanticHash"]] = table.SemanticHash,
                [mapping.Fields["OwnerRole"]] = table.OwnerRole,
                [mapping.Fields["SchemaReviewRequired"]] = table.SchemaReviewRequired ? "是" : "否",
                [mapping.Fields["OnlineSheetUrl"]] = FirstNonEmpty(sheet.SpreadsheetUrl, table.OnlineSheetUrl),
                [mapping.Fields["Status"]] = FirstNonEmpty(table.Status, "active"),
                [mapping.Fields["UpdatedAt"]] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            var branchKey = FirstNonEmpty(table.Branch, table.Profile, _request.BranchWorkspace.FeishuBranch, _request.Git.FeishuBranch, _request.Git.Profile, _request.Git.Branch);
            var recordId = FirstNonEmpty(
                registry.RegistryRecordId,
                await FindRegistryRecordIdAsync(registry.BaseToken, tableId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [mapping.Fields["TableId"]] = table.TableId,
                    [mapping.Fields["Branch"]] = branchKey
                }),
                await FindRegistryRecordIdAsync(registry.BaseToken, tableId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [mapping.Fields["TableId"]] = table.TableId,
                    [mapping.Fields["Profile"]] = branchKey
                }));
            var command = new List<string> { "base", "+record-upsert", "--base-token", registry.BaseToken, "--table-id", tableId, "--json", JsonSerializer.Serialize(body, CompactJsonOptions) };
            if (!string.IsNullOrWhiteSpace(recordId))
            {
                command.Add("--record-id");
                command.Add(recordId);
            }

            var upsert = await RunLarkCliStrictAsync(_gateway, _args, command);
            var action = new LifecycleActionResult { Action = "registry.config_sheets.upsert", Status = "done", Message = "已登记配表清单。" };
            action.Details["recordId"] = FirstNonEmpty(ParseRecordId(CombinedJsonOutput(upsert)), recordId);
            return action;
        }

        public async Task<LifecycleActionResult> UpsertSchemaReviewAsync(RegistryContract registry, ContractTableSpec table, ContractGitSpec git, string reason, CancellationToken cancellationToken)
        {
            var tableId = ResolveRegistryTableId(registry, "SchemaReviews");
            var mapping = RegistryLocalization.Default(_request.Locale);
            var body = new Dictionary<string, object>
            {
                [mapping.Fields["TableId"]] = table.TableId,
                [mapping.Fields["DisplayName"]] = FirstNonEmpty(table.DisplayName, table.TableId),
                [mapping.Fields["Branch"]] = FirstNonEmpty(git.FeishuBranch, git.Profile, git.Branch),
                [mapping.Fields["Profile"]] = FirstNonEmpty(git.Profile, git.FeishuBranch, git.Branch),
                [mapping.Fields["Status"]] = "pending",
                ["Reason"] = reason
            };
            var recordId = await FindRegistryRecordIdAsync(registry.BaseToken, tableId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [mapping.Fields["TableId"]] = table.TableId,
                [mapping.Fields["Branch"]] = FirstNonEmpty(git.FeishuBranch, git.Profile, git.Branch)
            });
            if (string.IsNullOrWhiteSpace(recordId))
            {
                recordId = await FindRegistryRecordIdAsync(registry.BaseToken, tableId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [mapping.Fields["TableId"]] = table.TableId,
                    [mapping.Fields["Profile"]] = FirstNonEmpty(git.Profile, git.FeishuBranch, git.Branch)
                });
            }
            var command = new List<string> { "base", "+record-upsert", "--base-token", registry.BaseToken, "--table-id", tableId, "--json", JsonSerializer.Serialize(body, CompactJsonOptions) };
            if (!string.IsNullOrWhiteSpace(recordId))
            {
                command.Add("--record-id");
                command.Add(recordId);
            }

            var upsert = await RunLarkCliStrictAsync(_gateway, _args, command);
            var action = new LifecycleActionResult { Action = "registry.schema_reviews.upsert", Status = "done", Message = "已创建或更新 pending Schema 审查记录。" };
            action.Details["schemaReviewId"] = FirstNonEmpty(ParseRecordId(CombinedJsonOutput(upsert)), recordId);
            action.Details["reason"] = reason;
            return action;
        }

        public async Task<LifecycleActionResult> UpsertMergeReviewAsync(RegistryContract registry, MergeReviewContract review, MergeReviewInputSummary summary, CancellationToken cancellationToken)
        {
            var action = new LifecycleActionResult
            {
                Action = "registry.merge_reviews.upsert",
                Status = "running",
                Message = "正在写入 MergeReviews 合并审查记录。"
            };
            registry ??= new RegistryContract();
            review ??= new MergeReviewContract();
            summary ??= LifecycleExecutor.BuildMergeReviewInputSummary(_request, review.TableIds);

            if (string.IsNullOrWhiteSpace(registry.BaseToken))
            {
                action.Status = "failed";
                action.Message = "无法写入 MergeReviews：contract.registry.baseToken 为空。请检查项目配置的 live registry。";
                return action;
            }

            RegistrySnapshot snapshot;
            try
            {
                snapshot = await LoadRegistrySnapshotFromLarkAsync(_gateway, registry.BaseToken, _request.Locale, _args);
            }
            catch (CliException ex)
            {
                action.Status = "failed";
                action.Message = "读取注册中心失败，无法提交合并审查记录：" + ex.Message;
                action.Details["error"] = ex.Detail;
                return action;
            }

            var table = FindRegistryTable(snapshot, "MergeReviews", _request.Locale);
            if (table == null)
            {
                action.Status = "failed";
                action.Message = "注册中心没有找到 MergeReviews/合并审查 表。请先运行 registry-migrate 或检查项目配置表结构。";
                return action;
            }

            var reviewIdField = ResolveRegistryFieldName(table, registry, "ReviewId", "审查ID", "reviewId");
            var tableIdField = ResolveRegistryFieldName(table, registry, "TableId", "配表ID", "tableId");
            var gitBranchField = ResolveRegistryFieldName(table, registry, "GitBranch", "Git分支", "sourceBranch");
            var statusField = ResolveRegistryFieldName(table, registry, "Status", "状态");
            var approverRoleField = ResolveRegistryFieldName(table, registry, "ApproverRole", "批准角色", "ApprovedByRole", "approverRole");
            var updatedAtField = ResolveRegistryFieldName(table, registry, "UpdatedAt", "更新时间", "updatedAt");
            var missingFields = new List<string>();
            foreach (var pair in new[]
            {
                new KeyValuePair<string, string>("审查ID", reviewIdField),
                new KeyValuePair<string, string>("配表ID", tableIdField),
                new KeyValuePair<string, string>("Git分支", gitBranchField),
                new KeyValuePair<string, string>("状态", statusField),
                new KeyValuePair<string, string>("ApproverRole", approverRoleField),
                new KeyValuePair<string, string>("更新时间", updatedAtField)
            })
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    missingFields.Add(pair.Key);
                }
            }

            if (missingFields.Count > 0)
            {
                action.Status = "failed";
                action.Message = "MergeReviews 表缺少必要字段：" + string.Join(", ", missingFields) + "。请在 Base 中补字段或运行 registry-migrate 后重试。";
                action.Details["availableFields"] = string.Join(", ", table.Fields.Select(f => FirstNonEmpty(f.DisplayName, f.FieldId)));
                return action;
            }

            if (!EnsureStatusOptionsReady(action, table, registry, "MergeReviews", "MergeReviews.状态"))
            {
                return action;
            }

            var sourceBranch = FirstNonEmpty(review.SourceBranch, summary.SourceBranch, _request.Git.Branch);
            var targetBranch = FirstNonEmpty(review.TargetBranch, summary.TargetBranch, "main");
            var tableId = FirstNonEmpty(review.TableId, "__project_pr_gate__");
            var reviewId = FirstNonEmpty(review.ReviewId, BuildMergeReviewId(sourceBranch, targetBranch, _request.Git.Head));
            var body = new Dictionary<string, object>
            {
                [reviewIdField] = reviewId,
                [tableIdField] = tableId,
                [gitBranchField] = sourceBranch,
                [statusField] = FirstNonEmpty(review.Status, "approved"),
                [approverRoleField] = FirstNonEmpty(review.ApproverRole, "configOwner"),
                [updatedAtField] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            AddOptionalReviewField(body, table, registry, "ReviewComment", FirstNonEmpty(review.ReviewComment, "Unity 提交合并审查记录。"), "审查说明", "评论", "Comment");
            AddOptionalReviewField(body, table, registry, "PrNumber", FirstNonEmpty(review.PrNumber, summary.PrNumber), "PR", "PR号", "prNumber");
            AddOptionalReviewField(body, table, registry, "PrUrl", FirstNonEmpty(review.PrUrl, summary.PrUrl), "PR链接", "prUrl");
            AddOptionalReviewField(body, table, registry, "MergeReportPath", FirstNonEmpty(review.MergeReportPath, summary.MergeReportPath), "合并报告", "mergeReportPath");
            AddOptionalReviewField(body, table, registry, "MergedPath", FirstNonEmpty(review.MergedPath, summary.MergedPath), "合并结果", "mergedPath");
            AddOptionalReviewField(body, table, registry, "RequestFingerprint", FirstNonEmpty(review.RequestFingerprint, summary.Fingerprint), "请求指纹", "Fingerprint", "requestFingerprint");

            string recordId;
            try
            {
                recordId = FindUniqueMergeReviewRecordId(table, reviewIdField, reviewId, tableIdField, tableId, gitBranchField, sourceBranch);
            }
            catch (CliException ex)
            {
                action.Status = "failed";
                action.Message = ex.Message;
                action.Details["error"] = ex.Detail;
                return action;
            }

            var command = new List<string> { "base", "+record-upsert", "--base-token", registry.BaseToken, "--table-id", table.TableId, "--json", JsonSerializer.Serialize(body, CompactJsonOptions) };
            if (!string.IsNullOrWhiteSpace(recordId))
            {
                command.Add("--record-id");
                command.Add(recordId);
            }

            try
            {
                var upsert = await RunLarkCliStrictAsync(_gateway, _args, command);
                action.Status = string.IsNullOrWhiteSpace(recordId) ? "done" : "updated";
                action.Message = "已写入 MergeReviews 合并审查记录；不会写 main、不会写本地 cache、不会改 ProjectSettings 或 ExcelToSO。";
                action.Details["recordId"] = FirstNonEmpty(ParseRecordId(CombinedJsonOutput(upsert)), recordId);
                action.Details["reviewId"] = reviewId;
                action.Details["tableId"] = tableId;
                action.Details["sourceBranch"] = sourceBranch;
                action.Details["targetBranch"] = targetBranch;
                action.Details["approverRole"] = FirstNonEmpty(review.ApproverRole, "configOwner");
                action.Details["requestFingerprint"] = FirstNonEmpty(review.RequestFingerprint, summary.Fingerprint);
                return action;
            }
            catch (CliException ex)
            {
                action.Status = "failed";
                action.Message = HumanizeMergeReviewUpsertFailure(ex);
                action.Details["error"] = ex.Message;
                action.Details["detail"] = ex.Detail;
                return action;
            }
        }

        public async Task<LifecycleActionResult> UpsertSchemaReviewApprovalAsync(RegistryContract registry, SchemaReviewApprovalContract review, CancellationToken cancellationToken)
        {
            var action = new LifecycleActionResult { Action = "registry.schema_reviews.approve", Status = "running", Message = "正在更新 SchemaReviews 审查状态。" };
            registry ??= new RegistryContract();
            review ??= new SchemaReviewApprovalContract();
            if (string.IsNullOrWhiteSpace(registry.BaseToken))
            {
                action.Status = "failed";
                action.Message = "无法更新 SchemaReviews：registry.baseToken 为空。";
                return action;
            }

            var snapshot = await LoadRegistrySnapshotFromLarkAsync(_gateway, registry.BaseToken, _request.Locale, _args);
            var table = FindRegistryTable(snapshot, "SchemaReviews", _request.Locale);
            if (table == null)
            {
                action.Status = "failed";
                action.Message = "注册中心没有找到 SchemaReviews 表。";
                return action;
            }

            var tableIdField = ResolveRegistryFieldName(table, registry, "TableId", "配表ID");
            var branchField = ResolveRegistryFieldName(table, registry, "Branch", "Feishu分支", "GitBranch", "Git分支", "Profile");
            var statusField = ResolveRegistryFieldName(table, registry, "Status", "状态");
            if (string.IsNullOrWhiteSpace(tableIdField) || string.IsNullOrWhiteSpace(statusField))
            {
                action.Status = "failed";
                action.Message = "SchemaReviews 表缺少配表ID或状态字段。";
                return action;
            }

            if (!EnsureStatusOptionsReady(action, table, registry, "SchemaReviews", "SchemaReviews.状态"))
            {
                return action;
            }

            var branch = FirstNonEmpty(review.Branch, review.Profile, _request.Git.FeishuBranch, _request.Git.Profile, _request.Git.Branch);
            var record = table.Records
                .Where(r => string.Equals(GetRegistryRecordValue(r, "TableId", _request.Locale, registry), review.TableId, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(r => string.IsNullOrWhiteSpace(branchField) || string.Equals(GetRegistryRecordValue(r, branchField, _request.Locale, registry), branch, StringComparison.OrdinalIgnoreCase));
            var body = new Dictionary<string, object>
            {
                [tableIdField] = review.TableId,
                [statusField] = FirstNonEmpty(review.Status, "approved")
            };
            if (!string.IsNullOrWhiteSpace(branchField))
            {
                body[branchField] = branch;
            }

            AddOptionalReviewField(body, table, registry, "ApproverRole", FirstNonEmpty(review.ApproverRole, "schemaReviewer"), "ApprovedByRole", "批准角色");
            AddOptionalReviewField(body, table, registry, "ReviewComment", review.ReviewComment, "审查说明", "评论", "Comment");
            AddOptionalReviewField(body, table, registry, "UpdatedAt", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture), "更新时间");
            var command = new List<string> { "base", "+record-upsert", "--base-token", registry.BaseToken, "--table-id", table.TableId, "--json", JsonSerializer.Serialize(body, CompactJsonOptions) };
            if (record != null && !string.IsNullOrWhiteSpace(record.RecordId))
            {
                command.Add("--record-id");
                command.Add(record.RecordId);
            }

            var upsert = await RunLarkCliStrictAsync(_gateway, _args, command);
            action.Status = "done";
            action.Message = "已更新 SchemaReviews 审查状态。";
            action.Details["recordId"] = FirstNonEmpty(ParseRecordId(CombinedJsonOutput(upsert)), record == null ? "" : record.RecordId);
            action.Details["tableId"] = review.TableId;
            return action;
        }

        public async Task<LifecycleActionResult> UpsertWaiverApprovalAsync(RegistryContract registry, WaiverApprovalContract waiver, CancellationToken cancellationToken)
        {
            var action = new LifecycleActionResult { Action = "registry.waivers.approve", Status = "running", Message = "正在写入 Waivers 临时放行记录。" };
            registry ??= new RegistryContract();
            waiver ??= new WaiverApprovalContract();
            if (string.IsNullOrWhiteSpace(registry.BaseToken))
            {
                action.Status = "failed";
                action.Message = "无法写入 Waivers：registry.baseToken 为空。";
                return action;
            }

            var snapshot = await LoadRegistrySnapshotFromLarkAsync(_gateway, registry.BaseToken, _request.Locale, _args);
            var table = FindRegistryTable(snapshot, "Waivers", _request.Locale);
            if (table == null)
            {
                action.Status = "failed";
                action.Message = "注册中心没有找到 Waivers 表。";
                return action;
            }

            var tableIdField = ResolveRegistryFieldName(table, registry, "TableId", "配表ID");
            var branchField = ResolveRegistryFieldName(table, registry, "Branch", "GitBranch", "Git分支", "Feishu分支");
            var statusField = ResolveRegistryFieldName(table, registry, "Status", "状态");
            var expiresField = ResolveRegistryFieldName(table, registry, "ExpiresAt", "过期时间", "expiresAt");
            var approverField = ResolveRegistryFieldName(table, registry, "ApprovedByRole", "ApproverRole", "批准角色");
            if (string.IsNullOrWhiteSpace(tableIdField) || string.IsNullOrWhiteSpace(branchField) || string.IsNullOrWhiteSpace(statusField) || string.IsNullOrWhiteSpace(expiresField))
            {
                action.Status = "failed";
                action.Message = "Waivers 表缺少配表ID、分支、状态或过期时间字段。";
                return action;
            }

            if (!EnsureStatusOptionsReady(action, table, registry, "Waivers", "Waivers.状态"))
            {
                return action;
            }

            var body = new Dictionary<string, object>
            {
                [tableIdField] = FirstNonEmpty(waiver.TableId, "__project_pr_gate__"),
                [branchField] = waiver.Branch,
                [statusField] = "approved",
                [expiresField] = waiver.ExpiresAt
            };
            if (!string.IsNullOrWhiteSpace(approverField))
            {
                body[approverField] = FirstNonEmpty(waiver.ApprovedByRole, "configOwner");
            }

            AddOptionalReviewField(body, table, registry, "Reason", waiver.Reason, "原因", "说明");
            var command = new List<string> { "base", "+record-upsert", "--base-token", registry.BaseToken, "--table-id", table.TableId, "--json", JsonSerializer.Serialize(body, CompactJsonOptions) };
            var upsert = await RunLarkCliStrictAsync(_gateway, _args, command);
            action.Status = "done";
            action.Message = "已写入 Waivers 临时放行记录。";
            action.Details["recordId"] = ParseRecordId(CombinedJsonOutput(upsert));
            action.Details["expiresAt"] = waiver.ExpiresAt;
            return action;
        }

        private string ResolveRegistryFieldName(RegistryTableSnapshot table, RegistryContract registry, string machineKey, params string[] aliases)
        {
            var field = ResolveRegistryFieldSnapshot(table, registry, machineKey, aliases);
            return field == null ? "" : FirstNonEmpty(field.DisplayName, field.FieldId, machineKey);
        }

        private RegistryFieldSnapshot? ResolveRegistryFieldSnapshot(RegistryTableSnapshot table, RegistryContract registry, string machineKey, params string[] aliases)
        {
            if (table == null)
            {
                return null;
            }

            var candidates = new List<string> { machineKey, RegistryLocalization.FieldDisplayName(machineKey, _request.Locale) };
            if (aliases != null)
            {
                candidates.AddRange(aliases);
            }

            if (registry != null)
            {
                foreach (var tableFields in registry.FieldDisplayNames.Values)
                {
                    foreach (var pair in tableFields)
                    {
                        if (string.Equals(pair.Key, machineKey, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(pair.Value, machineKey, StringComparison.OrdinalIgnoreCase))
                        {
                            candidates.Add(pair.Key);
                            candidates.Add(pair.Value);
                        }
                    }
                }
            }

            foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var field = table.Fields.FirstOrDefault(f =>
                    string.Equals(f.MachineKey, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.DisplayName, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.FieldId, candidate, StringComparison.OrdinalIgnoreCase));
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private bool EnsureStatusOptionsReady(LifecycleActionResult action, RegistryTableSnapshot table, RegistryContract registry, string tableMachineKey, string label)
        {
            var statusField = ResolveRegistryFieldSnapshot(table, registry, "Status", "状态");
            if (statusField == null || !RegistryMigrator.IsSelectLikeStatusField(statusField))
            {
                return true;
            }

            if (RegistryMigrator.StatusOptionsReady(statusField, tableMachineKey))
            {
                return true;
            }

            var required = RegistryMigrator.RequiredStatusOptions(tableMachineKey);
            var existing = new HashSet<string>(statusField.Options ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var missing = required.Where(option => !existing.Contains(option)).ToList();
            action.Status = "failed";
            action.Message = label + " 单选字段缺少必要状态选项：" + string.Join(", ", missing) + "。请先运行 registry-migrate --dry-run 查看计划，再执行 registry-migrate --yes 补齐 " + tableMachineKey + " 状态选项。";
            action.Details["missingStatusOptions"] = string.Join(",", missing);
            action.Details["existingStatusOptions"] = string.Join(",", statusField.Options ?? new List<string>());
            action.Details["nextStep"] = "请先运行 registry-migrate 补齐 " + tableMachineKey + " 状态选项。";
            return false;
        }

        private void AddOptionalReviewField(Dictionary<string, object> body, RegistryTableSnapshot table, RegistryContract registry, string machineKey, string value, params string[] aliases)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var fieldName = ResolveRegistryFieldName(table, registry, machineKey, aliases);
            if (!string.IsNullOrWhiteSpace(fieldName) && !body.ContainsKey(fieldName))
            {
                body[fieldName] = value;
            }
        }

        private string FindUniqueMergeReviewRecordId(RegistryTableSnapshot table, string reviewIdField, string reviewId, string tableIdField, string tableId, string gitBranchField, string gitBranch)
        {
            var reviewMatches = table.Records
                .Where(r => string.Equals(GetRegistryRecordValue(r, reviewIdField, _request.Locale, _request.Registry), reviewId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (reviewMatches.Count > 1)
            {
                throw BuildDuplicateRegistryRecordException(table.TableId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [reviewIdField] = reviewId
                }, reviewMatches);
            }

            if (reviewMatches.Count == 1)
            {
                return reviewMatches[0].RecordId;
            }

            var keyMatches = table.Records
                .Where(r => string.Equals(GetRegistryRecordValue(r, tableIdField, _request.Locale, _request.Registry), tableId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(GetRegistryRecordValue(r, gitBranchField, _request.Locale, _request.Registry), gitBranch, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (keyMatches.Count > 1)
            {
                throw BuildDuplicateRegistryRecordException(table.TableId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [tableIdField] = tableId,
                    [gitBranchField] = gitBranch
                }, keyMatches);
            }

            return keyMatches.Count == 1 ? keyMatches[0].RecordId : "";
        }

        private static string BuildMergeReviewId(string sourceBranch, string targetBranch, string gitHead)
        {
            var shortHead = FirstNonEmpty(gitHead, "unknown");
            if (shortHead.Length > 8)
            {
                shortHead = shortHead.Substring(0, 8);
            }

            return "merge-" + Slugify(sourceBranch) + "-to-" + Slugify(targetBranch) + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + shortHead;
        }

        private static string Slugify(string value)
        {
            value = (value ?? "").Trim();
            var builder = new StringBuilder();
            foreach (var c in value)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (builder.Length == 0 || builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }
            }

            return builder.ToString().Trim('-');
        }

        private static string HumanizeMergeReviewUpsertFailure(CliException ex)
        {
            var text = (ex.Message + "\n" + ex.Detail).ToLowerInvariant();
            if (text.Contains("option") || text.Contains("select") || text.Contains("单选") || text.Contains("状态"))
            {
                return "MergeReviews.状态 字段缺少 approved/completed 选项，无法写入审查记录。请在 Base 的“状态”单选字段里加入 approved（或 completed），再重试。";
            }

            if (text.Contains("permission") || text.Contains("forbidden") || text.Contains("权限") || text.Contains("无权"))
            {
                return "bot 没有写入 MergeReviews 的权限。请把注册中心 Base 授权给 bot，并确认应用拥有 Base 记录写入 scope。";
            }

            return "写入 MergeReviews 失败：" + ex.Message;
        }

        public async Task<LifecycleActionResult> ApplyRegistryMigrationAsync(RegistryContract registry, RegistryMigrationPlan plan, CancellationToken cancellationToken)
        {
            var result = new LifecycleContractResult();
            await ApplyRegistryMigrationToLarkAsync(_gateway, registry.BaseToken, plan, _args, result);
            return new LifecycleActionResult
            {
                Action = "registry.migration.apply",
                Status = result.Success ? "done" : "failed",
                Message = result.Success ? "已应用注册中心迁移。" : string.Join(" ", result.HumanReadableFailures)
            };
        }

        public async Task<BranchWorkspaceResolution> EnsureBranchWorkspaceAsync(BranchWorkspaceContract workspace, BranchWorkspaceResolution planned, CancellationToken cancellationToken)
        {
            planned = planned ?? new BranchWorkspaceResolution();
            if (!string.IsNullOrWhiteSpace(planned.WikiNodeToken) || !string.IsNullOrWhiteSpace(planned.WikiNodeUrl))
            {
                try
                {
                    var existing = await GetWikiNodeAsync(FirstNonEmpty(planned.WikiNodeUrl, planned.WikiNodeToken));
                    ApplyWikiNodeJson(planned, CombinedJsonOutput(existing));
                    planned.Status = "reused";
                    return planned;
                }
                catch (CliException)
                {
                    planned.WikiNodeToken = "";
                    planned.WikiNodeUrl = "";
                }
            }

            if (string.IsNullOrWhiteSpace(planned.RootWikiToken) && string.IsNullOrWhiteSpace(planned.RootWikiUrl))
            {
                planned.Status = "failed";
                return planned;
            }

            var rootInfo = new BranchWorkspaceResolution();
            try
            {
                var root = await GetWikiNodeAsync(FirstNonEmpty(planned.RootWikiUrl, planned.RootWikiToken));
                ApplyWikiNodeJson(rootInfo, CombinedJsonOutput(root));
            }
            catch (CliException)
            {
                planned.Status = "failed";
                return planned;
            }

            if (!string.IsNullOrWhiteSpace(rootInfo.CreatedBy))
            {
                planned.CreatedBy = FirstNonEmpty(planned.CreatedBy, rootInfo.CreatedBy);
            }

            var parentNodeToken = FirstNonEmpty(rootInfo.WikiNodeToken, planned.RootWikiToken);
            var spaceId = rootInfo.Status;
            if (!string.IsNullOrWhiteSpace(spaceId))
            {
                try
                {
                    var children = await RunLarkCliStrictAsync(_gateway, _args, new[] { "wiki", "+node-list", "--space-id", spaceId, "--parent-node-token", parentNodeToken, "--page-all", "--page-limit", "20" });
                    var existingChild = FindWikiChildByTitle(CombinedJsonOutput(children), planned.NodeTitle);
                    if (!string.IsNullOrWhiteSpace(existingChild.WikiNodeToken))
                    {
                        planned.WikiNodeToken = existingChild.WikiNodeToken;
                        planned.WikiNodeUrl = existingChild.WikiNodeUrl;
                        planned.Status = "reused";
                        return planned;
                    }
                }
                catch (CliException)
                {
                    // Listing is an optimization. If the bot can create under the parent, the next step will still work.
                }
            }

            if (!planned.CreateIfMissing)
            {
                planned.Status = "missing";
                return planned;
            }

            try
            {
                var create = await RunLarkCliStrictAsync(_gateway, _args, new[] { "wiki", "+node-create", "--parent-node-token", parentNodeToken, "--title", planned.NodeTitle, "--obj-type", "docx" });
                ApplyWikiNodeJson(planned, CombinedJsonOutput(create));
                planned.Status = "created";
            }
            catch (CliException)
            {
                planned.Status = "failed";
            }

            return planned;
        }

        private async Task<LarkCliResult> GetWikiNodeAsync(string tokenOrUrl)
        {
            try
            {
                return await RunLarkCliStrictAsync(_gateway, _args, new[] { "wiki", "+node-get", "--node-token", tokenOrUrl });
            }
            catch (CliException) when (!LooksLikeUrl(tokenOrUrl))
            {
                var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["token"] = tokenOrUrl }, CompactJsonOptions);
                return await RunLarkCliStrictAsync(_gateway, _args, new[] { "wiki", "spaces", "get_node", "--params", payload });
            }
        }

        public async Task<LifecycleActionResult> UpsertBranchBindingAsync(RegistryContract registry, BranchWorkspaceResolution resolution, CancellationToken cancellationToken)
        {
            var action = new LifecycleActionResult
            {
                Action = "registry.branch_bindings.upsert",
                Status = "skipped",
                Message = "未配置 Base token，跳过 BranchBindings 回填。"
            };
            action.Details["gitBranch"] = resolution.GitBranch;
            action.Details["profile"] = resolution.Profile;
            action.Details["wikiNodeToken"] = resolution.WikiNodeToken;
            action.Details["wikiNodeUrl"] = resolution.WikiNodeUrl;

            if (string.IsNullOrWhiteSpace(registry.BaseToken))
            {
                return action;
            }

            var tableId = ResolveRegistryTableId(registry, FirstNonEmpty(resolution.BindingRegistryTable, "BranchBindings"));
            var mapping = RegistryLocalization.Default(_request.Locale);
            var now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var body = new Dictionary<string, object>
            {
                [mapping.Fields["GitBranch"]] = resolution.GitBranch,
                [mapping.Fields["FeishuBranch"]] = resolution.FeishuBranch,
                [mapping.Fields["Profile"]] = resolution.Profile,
                [mapping.Fields["WikiNodeToken"]] = resolution.WikiNodeToken,
                [mapping.Fields["WikiNodeUrl"]] = resolution.WikiNodeUrl,
                [mapping.Fields["Status"]] = FirstNonEmpty(resolution.Status, "active"),
                [mapping.Fields["OwnerRole"]] = resolution.OwnerRole,
                [mapping.Fields["CreatedBy"]] = resolution.CreatedBy,
                [mapping.Fields["UpdatedAt"]] = now
            };

            var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [mapping.Fields["GitBranch"]] = resolution.GitBranch,
                [mapping.Fields["Profile"]] = FirstNonEmpty(resolution.Profile, resolution.FeishuBranch)
            };
            var recordId = await FindRegistryRecordIdAsync(registry.BaseToken, tableId, keys);
            if (string.IsNullOrWhiteSpace(recordId))
            {
                recordId = await FindRegistryRecordIdAsync(registry.BaseToken, tableId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [mapping.Fields["GitBranch"]] = resolution.GitBranch,
                    [mapping.Fields["FeishuBranch"]] = FirstNonEmpty(resolution.FeishuBranch, resolution.Profile)
                });
            }
            var command = new List<string> { "base", "+record-upsert", "--base-token", registry.BaseToken, "--table-id", tableId, "--json", JsonSerializer.Serialize(body, CompactJsonOptions) };
            if (!string.IsNullOrWhiteSpace(recordId))
            {
                command.Add("--record-id");
                command.Add(recordId);
            }

            var upsert = await RunLarkCliStrictAsync(_gateway, _args, command);
            action.Status = "done";
            action.Message = "已按 GitBranch + Profile 登记 BranchBindings。";
            action.Details["recordId"] = FirstNonEmpty(ParseRecordId(CombinedJsonOutput(upsert)), recordId);
            return action;
        }

        public Task<SeedLocalWorkbookResult> ReadLocalXlsxAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, CancellationToken cancellationToken)
        {
            var result = new SeedLocalWorkbookResult();
            var source = ResolveWorkspacePath(FirstNonEmpty(table.SourceXlsxPath, seed.SourceXlsxPath));
            result.SourceXlsxPath = source;

            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                result.Findings.Add(new ValidationFinding
                {
                    Severity = FindingSeverity.Error,
                    Code = "xlsx.missing",
                    Message = "配表 " + table.TableId + " 找不到本地 xlsx 源文件。请检查 sourceXlsxPath 是否正确。",
                    Location = source
                });
                return Task.FromResult(result);
            }

            var structure = XlsxWorkbookReader.InspectPortableStructures(source, FirstNonEmpty(table.TableId, table.SheetName));
            foreach (var finding in structure.Findings)
            {
                result.Findings.Add(finding);
            }

            var import = XlsxWorkbookReader.Import(source, BuildMatrixOptions(table, "xlsx", source));
            result.Workbook = import.Workbook;
            foreach (var finding in import.Report.Findings)
            {
                result.Findings.Add(finding);
            }

            if (result.Workbook != null)
            {
                result.Workbook.Metadata["tableId"] = table.TableId;
                result.Workbook.Metadata["sourceXlsxPath"] = source;
                result.SemanticHash = SemanticHasher.ComputeHash(result.Workbook);
            }

            return Task.FromResult(result);
        }

        public async Task<SeedOnlineSheetResult> EnsureOnlineSheetAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, WorkbookDocument localWorkbook, string semanticHash, CancellationToken cancellationToken)
        {
            var result = new SeedOnlineSheetResult();
            var localMatrix = BuildMatrixFromWorkbook(localWorkbook);
            ApplyMatrixDimensions(result, localMatrix);
            var existingFromConfig = FindExistingSheetInProjectConfig(seed, table);
            var existingToken = FirstNonEmpty(table.SpreadsheetToken, table.SpreadsheetUrl, existingFromConfig.SpreadsheetToken, existingFromConfig.SpreadsheetUrl);
            if (!string.IsNullOrWhiteSpace(existingToken))
            {
                try
                {
                    var info = await RunLarkCliStrictAsync(_gateway, _args, BuildSheetInfoCommand(existingToken));
                    var parsed = ParseSheetCreationResult(CombinedJsonOutput(info));
                    result.SpreadsheetToken = FirstNonEmpty(parsed.SpreadsheetToken, table.SpreadsheetToken, existingFromConfig.SpreadsheetToken, existingToken);
                    result.SpreadsheetUrl = FirstNonEmpty(parsed.SpreadsheetUrl, table.SpreadsheetUrl, existingFromConfig.SpreadsheetUrl);
                    result.SheetId = FirstNonEmpty(table.SheetId, existingFromConfig.SheetId, parsed.SheetId);
                    result.WikiNodeToken = FirstNonEmpty(table.WikiNodeToken, existingFromConfig.WikiNodeToken, parsed.WikiNodeToken);
                    result.Reused = true;
                    result.ImportMode = "existing";
                    return result;
                }
                catch (CliException ex)
                {
                    result.Findings.Add(new ValidationFinding
                    {
                        Severity = FindingSeverity.Error,
                        Code = "seed.existing_sheet_unavailable",
                        Message = "已有 registry/config token 指向的在线 Sheet 无法读取。不会创建重复表；请确认权限或修正 token 后重试。",
                        Location = table.TableId,
                        Details = { ["error"] = ex.Message }
                    });
                    return result;
                }
            }

            var existingUnderBranch = await TryFindExistingSheetUnderBranchNodeAsync(table, cancellationToken);
            if (existingUnderBranch.Findings.Any(f => f.Severity == FindingSeverity.Error))
            {
                foreach (var finding in existingUnderBranch.Findings)
                {
                    result.Findings.Add(finding);
                }

                return result;
            }

            if (!string.IsNullOrWhiteSpace(existingUnderBranch.SpreadsheetToken))
            {
                result.SpreadsheetToken = existingUnderBranch.SpreadsheetToken;
                result.SpreadsheetUrl = existingUnderBranch.SpreadsheetUrl;
                result.SheetId = FirstNonEmpty(existingUnderBranch.SheetId, table.SheetId, table.SheetName);
                result.WikiNodeToken = existingUnderBranch.WikiNodeToken;
                result.Reused = true;
                result.ImportMode = "existing-wiki-child";
                ApplyMatrixDimensions(result, localMatrix);
                return result;
            }

            var importFailure = "";
            if (seed.PreferDriveImport)
            {
                var imported = await TryDriveImportXlsxAsync(seed, table, cancellationToken);
                if (imported.Success)
                {
                    var parsed = ParseSheetCreationResult(CombinedJsonOutput(imported));
                    result.SpreadsheetToken = parsed.SpreadsheetToken;
                    result.SpreadsheetUrl = parsed.SpreadsheetUrl;
                    result.SheetId = FirstNonEmpty(parsed.SheetId, table.SheetId, table.SheetName);
                    result.WikiNodeToken = parsed.WikiNodeToken;
                    result.Created = true;
                    result.ImportMode = "drive-import-xlsx";
                    if (!string.IsNullOrWhiteSpace(result.SpreadsheetToken))
                    {
                        if (string.IsNullOrWhiteSpace(result.SheetId) || string.Equals(result.SheetId, table.SheetName, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var info = await RunLarkCliStrictAsync(_gateway, _args, BuildSheetInfoCommand(result.SpreadsheetToken));
                                var infoSheet = ParseSheetCreationResult(CombinedJsonOutput(info));
                                result.SheetId = FirstNonEmpty(infoSheet.SheetId, result.SheetId);
                                result.SpreadsheetUrl = FirstNonEmpty(result.SpreadsheetUrl, infoSheet.SpreadsheetUrl);
                            }
                            catch (CliException)
                            {
                                // The immediate online read step will fail with a precise strict-bot diagnostic if the sheet cannot be inspected.
                            }
                        }

                        return result;
                    }
                }

                importFailure = Trim(imported.Stderr + "\n" + imported.Stdout);
            }

            var sheet = await CreateOnlineSheetAsync(ToContractTable(table, seed), cancellationToken);
            result.SpreadsheetToken = sheet.SpreadsheetToken;
            result.SpreadsheetUrl = sheet.SpreadsheetUrl;
            result.SheetId = FirstNonEmpty(sheet.SheetId, table.SheetId, table.SheetName, "Sheet1");
            result.WikiNodeToken = sheet.WikiNodeToken;
            result.Created = true;
            result.ImportMode = "sheets-create-values-write";
            result.CapabilityDifference = "drive import xlsx 未使用或失败，fallback 只写入 semantic 普通值；不会保留 Excel 格式、隐藏信息、图片或公式。";
            if (!string.IsNullOrWhiteSpace(importFailure))
            {
                result.CapabilityDifference += " drive import 失败摘要：" + importFailure;
            }

            await WriteSheetValuesInChunksAsync(result.SpreadsheetToken, result.SheetId, localMatrix, cancellationToken);
            return result;
        }

        private async Task<SeedOnlineSheetResult> TryFindExistingSheetUnderBranchNodeAsync(SeedTableContract table, CancellationToken cancellationToken)
        {
            var result = new SeedOnlineSheetResult();
            var branchNode = FirstNonEmpty(table.WikiRootToken, table.WikiNodeToken);
            if (string.IsNullOrWhiteSpace(branchNode))
            {
                return result;
            }

            try
            {
                var branchInfo = new BranchWorkspaceResolution();
                var node = await GetWikiNodeAsync(branchNode);
                ApplyWikiNodeJson(branchInfo, CombinedJsonOutput(node));
                var spaceId = branchInfo.Status;
                var parentNodeToken = FirstNonEmpty(branchInfo.WikiNodeToken, branchNode);
                if (string.IsNullOrWhiteSpace(spaceId) || string.IsNullOrWhiteSpace(parentNodeToken))
                {
                    return result;
                }

                var children = await RunLarkCliStrictAsync(_gateway, _args, new[] { "wiki", "+node-list", "--space-id", spaceId, "--parent-node-token", parentNodeToken, "--page-all", "--page-limit", "100" });
                var sheet = FindWikiSheetChildByTitle(CombinedJsonOutput(children), FirstNonEmpty(table.DisplayName, table.TableId));
                if (string.IsNullOrWhiteSpace(sheet.SpreadsheetToken))
                {
                    sheet = FindWikiSheetChildByTitle(CombinedJsonOutput(children), table.TableId);
                }

                if (string.IsNullOrWhiteSpace(sheet.SpreadsheetToken))
                {
                    return result;
                }

                var info = await RunLarkCliStrictAsync(_gateway, _args, BuildSheetInfoCommand(sheet.SpreadsheetToken));
                var parsed = ParseSheetCreationResult(CombinedJsonOutput(info));
                result.SpreadsheetToken = FirstNonEmpty(parsed.SpreadsheetToken, sheet.SpreadsheetToken);
                result.SpreadsheetUrl = FirstNonEmpty(parsed.SpreadsheetUrl, sheet.SpreadsheetUrl);
                result.SheetId = FirstNonEmpty(parsed.SheetId, table.SheetId, table.SheetName);
                result.WikiNodeToken = sheet.WikiNodeToken;
                return result;
            }
            catch (CliException ex)
            {
                result.Findings.Add(new ValidationFinding
                {
                    Severity = FindingSeverity.Error,
                    Code = "seed.branch_node_list_failed",
                    Message = "无法读取分支工作区下已有 Sheet，已阻断以避免重复创建在线表。请检查 bot 的 Wiki 节点读取权限后重试。",
                    Location = table.TableId,
                    Details = { ["error"] = ex.Message }
                });
                return result;
            }
        }

        private async Task WriteSheetValuesInChunksAsync(string spreadsheetToken, string sheetId, IList<IList<string>> rows, CancellationToken cancellationToken)
        {
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            var totalColumns = Math.Max(1, rows.Max(r => r == null ? 0 : r.Count));
            var maxRows = Math.Max(1, _args.GetInt("lark-write-max-rows", 80));
            var maxJsonChars = Math.Max(4000, _args.GetInt("lark-write-max-json-chars", 24000));
            var start = 0;
            while (start < rows.Count)
            {
                var count = 0;
                string valuesJson;
                do
                {
                    count++;
                    valuesJson = JsonSerializer.Serialize(PadMatrix(rows.Skip(start).Take(count), totalColumns), CompactJsonOptions);
                    if (valuesJson.Length > maxJsonChars && count > 1)
                    {
                        count--;
                        valuesJson = JsonSerializer.Serialize(PadMatrix(rows.Skip(start).Take(count), totalColumns), CompactJsonOptions);
                        break;
                    }
                }
                while (start + count < rows.Count && count < maxRows && valuesJson.Length <= maxJsonChars);

                if (count <= 0)
                {
                    count = 1;
                    valuesJson = JsonSerializer.Serialize(PadMatrix(rows.Skip(start).Take(count), totalColumns), CompactJsonOptions);
                }

                var range = BuildA1Range(sheetId, start + 1, 1, count, totalColumns);
                Console.Error.WriteLine("[seed] sheets +write range=" + range + " rows=" + count.ToString(CultureInfo.InvariantCulture));
                await RunLarkCliStrictAsync(_gateway, _args, new[] { "sheets", "+write", "--spreadsheet-token", spreadsheetToken, "--sheet-id", sheetId, "--range", range, "--values", valuesJson });
                start += count;
            }
        }

        private static IList<IList<string>> PadMatrix(IEnumerable<IList<string>> rows, int totalColumns)
        {
            var result = new List<IList<string>>();
            foreach (var row in rows)
            {
                var padded = new List<string>();
                for (var i = 0; i < totalColumns; i++)
                {
                    padded.Add(row != null && i < row.Count ? row[i] ?? "" : "");
                }

                result.Add(padded);
            }

            return result;
        }

        public async Task<SeedOnlineRoundTripResult> ReadAndExportOnlineSheetAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken)
        {
            var result = new SeedOnlineRoundTripResult();
            var temp = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ConfigSheetForge", "seed-temp", Guid.NewGuid().ToString("N"), MakeSafeFileName(table.TableId));
            Directory.CreateDirectory(temp);
            var provider = new LarkCliWorkbookProvider();
            var readSheetId = FirstNonEmpty(sheet.SheetId, table.SheetId);
            var exactRange = sheet.UsedRowCount > 0 && sheet.UsedColumnCount > 0 && !string.IsNullOrWhiteSpace(readSheetId)
                ? BuildA1Range(readSheetId, 1, 1, sheet.UsedRowCount, sheet.UsedColumnCount)
                : "";
            var export = await provider.ExportAsync(BuildProviderContext(), new ProviderExportRequest
            {
                RootTokenOrUrl = FirstNonEmpty(sheet.SpreadsheetToken, sheet.SpreadsheetUrl),
                SpreadsheetTokenOrUrl = FirstNonEmpty(sheet.SpreadsheetToken, sheet.SpreadsheetUrl),
                TableId = table.TableId,
                SheetId = readSheetId,
                Range = exactRange,
                CacheDirectory = temp,
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            }, cancellationToken);

            foreach (var finding in export.Findings)
            {
                result.Findings.Add(ToValidationFinding(finding, table.TableId));
            }

            result.OnlineWorkbook = export.Workbook;
            result.ExportedXlsxPath = FindExportedXlsx(temp, table.TableId);
            if (string.IsNullOrWhiteSpace(result.ExportedXlsxPath))
            {
                result.Findings.Add(new ValidationFinding
                {
                    Severity = FindingSeverity.Error,
                    Code = "seed.export_xlsx_missing",
                    Message = "在线 Sheet 没有成功导出 xlsx。请确认应用有文件导出权限后重试。",
                    Location = table.TableId
                });
                return result;
            }

            var structure = XlsxWorkbookReader.InspectPortableStructures(result.ExportedXlsxPath, table.TableId);
            foreach (var finding in structure.Findings)
            {
                result.Findings.Add(finding);
            }

            var imported = XlsxWorkbookReader.Import(result.ExportedXlsxPath, BuildMatrixOptions(table, "xlsx", result.ExportedXlsxPath));
            result.ExportedXlsxWorkbook = imported.Workbook;
            foreach (var finding in imported.Report.Findings)
            {
                result.Findings.Add(finding);
            }

            return result;
        }

        public async Task<LifecycleActionResult> WriteSeedCacheAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, WorkbookDocument localWorkbook, string semanticHash, string exportedXlsxPath, CancellationToken cancellationToken)
        {
            var xlsxPath = ResolveWorkspacePath(FirstNonEmpty(table.CacheXlsxPath, Path.Combine(seed.ExcelCacheDirectory, table.TableId + ".xlsx")));
            var semanticPath = ResolveWorkspacePath(FirstNonEmpty(table.SemanticCachePath, Path.Combine(seed.CacheDirectory, table.TableId + ".semantic.json")));
            var shaPath = ResolveWorkspacePath(FirstNonEmpty(table.HashCachePath, Path.Combine(seed.CacheDirectory, table.TableId + ".sha256")));
            var cacheTable = new TableConfig
            {
                Id = table.TableId,
                Name = FirstNonEmpty(table.DisplayName, table.TableId),
                LocalSourcePath = table.SourceXlsxPath,
                UseExcelToSoCacheDialect = UsesExcelToSoCacheDialect(table),
                Fields = table.Fields.Select(CloneFieldSpec).ToList(),
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            };
            var existingHash = await ReadExistingHashAsync(shaPath);
            var unchanged = string.Equals(existingHash, semanticHash, StringComparison.Ordinal) &&
                            File.Exists(xlsxPath) &&
                            File.Exists(semanticPath) &&
                            File.Exists(shaPath) &&
                            (!cacheTable.UseExcelToSoCacheDialect || !CacheXlsxNeedsExcelToSoDialectRewrite(xlsxPath, cacheTable, Directory.GetCurrentDirectory()));
            if (!unchanged)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(xlsxPath) ?? ".");
                Directory.CreateDirectory(Path.GetDirectoryName(semanticPath) ?? ".");
                Directory.CreateDirectory(Path.GetDirectoryName(shaPath) ?? ".");
                if (!string.IsNullOrWhiteSpace(exportedXlsxPath) && File.Exists(exportedXlsxPath))
                {
                    if (cacheTable.UseExcelToSoCacheDialect)
                    {
                        var plan = BuildExcelToSoCacheDialectPlan(localWorkbook, cacheTable, Directory.GetCurrentDirectory());
                        if (plan.Errors.Count > 0)
                        {
                            throw new CliException("无法写入 ExcelToSO cache xlsx：" + string.Join("；", plan.Errors), 2);
                        }

                        WriteExcelToSoCacheXlsx(xlsxPath, localWorkbook, cacheTable, plan.TypeRow, Directory.GetCurrentDirectory());
                    }
                    else
                    {
                        File.Copy(exportedXlsxPath, xlsxPath, overwrite: true);
                    }
                }

                await WriteJsonAsync(semanticPath, localWorkbook);
                await File.WriteAllTextAsync(shaPath, semanticHash + Environment.NewLine, Utf8NoBom, cancellationToken);
            }

            var action = new LifecycleActionResult
            {
                Action = "seed.cache.write",
                Status = unchanged ? "unchanged" : "done",
                Message = unchanged ? "semantic hash 未变化，cache 文件未重写，mtime 保持不变。" : "已写入 seed cache。"
            };
            action.Details["tableId"] = table.TableId;
            action.Details["xlsxPath"] = xlsxPath;
            action.Details["semanticPath"] = semanticPath;
            action.Details["shaPath"] = shaPath;
            action.Details["semanticHash"] = semanticHash;
            return action;
        }

        public async Task<LifecycleActionResult> UpdateProjectConfigAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken)
        {
            var path = ResolveWorkspacePath(FirstNonEmpty(table.ProjectConfigPath, seed.ProjectConfigPath));
            var action = new LifecycleActionResult { Action = "seed.project_config.update", Status = "skipped", Message = "未配置 ProjectSettings/*ConfigSheetForge*.json，跳过项目配置回填。" };
            action.Details["tableId"] = table.TableId;
            if (string.IsNullOrWhiteSpace(path))
            {
                return action;
            }

            action.Details["path"] = path;
            if (!File.Exists(path))
            {
                action.Status = "failed";
                action.Message = "项目配置文件不存在，无法回填 spreadsheetToken/sheetId/url。";
                return action;
            }

            var json = StripUtf8Bom(await File.ReadAllTextAsync(path, Utf8NoBom, cancellationToken));
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                action.Status = "failed";
                action.Message = "项目配置 JSON 无法解析。";
                return action;
            }

            var changed = UpsertProjectConfigSheet(node, table, sheet, _request.BranchWorkspace);
            if (changed)
            {
                await File.WriteAllTextAsync(path, node.ToJsonString(JsonOptions) + Environment.NewLine, Utf8NoBom, cancellationToken);
            }

            action.Status = changed ? "done" : "unchanged";
            action.Message = changed ? "已回填项目配置中的在线 Sheet 信息。" : "项目配置已包含相同在线 Sheet 信息，未改动。";
            action.Details["spreadsheetToken"] = sheet.SpreadsheetToken;
            action.Details["sheetId"] = sheet.SheetId;
            action.Details["url"] = sheet.SpreadsheetUrl;
            return action;
        }

        public async Task<LifecycleActionResult> UpsertSeedRegistryRecordAsync(RegistryContract registry, SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(registry.BaseToken))
            {
                return new LifecycleActionResult
                {
                    Action = "seed.registry.config_sheets.upsert",
                    Status = "skipped",
                    Message = "未配置 Base token，跳过 ConfigSheets 注册中心回填。",
                    Details = { ["tableId"] = table.TableId }
                };
            }

            return await UpsertRegistryRecordAsync(registry, ToContractTable(table, seed, sheet), ToSheetCreationResult(sheet), cancellationToken);
        }

        public async Task<LifecycleActionResult> UpsertSeedSchemaReviewAsync(RegistryContract registry, SeedFromLocalXlsxContract seed, SeedTableContract table, ContractGitSpec git, bool schemaChangeDetected, string reason, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(registry.BaseToken) || !table.SchemaReviewRequired)
            {
                return new LifecycleActionResult
                {
                    Action = "seed.registry.schema_reviews.upsert",
                    Status = "skipped",
                    Message = table.SchemaReviewRequired ? "未配置 Base token，跳过 SchemaReviews 回填。" : "该表配置为不需要 SchemaReviews。",
                    Details = { ["tableId"] = table.TableId, ["schemaChangeDetected"] = schemaChangeDetected.ToString().ToLowerInvariant() }
                };
            }

            var action = await UpsertSchemaReviewAsync(registry, ToContractTable(table, seed), git, reason, cancellationToken);
            action.Action = "seed.registry.schema_reviews.upsert";
            action.Details["schemaChangeDetected"] = schemaChangeDetected.ToString().ToLowerInvariant();
            action.Details["baselineStrategy"] = seed.BaselineStrategy;
            return action;
        }

        public Task<LifecycleActionResult> UpdateExcelToSoSettingsAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, CancellationToken cancellationToken)
        {
            var unity = table.UnityExcelToSo ?? new UnityExcelToSoContract();
            var update = UnityExcelToSoSettingsUpdater.UpsertFile(ResolveWorkspacePath(unity.SettingsPath), new UnityExcelToSoEntry
            {
                TableId = FirstNonEmpty(unity.TableId, table.TableId),
                ExcelPath = FirstNonEmpty(unity.ExcelPath, table.CacheXlsxPath, Path.Combine(seed.ExcelCacheDirectory, table.TableId + ".xlsx")),
                ScriptableObjectType = unity.ScriptableObjectType,
                AssetPath = unity.AssetPath,
                ExtraFields = unity.ExtraFields
            });
            var action = new LifecycleActionResult
            {
                Action = "seed.unity.excel_to_so.upsert",
                Status = update.Changed ? "done" : "unchanged",
                Message = update.Message
            };
            action.Details["tableId"] = table.TableId;
            action.Details["settingsPath"] = ResolveWorkspacePath(unity.SettingsPath);
            return Task.FromResult(action);
        }

        private MatrixWorkbookImportOptions BuildMatrixOptions(SeedTableContract table, string providerId, string sourceId)
        {
            return new MatrixWorkbookImportOptions
            {
                ProviderId = providerId,
                SourceId = sourceId,
                SourceTitle = FirstNonEmpty(table.DisplayName, table.TableId, table.SheetName),
                SheetId = table.SheetId,
                SheetName = FirstNonEmpty(table.SheetName, table.TableId),
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            };
        }

        private string ResolveWorkspacePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                return path ?? "";
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path.Replace('/', Path.DirectorySeparatorChar)));
        }

        private IEnumerable<string> BuildSheetInfoCommand(string tokenOrUrl)
        {
            if (LooksLikeUrl(tokenOrUrl))
            {
                return new[] { "sheets", "+info", "--url", tokenOrUrl };
            }

            return new[] { "sheets", "+info", "--spreadsheet-token", tokenOrUrl };
        }

        private static bool LooksLikeUrl(string value)
        {
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<LarkCliResult> TryDriveImportXlsxAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, CancellationToken cancellationToken)
        {
            var doctor = await _gateway.RunAsync(new[] { "doctor" }, Directory.GetCurrentDirectory(), cancellationToken);
            if (!doctor.Success)
            {
                return doctor;
            }

            var args = new List<string>
            {
                "drive",
                "+import",
                "--file",
                ResolveWorkspacePath(table.SourceXlsxPath),
                "--target-type",
                "sheet",
                "--title",
                FirstNonEmpty(table.DisplayName, table.TableId)
            };
            var parent = FirstNonEmpty(table.WikiRootToken, seed.WikiRootToken);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                args.Add("--parent-token");
                args.Add(parent);
            }

            var identity = _args.Get("lark-identity", "bot");
            return await _gateway.RunAsync(WithLarkIdentity(args, identity), Directory.GetCurrentDirectory(), cancellationToken);
        }

        private ProviderContext BuildProviderContext()
        {
            var context = new ProviderContext { WorkspaceRoot = Directory.GetCurrentDirectory() };
            context.Settings["larkCliPath"] = _args.Get("lark-cli", "lark-cli");
            context.Settings["larkCliIdentity"] = _args.Get("lark-identity", "bot");
            context.Settings["larkAllowUserFallback"] = _args.HasFlag("allow-user-fallback") ? "true" : "false";
            return context;
        }

        private static ContractTableSpec ToContractTable(SeedTableContract table, SeedFromLocalXlsxContract seed)
        {
            return ToContractTable(table, seed, null);
        }

        private static ContractTableSpec ToContractTable(SeedTableContract table, SeedFromLocalXlsxContract seed, SeedOnlineSheetResult? sheet)
        {
            var contract = new ContractTableSpec
            {
                TableId = table.TableId,
                DisplayName = table.DisplayName,
                ExcelPath = FirstNonEmpty(table.CacheXlsxPath, Path.Combine(seed.ExcelCacheDirectory, table.TableId + ".xlsx")),
                LocalCachePath = FirstNonEmpty(table.CacheXlsxPath, Path.Combine(seed.ExcelCacheDirectory, table.TableId + ".xlsx")),
                SourceXlsxPath = table.SourceXlsxPath,
                CacheXlsxPath = table.CacheXlsxPath,
                SemanticCachePath = table.SemanticCachePath,
                HashCachePath = table.HashCachePath,
                ProjectConfigPath = table.ProjectConfigPath,
                SpreadsheetToken = FirstNonEmpty(sheet?.SpreadsheetToken ?? "", table.SpreadsheetToken),
                SpreadsheetUrl = FirstNonEmpty(sheet?.SpreadsheetUrl ?? "", table.SpreadsheetUrl),
                SheetId = FirstNonEmpty(sheet?.SheetId ?? "", table.SheetId),
                SheetName = table.SheetName,
                WikiRootToken = FirstNonEmpty(table.WikiRootToken, seed.WikiRootToken),
                WikiNodeToken = FirstNonEmpty(sheet?.WikiNodeToken ?? "", table.WikiNodeToken),
                WikiNodeUrl = table.WikiNodeUrl,
                Branch = table.Branch,
                Profile = table.Profile,
                SemanticHash = table.SemanticHash,
                OwnerRole = table.OwnerRole,
                RegistryRecordId = table.RegistryRecordId,
                SchemaReviewRequired = table.SchemaReviewRequired,
                Status = "active",
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            };
            foreach (var field in table.Fields)
            {
                contract.Fields.Add(field);
            }

            return contract;
        }

        private static SheetCreationResult ToSheetCreationResult(SeedOnlineSheetResult sheet)
        {
            return new SheetCreationResult
            {
                SpreadsheetToken = sheet.SpreadsheetToken,
                SpreadsheetUrl = sheet.SpreadsheetUrl,
                SheetId = sheet.SheetId,
                WikiNodeToken = sheet.WikiNodeToken
            };
        }

        private static ValidationFinding ToValidationFinding(ProviderDoctorFinding finding, string tableId)
        {
            var validation = new ValidationFinding
            {
                Severity = finding.Severity,
                Code = finding.Code,
                Message = finding.Message,
                Location = tableId
            };
            foreach (var pair in finding.Details)
            {
                validation.Details[pair.Key] = pair.Value;
            }

            validation.Details["tableId"] = tableId;
            return validation;
        }

        private static IList<IList<string>> BuildMatrixFromWorkbook(WorkbookDocument workbook)
        {
            var sheet = workbook.Sheets.FirstOrDefault();
            if (sheet == null)
            {
                return new List<IList<string>>();
            }

            var columns = sheet.Columns.ToList();
            var matrix = new List<IList<string>>
            {
                columns.Select(c => FirstNonEmpty(c.DisplayName, c.Details.TryGetValue("sourceColumnName", out var sourceColumnName) ? sourceColumnName : "", c.Key)).ToList(),
                columns.Select(c => FirstNonEmpty(c.ValueKind, "string")).ToList(),
                columns.Select(c => c.Details.TryGetValue("description", out var description) ? description : "").ToList()
            };
            foreach (var row in sheet.Rows.OrderBy(r => r.SourceIndex))
            {
                var values = new List<string>();
                foreach (var column in columns)
                {
                    values.Add(row.Cells.TryGetValue(column.Key, out var cell) ? cell.SemanticText : "");
                }

                matrix.Add(values);
            }

            return matrix;
        }

        private static void ApplyMatrixDimensions(SeedOnlineSheetResult result, IList<IList<string>> matrix)
        {
            if (result == null || matrix == null)
            {
                return;
            }

            result.UsedRowCount = matrix.Count;
            result.UsedColumnCount = matrix.Count == 0 ? 0 : matrix.Max(r => r == null ? 0 : r.Count);
        }

        private static string BuildA1Range(string sheetId, int startRow, int startColumn, int rowCount, int columnCount)
        {
            startRow = Math.Max(1, startRow);
            startColumn = Math.Max(1, startColumn);
            rowCount = Math.Max(1, rowCount);
            columnCount = Math.Max(1, columnCount);
            var endColumn = startColumn + columnCount - 1;
            var endRow = startRow + rowCount - 1;
            return sheetId + "!" + ToA1Column(startColumn) + startRow.ToString(CultureInfo.InvariantCulture) + ":" + ToA1Column(endColumn) + endRow.ToString(CultureInfo.InvariantCulture);
        }

        private static string ToA1Column(int oneBasedColumn)
        {
            var column = Math.Max(1, oneBasedColumn);
            var builder = new StringBuilder();
            while (column > 0)
            {
                column--;
                builder.Insert(0, (char)('A' + column % 26));
                column /= 26;
            }

            return builder.ToString();
        }

        private static bool UpsertProjectConfigSheet(JsonNode node, SeedTableContract table, SeedOnlineSheetResult sheet, BranchWorkspaceContract workspace)
        {
            var tableId = table.TableId;
            var target = FindProjectConfigTableNode(node, tableId, FirstNonEmpty(table.Branch, table.Profile));
            if (target == null)
            {
                return false;
            }

            var writeTarget = GetProjectConfigFeishuNode(target);
            if (writeTarget == null)
            {
                if (!HasAnyJsonProperty(target, "spreadsheetToken", "spreadsheet", "spreadsheetUrl", "url", "onlineSheetUrl", "sheetId", "wikiNodeToken", "branchWikiNodeToken"))
                {
                    return false;
                }

                writeTarget = target;
            }

            var changed = false;
            changed |= SetJsonString(writeTarget, "spreadsheetToken", sheet.SpreadsheetToken);
            changed |= SetJsonString(writeTarget, "sheetId", sheet.SheetId);
            changed |= SetJsonString(writeTarget, HasAnyJsonProperty(writeTarget, "url") ? "url" : "onlineSheetUrl", sheet.SpreadsheetUrl);
            changed |= SetJsonString(writeTarget, "branch", FirstNonEmpty(table.Branch, workspace.FeishuBranch));
            changed |= SetJsonString(writeTarget, "profile", FirstNonEmpty(table.Profile, workspace.Profile));
            changed |= SetJsonString(writeTarget, "wikiNodeToken", FirstNonEmpty(sheet.WikiNodeToken, table.WikiNodeToken, workspace.ExistingWikiNodeToken));
            changed |= SetJsonString(writeTarget, "branchWikiNodeToken", FirstNonEmpty(table.WikiRootToken, workspace.ExistingWikiNodeToken));
            changed |= SetJsonString(writeTarget, "branchWikiNodeUrl", FirstNonEmpty(table.WikiNodeUrl, workspace.ExistingWikiNodeUrl));
            return changed;
        }

        private SeedOnlineSheetResult FindExistingSheetInProjectConfig(SeedFromLocalXlsxContract seed, SeedTableContract table)
        {
            var path = ResolveWorkspacePath(FirstNonEmpty(table.ProjectConfigPath, seed.ProjectConfigPath));
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new SeedOnlineSheetResult();
            }

            try
            {
                var node = JsonNode.Parse(StripUtf8Bom(File.ReadAllText(path, Utf8NoBom)));
                var target = FindProjectConfigTableNode(node, table.TableId, FirstNonEmpty(table.Branch, table.Profile));
                if (target == null)
                {
                    return new SeedOnlineSheetResult();
                }

                return new SeedOnlineSheetResult
                {
                    SpreadsheetToken = GetProjectConfigString(target, "spreadsheetToken", "spreadsheet"),
                    SpreadsheetUrl = GetProjectConfigString(target, "spreadsheetUrl", "onlineSheetUrl", "url"),
                    SheetId = GetProjectConfigString(target, "sheetId"),
                    WikiNodeToken = GetProjectConfigString(target, "wikiNodeToken")
                };
            }
            catch
            {
                return new SeedOnlineSheetResult();
            }
        }

        private static JsonObject? FindProjectConfigTableNode(JsonNode? node, string tableId, string branchKey = "")
        {
            var candidates = new List<JsonObject>();
            CollectProjectConfigTableNodes(node, tableId, candidates);
            if (!string.IsNullOrWhiteSpace(branchKey))
            {
                foreach (var candidate in candidates)
                {
                    var candidateBranch = FirstNonEmpty(GetProjectConfigString(candidate, "branch", "feishuBranch"), GetProjectConfigString(candidate, "profile"));
                    if (string.Equals(candidateBranch, branchKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            return candidates.FirstOrDefault();
        }

        private static void CollectProjectConfigTableNodes(JsonNode? node, string tableId, IList<JsonObject> candidates)
        {
            if (node is JsonObject obj)
            {
                var id = GetJsonNodeString(obj, "tableId", "id", "key");
                if (string.Equals(id, tableId, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(obj);
                }

                foreach (var pair in obj)
                {
                    CollectProjectConfigTableNodes(pair.Value, tableId, candidates);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    CollectProjectConfigTableNodes(item, tableId, candidates);
                }
            }
        }

        private static JsonArray? FindProjectConfigTablesArray(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                foreach (var name in new[] { "tables", "configSheets", "tableMappings", "excelTables", "excelToSoTables" })
                {
                    if (obj.TryGetPropertyValue(name, out var value) && value is JsonArray array)
                    {
                        return array;
                    }
                }

                foreach (var pair in obj)
                {
                    var match = FindProjectConfigTablesArray(pair.Value);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        private static JsonObject? GetProjectConfigFeishuNode(JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (string.Equals(pair.Key, "feishu", StringComparison.OrdinalIgnoreCase) && pair.Value is JsonObject feishu)
                {
                    return feishu;
                }
            }

            return null;
        }

        private static string GetProjectConfigString(JsonObject obj, params string[] names)
        {
            var feishu = GetProjectConfigFeishuNode(obj);
            if (feishu != null)
            {
                var nested = GetJsonNodeString(feishu, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }

            return GetJsonNodeString(obj, names);
        }

        private static bool HasAnyJsonProperty(JsonObject obj, params string[] names)
        {
            foreach (var name in names)
            {
                foreach (var pair in obj)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string GetJsonNodeString(JsonObject obj, params string[] names)
        {
            foreach (var name in names)
            {
                foreach (var pair in obj)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return pair.Value == null ? "" : pair.Value.ToString();
                    }
                }
            }

            return "";
        }

        private static bool SetJsonString(JsonObject obj, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var pair in obj.ToList())
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(pair.Value == null ? "" : pair.Value.ToString(), value, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    obj[pair.Key] = value;
                    return true;
                }
            }

            obj[name] = value;
            return true;
        }

        private static string Trim(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            text = text.Trim();
            return text.Length <= 1000 ? text : text.Substring(0, 1000);
        }

        private string ResolveRegistryTableId(RegistryContract registry, string machineKey)
        {
            if (registry.TableIds.TryGetValue(machineKey, out var tableId) && !string.IsNullOrWhiteSpace(tableId))
            {
                return tableId;
            }

            if (!string.IsNullOrWhiteSpace(registry.BaseToken))
            {
                throw new CliException("注册中心缺少表 ID 映射：" + machineKey + "。请在 manifest/contract 中提供 registry.tableIds." + machineKey + " 或 feishu.registryBase.tables." + machineKey + "；如果项目配置无法表达 machine key，请改走项目 adapter 生成 apply-contract。不能把中文显示名当 table_id 调用 Base。", 2);
            }

            return RegistryLocalization.TableDisplayName(machineKey, _request.Locale);
        }

        private async Task<string> FindRegistryRecordIdAsync(string baseToken, string tableId, string keyFieldName, string keyValue)
        {
            if (string.IsNullOrWhiteSpace(keyValue))
            {
                return "";
            }

            return await FindRegistryRecordIdAsync(baseToken, tableId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [keyFieldName] = keyValue
            });
        }

        private async Task<string> FindRegistryRecordIdAsync(string baseToken, string tableId, IDictionary<string, string> keys)
        {
            if (keys == null || keys.Count == 0 || keys.Values.Any(string.IsNullOrWhiteSpace))
            {
                return "";
            }

            var list = await RunLarkCliStrictAsync(_gateway, _args, new[] { "base", "+record-list", "--base-token", baseToken, "--table-id", tableId, "--offset", "0", "--limit", "200", "--format", "json" });
            var records = ParseLarkBaseRecordListJson(CombinedJsonOutput(list));
            var matches = FindMatchingRegistryRecords(records, keys, _request.Locale, _request.Registry);
            if (matches.Count > 1)
            {
                throw BuildDuplicateRegistryRecordException(tableId, keys, matches);
            }

            return matches.Count == 1 ? matches[0].RecordId : "";
        }

        private static CliException BuildDuplicateRegistryRecordException(string tableId, IDictionary<string, string> keys, IReadOnlyList<RegistryRecordSnapshot> matches)
        {
            var recordIds = string.Join(", ", matches.Select(m => FirstNonEmpty(m.RecordId, "(无 record_id)")));
            var keyText = string.Join(" + ", keys.Select(k => k.Key + " “" + k.Value + "”"));
            var tableLabel = RegistryLookupLabel(keys);
            return new CliException(tableLabel + " 按 " + keyText + " 查到 " + matches.Count.ToString(CultureInfo.InvariantCulture) + " 条重复记录（record_id: " + recordIds + "）。请先运行 registry-migrate --dry-run 查看 cleanup/migrate 计划，确认后清理重复行；config-sheet-forge 不会静默任选一条。table_id=" + Mask(tableId), 2);
        }

        private static string RegistryLookupLabel(IDictionary<string, string> keys)
        {
            var names = keys.Keys.ToList();
            if (names.Any(n => n.Contains("GitBranch", StringComparison.OrdinalIgnoreCase) || n.Contains("Git分支", StringComparison.OrdinalIgnoreCase)) &&
                names.Any(n => n.Contains("Profile", StringComparison.OrdinalIgnoreCase) || n.Contains("配置Profile", StringComparison.OrdinalIgnoreCase)))
            {
                return "BranchBindings";
            }

            if (names.Any(n => n.Contains("TableId", StringComparison.OrdinalIgnoreCase) || n.Contains("配表ID", StringComparison.OrdinalIgnoreCase)) &&
                names.Any(n => n.Contains("Branch", StringComparison.OrdinalIgnoreCase) || n.Contains("分支", StringComparison.OrdinalIgnoreCase) || n.Contains("Profile", StringComparison.OrdinalIgnoreCase)))
            {
                return "ConfigSheets/SchemaReviews";
            }

            return "注册中心";
        }

        private static void ApplyWikiNodeJson(BranchWorkspaceResolution resolution, string json)
        {
            foreach (var node in FindJsonObjects(json, "node_token"))
            {
                resolution.WikiNodeToken = FirstNonEmpty(resolution.WikiNodeToken, GetJsonString(node, "node_token", "nodeToken"));
                resolution.WikiNodeUrl = FirstNonEmpty(resolution.WikiNodeUrl, GetJsonString(node, "url", "node_url", "nodeUrl"));
                resolution.NodeTitle = FirstNonEmpty(GetJsonString(node, "title"), resolution.NodeTitle);
                resolution.Status = FirstNonEmpty(GetJsonString(node, "space_id", "spaceId"), resolution.Status);
                resolution.CreatedBy = FirstNonEmpty(resolution.CreatedBy, GetJsonString(node, "creator", "owner"));
            }
        }

        private static BranchWorkspaceResolution FindWikiChildByTitle(string json, string title)
        {
            foreach (var node in FindJsonObjects(json, "node_token"))
            {
                if (!string.Equals(GetJsonString(node, "title"), title, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var resolution = new BranchWorkspaceResolution
                {
                    NodeTitle = title,
                    WikiNodeToken = GetJsonString(node, "node_token", "nodeToken"),
                    WikiNodeUrl = GetJsonString(node, "url", "node_url", "nodeUrl"),
                    Status = GetJsonString(node, "space_id", "spaceId")
                };
                return resolution;
            }

            return new BranchWorkspaceResolution();
        }

        private static SheetCreationResult FindWikiSheetChildByTitle(string json, string title)
        {
            foreach (var node in FindJsonObjects(json, "node_token"))
            {
                if (!string.Equals(GetJsonString(node, "title"), title, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var objType = GetJsonString(node, "obj_type", "objType", "type");
                if (!string.IsNullOrWhiteSpace(objType) && objType.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                return new SheetCreationResult
                {
                    SpreadsheetToken = FirstNonEmpty(GetJsonString(node, "obj_token", "objToken", "spreadsheet_token", "token"), GetJsonString(node, "file_token", "fileToken")),
                    SpreadsheetUrl = GetJsonString(node, "url", "node_url", "nodeUrl"),
                    WikiNodeToken = GetJsonString(node, "node_token", "nodeToken")
                };
            }

            return new SheetCreationResult();
        }

        private static SheetCreationResult ParseSheetCreationResult(string json)
        {
            var result = new SheetCreationResult();
            foreach (var element in FindJsonObjects(json, "spreadsheet_token").Concat(FindJsonObjects(json, "token")))
            {
                result.SpreadsheetToken = FirstNonEmpty(result.SpreadsheetToken, GetJsonString(element, "spreadsheet_token", "token"));
                result.SpreadsheetUrl = FirstNonEmpty(result.SpreadsheetUrl, GetJsonString(element, "url"));
                result.SheetId = FirstNonEmpty(result.SheetId, GetJsonString(element, "sheet_id", "sheetId"));
                result.WikiNodeToken = FirstNonEmpty(result.WikiNodeToken, GetJsonString(element, "node_token", "wiki_node_token"));
            }

            foreach (var sheet in FindJsonObjects(json, "sheet_id"))
            {
                result.SheetId = FirstNonEmpty(result.SheetId, GetJsonString(sheet, "sheet_id", "sheetId"));
            }

            return result;
        }

        private static string ParseRecordId(string json)
        {
            return Program.ParseLarkRecordId(json);
        }

        private static string JsonElementToText(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.ToString();
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

public sealed class ForgeConfig
{
    public string SchemaVersion { get; set; } = "1";
    public string Provider { get; set; } = "lark";
    public string RootUrl { get; set; } = "";
    public string RootToken { get; set; } = "";
    public string RootObjectType { get; set; } = "";
    public string RegistryPath { get; set; } = ".config-sheet-forge/registry.json";
    public string CacheDirectory { get; set; } = ".config-sheet-forge/cache";
    public Dictionary<string, string> ProviderSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TableRegistry
{
    public string SchemaVersion { get; set; } = "1";
    public List<TableConfig> Tables { get; set; } = new();
}

public sealed class TableConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "lark";
    public string Spreadsheet { get; set; } = "";
    public string SheetId { get; set; } = "";
    public string Range { get; set; } = "";
    public string LocalSourcePath { get; set; } = "";
    public bool UseExcelToSoCacheDialect { get; set; }
    public List<ContractFieldSpec> Fields { get; set; } = new();
    public int FieldRow { get; set; } = 0;
    public int TypeRow { get; set; } = -1;
    public int DescriptionRow { get; set; } = -1;
    public int DataStartRow { get; set; } = -1;
    public bool TreatUnknownTypesAsEnum { get; set; }
}

public sealed class Workspace
{
    public Workspace(string root, WorkspacePaths paths, ForgeConfig config, TableRegistry registry)
    {
        Root = root;
        Paths = paths;
        Config = config;
        Registry = registry;
    }

    public string Root { get; }
    public WorkspacePaths Paths { get; }
    public ForgeConfig Config { get; }
    public TableRegistry Registry { get; }
}

public sealed class WorkspacePaths
{
    public string StateDirectory { get; set; } = "";
    public string CacheDirectory { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string RegistryPath { get; set; } = "";

    public static WorkspacePaths For(string root)
    {
        var state = Path.Combine(root, ".config-sheet-forge");
        return new WorkspacePaths
        {
            StateDirectory = state,
            CacheDirectory = Path.Combine(state, "cache"),
            ConfigPath = Path.Combine(state, "config.json"),
            RegistryPath = Path.Combine(state, "registry.json")
        };
    }
}

public sealed class ParsedArgs
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positionals { get; } = new();

    public static ParsedArgs Parse(IEnumerable<string> args)
    {
        var parsed = new ParsedArgs();
        var items = args.ToList();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!item.StartsWith("--", StringComparison.Ordinal))
            {
                parsed.Positionals.Add(item);
                continue;
            }

            var keyValue = item.Substring(2).Split('=', 2);
            var key = keyValue[0];
            if (keyValue.Length == 2)
            {
                parsed._options[key] = keyValue[1];
                continue;
            }

            if (i + 1 < items.Count && !items[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed._options[key] = items[++i];
            }
            else
            {
                parsed._flags.Add(key);
            }
        }

        return parsed;
    }

    public bool HasFlag(string key) => _flags.Contains(key);

    public bool TryGet(string key, out string value) => _options.TryGetValue(key, out value!);

    public string Get(string key, string fallback)
    {
        return _options.TryGetValue(key, out var value) ? value : fallback;
    }

    public ParsedArgs WithDefault(string key, string value)
    {
        if (!_options.ContainsKey(key) && !_flags.Contains(key) && !string.IsNullOrWhiteSpace(key))
        {
            _options[key] = value ?? "";
        }

        return this;
    }

    public int GetInt(string key, int fallback)
    {
        return _options.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    public bool GetBool(string key, bool fallback)
    {
        if (_flags.Contains(key))
        {
            return true;
        }

        if (!_options.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CliException : Exception
{
    public CliException(string message, int exitCode, string detail = "") : base(message)
    {
        ExitCode = exitCode;
        Detail = detail;
    }

    public int ExitCode { get; }
    public string Detail { get; }
}
