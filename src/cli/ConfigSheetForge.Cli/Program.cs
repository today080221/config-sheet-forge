using System.Security.Cryptography;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public static async Task<int> Main(string[] args)
    {
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
                "seed-from-xlsx" => await SeedFromXlsxAsync(parsed),
                "bootstrap-target-branch-from-local-xlsx" => await SeedFromXlsxAsync(parsed, "bootstrap-target-branch-from-local-xlsx"),
                "merge" => await MergeAsync(parsed),
                "gate" => await GateAsync(parsed),
                "apply-contract" => await ApplyContractAsync(parsed),
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

    private static async Task<int> SyncTableConfigsAsync(Workspace workspace, ParsedArgs args, IList<TableConfig> tables, string cacheDirectory, string excelCacheDirectory)
    {
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(excelCacheDirectory);
        var hasError = false;
        foreach (var table in tables)
        {
            var tableProvider = CreateProvider(FirstNonEmpty(table.Provider, workspace.Config.Provider));
            var tableTemp = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ConfigSheetForge", "sync-cache-temp", Guid.NewGuid().ToString("N"), table.Id);
            Directory.CreateDirectory(tableTemp);
            Console.WriteLine("[stage] 正在读取在线 Sheet: " + table.Id);
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

            foreach (var finding in result.Findings)
            {
                PrintFinding(finding, args.HasFlag("details"));
                if (finding.Severity == FindingSeverity.Error)
                {
                    hasError = true;
                }
            }

            var tableHasError = result.Findings.Any(f => f.Severity == FindingSeverity.Error);
            if (result.Workbook == null)
            {
                hasError = true;
                continue;
            }

            Console.WriteLine("[stage] 正在三方一致性检查: " + table.Id);
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
                Console.WriteLine("[stage] 正在 hash gate: " + table.Id);
                var hash = SemanticHasher.ComputeHash(result.Workbook);
                var cacheWrite = await WriteCacheIfChangedAsync(cacheDirectory, table.Id, result.Workbook, hash, xlsxPath, excelCacheDirectory);
                Console.WriteLine(table.Id + ": " + hash);
                Console.WriteLine(cacheWrite ? "  cache updated" : "  无变化，未重写 cache");
            }
        }

        return hasError ? 1 : 0;
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
                await File.WriteAllTextAsync(Path.Combine(workspace.Paths.CacheDirectory, tableId + ".sha256"), fileCacheHash + Environment.NewLine, Encoding.UTF8);
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
        if (args.HasFlag("allow-user-fallback"))
        {
            throw new CliException("sync-cache 默认使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
        }

        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        var request = args.TryGet("manifest", out var manifestPath)
            ? await ReadSeedManifestAsync(workspace, manifestPath, args)
            : NewSeedRequestFromWorkspace(workspace, args);
        request.Operation = "sync-cache";
        request.SyncCache ??= new SyncCacheContract();
        request.DryRun = args.HasFlag("dry-run") || !args.HasFlag("yes");
        request.SyncCache.TableId = args.Get("table", "");
        request.SyncCache.CacheDirectory = args.Get("cache-dir", workspace.Paths.CacheDirectory);
        request.SyncCache.ExcelCacheDirectory = args.Get("excel-cache-dir", Path.Combine(workspace.Paths.StateDirectory, "excel-cache"));
        request.SyncCache.ConfirmApply = args.HasFlag("yes") || args.HasFlag("confirm");
        if (!request.DryRun && !request.SyncCache.ConfirmApply)
        {
            throw new CliException("sync-cache apply 会更新本地 cache，必须显式传 --yes。", 2);
        }

        await HydrateSyncCacheRequestFromRegistryAsync(request, args, request.SyncCache.TableId);
        var result = await LifecycleExecutor.ExecuteAsync(request, new CliLifecyclePlatform(args, request), CancellationToken.None);
        if (result.Success && !request.DryRun)
        {
            var tables = BuildSyncCacheTables(request, args.Get("table", ""));
            if (tables.Count == 0)
            {
                result.AddFailure("sync-cache apply 找不到当前 branch/profile 的在线 Sheet 定位信息。请确认 ConfigSheets/ProjectSettings 已包含 spreadsheetToken、sheetId 和 TableId + Branch/Profile。");
            }
            else
            {
                var exit = await SyncTableConfigsAsync(workspace, args, tables, request.SyncCache.CacheDirectory, request.SyncCache.ExcelCacheDirectory);
                if (exit != 0)
                {
                    result.AddFailure("sync-cache apply 没有通过在线读取 / xlsx 导出 / 三方一致性检查，已阻断 cache 更新。");
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

    private static List<TableConfig> BuildSyncCacheTables(LifecycleContractRequest request, string selectedTable)
    {
        var seed = request.SeedFromLocalXlsx ?? new SeedFromLocalXlsxContract();
        var tables = new List<TableConfig>();
        foreach (var table in seed.Tables)
        {
            if (!string.IsNullOrWhiteSpace(selectedTable) && !string.Equals(selectedTable, table.TableId, StringComparison.OrdinalIgnoreCase))
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
                FieldRow = table.FieldRow,
                TypeRow = table.TypeRow,
                DescriptionRow = table.DescriptionRow,
                DataStartRow = table.DataStartRow,
                TreatUnknownTypesAsEnum = table.TreatUnknownTypesAsEnum
            });
        }

        return tables;
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

        var json = await File.ReadAllTextAsync(manifestPath, Encoding.UTF8);
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
        await File.WriteAllTextAsync(reportPath, RenderMergeReport(report), Encoding.UTF8);
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
        if (args.HasFlag("dry-run"))
        {
            request.DryRun = true;
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
            if (args.HasFlag("allow-user-fallback"))
            {
                throw new CliException("sync-cache 默认使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
            }

            request.SyncCache.ConfirmApply = request.SyncCache.ConfirmApply || args.HasFlag("yes") || args.HasFlag("confirm");
            if (!request.DryRun && !request.SyncCache.ConfirmApply)
            {
                throw new CliException("sync-cache apply 会更新本地 cache，必须显式传 --yes，或在 contract.syncCache.confirmApply=true。", 2);
            }

            await HydrateSyncCacheRequestFromRegistryAsync(request, args, request.SyncCache.TableId);
        }

        if (CompareMergeOperationRequested(request.Operation))
        {
            request.MergeInputs ??= new MergeInputsContract();
            request.MergePolicy ??= new MergePolicyContract();
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

        ILifecyclePlatform platform = request.DryRun && !SeedOperationRequested(request.Operation)
            ? new PreviewLifecyclePlatform()
            : new CliLifecyclePlatform(args, request);
        var result = await LifecycleExecutor.ExecuteAsync(request, platform, CancellationToken.None);
        if (SyncCacheOperationRequested(request.Operation) && result.Success && !request.DryRun)
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
                var exit = await SyncTableConfigsAsync(workspace, args, tables, request.SyncCache.CacheDirectory, request.SyncCache.ExcelCacheDirectory);
                if (exit != 0)
                {
                    result.AddFailure("sync-cache apply 没有通过在线读取 / xlsx 导出 / 三方一致性检查，已阻断 cache 更新。");
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
                Message = "预览：不会写入 Base。"
            });
        }
        else
        {
            if (plan.Actions.Any(IsDestructiveRegistryMigrationAction) && !args.HasFlag("yes"))
            {
                throw new CliException("registry-migrate apply 包含删除空白默认行/默认字段/重复 BranchBindings 等危险动作，必须显式传 --yes。建议先运行 --dry-run 审计 record_id 后再确认。", 2);
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
            IsDefaultField = IsDefaultBaseField(displayName)
        });
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

    private static bool SyncCacheOperationRequested(string operation)
    {
        return string.Equals(operation, "sync-cache", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation, "sync-from-online-sheet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompareMergeOperationRequested(string operation)
    {
        return string.Equals(operation, "compare-merge", StringComparison.OrdinalIgnoreCase);
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
        Console.WriteLine("  config-sheet-forge sync-cache [--table <id>] [--manifest <project-config-or-contract>] [--dry-run] [--yes]");
        Console.WriteLine("  config-sheet-forge seed-from-xlsx --table <id> --source-xlsx <path> --dry-run");
        Console.WriteLine("  config-sheet-forge seed-from-xlsx --all --manifest <project-config-or-contract> --dry-run");
        Console.WriteLine("  config-sheet-forge bootstrap-target-branch-from-local-xlsx --all --manifest <project-config-or-contract> --target-branch main --dry-run");
        Console.WriteLine("  config-sheet-forge bootstrap-target-branch-from-local-xlsx --all --manifest <project-config-or-contract> --target-branch main --preview-result <dry-run-result.json> --confirm-create-online-sheets --confirm-registry-upsert --confirm-schema-reviews");
        Console.WriteLine("    apply flags: --confirm-create-online-sheets --confirm-registry-upsert --confirm-schema-reviews [--confirm-write-local-cache] [--confirm-write-project-config] [--confirm-excel-to-so]");
        Console.WriteLine("  config-sheet-forge merge --base <file> --ours <file> --theirs <file> [--out <report.md>]");
        Console.WriteLine("  config-sheet-forge gate [--cache <dir>] [--details] [--annotations github]");
        Console.WriteLine("  config-sheet-forge apply-contract --request <contract.json> [--out <result.json>] [--dry-run]");
        Console.WriteLine("  config-sheet-forge registry-migrate --base <token> [--locale zh-Hans] [--dry-run] [--cleanup-default-rows] [--cleanup-default-fields] [--cleanup-duplicate-branch-bindings] [--yes]");
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

    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
        var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
        if (value == null)
        {
            throw new CliException("Could not read JSON from " + path, 2);
        }

        return value;
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    private static async Task<bool> WriteCacheIfChangedAsync(string cacheDirectory, string tableId, WorkbookDocument workbook, string hash, string? tempXlsxPath, string? xlsxDirectory = null)
    {
        Directory.CreateDirectory(cacheDirectory);
        var xlsxRoot = FirstNonEmpty(xlsxDirectory ?? "", cacheDirectory);
        Directory.CreateDirectory(xlsxRoot);
        var semanticPath = Path.Combine(cacheDirectory, tableId + ".semantic.json");
        var shaPath = Path.Combine(cacheDirectory, tableId + ".sha256");
        var xlsxPath = Path.Combine(xlsxRoot, MakeSafeFileName(tableId) + ".xlsx");
        var existingHash = await ReadExistingHashAsync(shaPath);
        var hasRequiredFiles = File.Exists(semanticPath) && (string.IsNullOrWhiteSpace(tempXlsxPath) || File.Exists(xlsxPath));
        if (string.Equals(existingHash, hash, StringComparison.Ordinal) && hasRequiredFiles)
        {
            return false;
        }

        await WriteJsonAsync(semanticPath, workbook);
        await File.WriteAllTextAsync(shaPath, hash + Environment.NewLine, Encoding.UTF8);
        if (!string.IsNullOrWhiteSpace(tempXlsxPath) && File.Exists(tempXlsxPath))
        {
            File.Copy(tempXlsxPath, xlsxPath, overwrite: true);
        }

        return true;
    }

    private static async Task<string> ReadExistingHashAsync(string path)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8);
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

    private sealed class CliLifecyclePlatform : ILifecyclePlatform, ISeedFromLocalXlsxPlatform, IBranchWorkspacePlatform, ITargetBranchBootstrapPostflightPlatform
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
            var existingHash = await ReadExistingHashAsync(shaPath);
            var unchanged = string.Equals(existingHash, semanticHash, StringComparison.Ordinal) &&
                            File.Exists(xlsxPath) &&
                            File.Exists(semanticPath) &&
                            File.Exists(shaPath);
            if (!unchanged)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(xlsxPath) ?? ".");
                Directory.CreateDirectory(Path.GetDirectoryName(semanticPath) ?? ".");
                Directory.CreateDirectory(Path.GetDirectoryName(shaPath) ?? ".");
                if (!string.IsNullOrWhiteSpace(exportedXlsxPath) && File.Exists(exportedXlsxPath))
                {
                    File.Copy(exportedXlsxPath, xlsxPath, overwrite: true);
                }

                await WriteJsonAsync(semanticPath, localWorkbook);
                await File.WriteAllTextAsync(shaPath, semanticHash + Environment.NewLine, Encoding.UTF8, cancellationToken);
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

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
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
                await File.WriteAllTextAsync(path, node.ToJsonString(JsonOptions) + Environment.NewLine, Encoding.UTF8, cancellationToken);
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
                var node = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
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
            foreach (var record in FindJsonObjects(json, "record_id"))
            {
                var id = GetJsonString(record, "record_id", "recordId");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }

            return "";
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
