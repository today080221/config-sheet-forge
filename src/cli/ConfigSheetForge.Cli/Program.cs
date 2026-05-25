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
            var tableTemp = Path.Combine(Path.GetTempPath(), "csforge-sync-" + Guid.NewGuid().ToString("N"), table.Id);
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
                    Console.WriteLine(cacheWrite ? "  cache updated" : "  cache unchanged");
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
            var tableTemp = Path.Combine(Path.GetTempPath(), "csforge-sync-cache-" + Guid.NewGuid().ToString("N"), table.Id);
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
            if (result.Workbook == null)
            {
                hasError = true;
                continue;
            }

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
                var cacheWrite = await WriteCacheIfChangedAsync(cacheDirectory, table.Id, result.Workbook, hash, xlsxPath, excelCacheDirectory);
                Console.WriteLine(table.Id + ": " + hash);
                Console.WriteLine(cacheWrite ? "  cache updated" : "  cache unchanged");
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

    private static async Task<int> SeedFromXlsxAsync(ParsedArgs args)
    {
        if (args.HasFlag("allow-user-fallback"))
        {
            throw new CliException("seed-from-xlsx 默认且固定使用 strict bot 权限；bot 权限不足时不会 fallback 到 user。请补应用 scope/资源权限后重试。", 2);
        }

        var workspace = await LoadWorkspaceAsync(requireConfig: false);
        var request = await BuildSeedRequestAsync(workspace, args);
        request.Operation = "seed-from-local-xlsx";
        request.DryRun = args.HasFlag("dry-run") || request.DryRun;
        request.SeedFromLocalXlsx.ConfirmApply = request.SeedFromLocalXlsx.ConfirmApply || args.HasFlag("yes") || args.HasFlag("confirm");
        request.SeedFromLocalXlsx.ConfirmExcelToSoSettingsUpdate = request.SeedFromLocalXlsx.ConfirmExcelToSoSettingsUpdate || args.HasFlag("confirm-excel-to-so");
        request.SeedFromLocalXlsx.ConfirmProjectConfigUpdate = request.SeedFromLocalXlsx.ConfirmProjectConfigUpdate || args.HasFlag("confirm-project-config");

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
        request.DryRun = args.HasFlag("dry-run") || !args.HasFlag("yes");
        request.SyncCache.TableId = args.Get("table", "");
        request.SyncCache.CacheDirectory = args.Get("cache-dir", workspace.Paths.CacheDirectory);
        request.SyncCache.ExcelCacheDirectory = args.Get("excel-cache-dir", Path.Combine(workspace.Paths.StateDirectory, "excel-cache"));
        request.SyncCache.ConfirmApply = args.HasFlag("yes") || args.HasFlag("confirm");
        if (!request.DryRun && !request.SyncCache.ConfirmApply)
        {
            throw new CliException("sync-cache apply 会更新本地 cache，必须显式传 --yes。", 2);
        }

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
                WikiRootToken = FirstNonEmpty(args.Get("wiki-root", ""), workspace.Config.RootToken, workspace.Config.RootUrl),
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
        seedRequest.SeedFromLocalXlsx.WikiRootToken = FirstNonEmpty(seedRequest.SeedFromLocalXlsx.WikiRootToken, FindStringDeep(root, "wikiRootToken", "feishuRootToken", "rootToken"));
        seedRequest.SeedFromLocalXlsx.WikiParentTitle = FirstNonEmpty(FindStringDeep(root, "wikiRootTitle", "rootWikiTitle", "wikiParentTitle"), seedRequest.SeedFromLocalXlsx.WikiParentTitle);
        seedRequest.SeedFromLocalXlsx.CacheDirectory = FirstNonEmpty(FindStringDeep(root, "semanticCacheDirectory", "cacheDirectory"), seedRequest.SeedFromLocalXlsx.CacheDirectory);
        seedRequest.SeedFromLocalXlsx.ExcelCacheDirectory = FirstNonEmpty(FindStringDeep(root, "excelCacheDirectory", "xlsxCacheDirectory"), seedRequest.SeedFromLocalXlsx.ExcelCacheDirectory);
        seedRequest.SeedFromLocalXlsx.BaselineStrategy = FirstNonEmpty(FindStringDeep(root, "baselineStrategy", "schemaReviewBaselineStrategy"), seedRequest.SeedFromLocalXlsx.BaselineStrategy);
        seedRequest.BranchWorkspace.Mode = FirstNonEmpty(FindStringDeep(root, "branchBindingMode", "mode"), seedRequest.BranchWorkspace.Mode);
        seedRequest.BranchWorkspace.RootWikiToken = FirstNonEmpty(FindStringDeep(root, "branchWorkspaceRootWikiToken", "wikiRootToken", "feishuRootToken", "rootToken"), seedRequest.SeedFromLocalXlsx.WikiRootToken);
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
            var table = new SeedTableContract
            {
                TableId = tableId,
                DisplayName = FirstNonEmpty(GetJsonString(tableElement, "displayName", "name", "title"), tableId),
                SourceXlsxPath = FirstNonEmpty(GetJsonString(tableElement, "sourceXlsxPath", "sourceXlsx", "oldExcelPath", "localSourcePath"), ""),
                CacheXlsxPath = cacheXlsx,
                SemanticCachePath = FirstNonEmpty(GetJsonString(tableElement, "semanticCachePath"), Path.Combine(seedRequest.SeedFromLocalXlsx.CacheDirectory, tableId + ".semantic.json")),
                HashCachePath = FirstNonEmpty(GetJsonString(tableElement, "hashCachePath", "sha256Path"), Path.Combine(seedRequest.SeedFromLocalXlsx.CacheDirectory, tableId + ".sha256")),
                ProjectConfigPath = Path.GetFullPath(manifestPath),
                SpreadsheetToken = GetJsonString(tableElement, "spreadsheetToken", "spreadsheet"),
                SpreadsheetUrl = GetJsonString(tableElement, "spreadsheetUrl", "url", "onlineSheetUrl"),
                SheetId = GetJsonString(tableElement, "sheetId"),
                SheetName = FirstNonEmpty(GetJsonString(tableElement, "sheetName"), tableId),
                WikiRootToken = FirstNonEmpty(GetJsonString(tableElement, "wikiRootToken"), seedRequest.SeedFromLocalXlsx.WikiRootToken),
                WikiNodeUrl = GetJsonString(tableElement, "wikiNodeUrl", "branchWikiNodeUrl"),
                Branch = GetJsonString(tableElement, "branch", "feishuBranch"),
                Profile = GetJsonString(tableElement, "profile", "feishuProfile"),
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

            request.SeedFromLocalXlsx.ConfirmApply = request.SeedFromLocalXlsx.ConfirmApply || args.HasFlag("yes") || args.HasFlag("confirm");
            request.SeedFromLocalXlsx.ConfirmExcelToSoSettingsUpdate = request.SeedFromLocalXlsx.ConfirmExcelToSoSettingsUpdate || args.HasFlag("confirm-excel-to-so");
            request.SeedFromLocalXlsx.ConfirmProjectConfigUpdate = request.SeedFromLocalXlsx.ConfirmProjectConfigUpdate || args.HasFlag("confirm-project-config");
        }

        ILifecyclePlatform platform = request.DryRun && !SeedOperationRequested(request.Operation)
            ? new PreviewLifecyclePlatform()
            : new CliLifecyclePlatform(args, request);
        var result = await LifecycleExecutor.ExecuteAsync(request, platform, CancellationToken.None);
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
            CleanupDefaultFields = args.HasFlag("cleanup-default-fields")
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

    private static async Task<RegistrySnapshot> LoadRegistrySnapshotFromLarkAsync(LarkCliGateway gateway, string baseToken, string locale, ParsedArgs args)
    {
        var snapshot = new RegistrySnapshot();
        var tableResult = await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+table-list", "--base-token", baseToken, "--offset", "0", "--limit", "100" });
        foreach (var tableElement in FindJsonObjects(CombinedJsonOutput(tableResult), "table_id"))
        {
            var tableId = GetJsonString(tableElement, "table_id", "tableId");
            if (string.IsNullOrWhiteSpace(tableId))
            {
                continue;
            }

            var displayName = GetJsonString(tableElement, "table_name", "name", "tableName");
            var table = new RegistryTableSnapshot
            {
                TableId = tableId,
                DisplayName = displayName,
                MachineKey = ResolveMachineKey(displayName, RegistryLocalization.Default(locale).Tables)
            };
            snapshot.Tables.Add(table);

            var fieldResult = await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+field-list", "--base-token", baseToken, "--table-id", tableId, "--offset", "0", "--limit", "200" });
            foreach (var fieldElement in FindJsonObjects(CombinedJsonOutput(fieldResult), "field_id"))
            {
                var fieldDisplay = GetJsonString(fieldElement, "field_name", "name", "fieldName");
                var field = new RegistryFieldSnapshot
                {
                    FieldId = GetJsonString(fieldElement, "field_id", "fieldId"),
                    DisplayName = fieldDisplay,
                    MachineKey = ResolveMachineKey(fieldDisplay, RegistryLocalization.Default(locale).Fields),
                    Type = GetJsonString(fieldElement, "type", "field_type", "fieldType"),
                    IsDefaultField = IsDefaultBaseField(fieldDisplay)
                };
                table.Fields.Add(field);
            }

            var recordResult = await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+record-list", "--base-token", baseToken, "--table-id", tableId, "--offset", "0", "--limit", "200" });
            foreach (var recordElement in FindJsonObjects(CombinedJsonOutput(recordResult), "record_id"))
            {
                var record = new RegistryRecordSnapshot { RecordId = GetJsonString(recordElement, "record_id", "recordId") };
                if (recordElement.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in fields.EnumerateObject())
                    {
                        record.Values[field.Name] = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() ?? "" : field.Value.ToString();
                    }
                }

                table.Records.Add(record);
            }
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
                        });
                        await RunLarkCliStrictAsync(gateway, args, new[] { "base", "+field-update", "--base-token", baseToken, "--table-id", GetDetail(action, "tableId"), "--field-id", GetDetail(action, "fieldId"), "--json", fieldJson, "--yes" });
                        action.Status = "done";
                        break;
                    case "registry.record.delete_empty":
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
        var doctor = await gateway.RunAsync(new[] { "doctor" }, Directory.GetCurrentDirectory(), CancellationToken.None);
        if (!doctor.Success)
        {
            throw new CliException("lark-cli doctor 没有通过。请先修复本地 Feishu CLI 配置和权限。", 1, doctor.Stderr);
        }

        var identity = args.Get("lark-identity", "bot");
        var first = await gateway.RunAsync(WithLarkIdentity(commandArgs, identity), Directory.GetCurrentDirectory(), CancellationToken.None);
        if (first.Success || !string.Equals(identity, "bot", StringComparison.OrdinalIgnoreCase) || !args.HasFlag("allow-user-fallback"))
        {
            if (!first.Success)
            {
                throw new CliException("飞书操作失败。当前是 bot 严格模式，不会静默切换到 user；请补应用权限，或显式传 --allow-user-fallback。", 1, first.Stderr);
            }

            return first;
        }

        var fallback = await gateway.RunAsync(WithLarkIdentity(commandArgs, "user"), Directory.GetCurrentDirectory(), CancellationToken.None);
        if (!fallback.Success)
        {
            throw new CliException("飞书操作失败，bot 和显式允许的 user fallback 都没有成功。", 1, fallback.Stderr);
        }

        return fallback;
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
               string.Equals(operation, "bootstrap-from-local-xlsx", StringComparison.OrdinalIgnoreCase);
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
        Console.WriteLine("  config-sheet-forge merge --base <file> --ours <file> --theirs <file> [--out <report.md>]");
        Console.WriteLine("  config-sheet-forge gate [--cache <dir>] [--details] [--annotations github]");
        Console.WriteLine("  config-sheet-forge apply-contract --request <contract.json> [--out <result.json>] [--dry-run]");
        Console.WriteLine("  config-sheet-forge registry-migrate --base <token> [--locale zh-Hans] [--cleanup-default-rows] [--cleanup-default-fields]");
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

    private sealed class CliLifecyclePlatform : ILifecyclePlatform, ISeedFromLocalXlsxPlatform, IBranchWorkspacePlatform
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
            var values = JsonSerializer.Serialize(templateRows);
            await RunLarkCliStrictAsync(_gateway, _args, new[] { "sheets", "+write", "--spreadsheet-token", sheet.SpreadsheetToken, "--sheet-id", sheet.SheetId, "--range", sheet.SheetId + "!A1", "--values", values });
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
                await FindRegistryRecordIdAsync(registry.BaseToken, tableId, mapping.Fields["TableId"], table.TableId));
            var command = new List<string> { "base", "+record-upsert", "--base-token", registry.BaseToken, "--table-id", tableId, "--json", JsonSerializer.Serialize(body) };
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
            var command = new List<string> { "base", "+record-upsert", "--base-token", registry.BaseToken, "--table-id", tableId, "--json", JsonSerializer.Serialize(body) };
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
            if (!string.IsNullOrWhiteSpace(planned.WikiNodeToken))
            {
                try
                {
                    var existing = await RunLarkCliStrictAsync(_gateway, _args, new[] { "wiki", "+node-get", "--node-token", planned.WikiNodeToken });
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

            if (string.IsNullOrWhiteSpace(planned.RootWikiToken))
            {
                planned.Status = "failed";
                return planned;
            }

            var rootInfo = new BranchWorkspaceResolution();
            try
            {
                var root = await RunLarkCliStrictAsync(_gateway, _args, new[] { "wiki", "+node-get", "--node-token", planned.RootWikiToken });
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
            var command = new List<string> { "base", "+record-upsert", "--base-token", registry.BaseToken, "--table-id", tableId, "--json", JsonSerializer.Serialize(body, JsonOptions) };
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

            var matrix = BuildMatrixFromWorkbook(localWorkbook);
            var values = JsonSerializer.Serialize(matrix, JsonOptions);
            await RunLarkCliStrictAsync(_gateway, _args, new[] { "sheets", "+write", "--spreadsheet-token", result.SpreadsheetToken, "--sheet-id", result.SheetId, "--range", result.SheetId + "!A1", "--values", values });
            return result;
        }

        public async Task<SeedOnlineRoundTripResult> ReadAndExportOnlineSheetAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken)
        {
            var result = new SeedOnlineRoundTripResult();
            var temp = Path.Combine(Path.GetTempPath(), "csforge-seed-" + Guid.NewGuid().ToString("N"), MakeSafeFileName(table.TableId));
            Directory.CreateDirectory(temp);
            var provider = new LarkCliWorkbookProvider();
            var export = await provider.ExportAsync(BuildProviderContext(), new ProviderExportRequest
            {
                RootTokenOrUrl = FirstNonEmpty(sheet.SpreadsheetToken, sheet.SpreadsheetUrl),
                SpreadsheetTokenOrUrl = FirstNonEmpty(sheet.SpreadsheetToken, sheet.SpreadsheetUrl),
                TableId = table.TableId,
                SheetId = FirstNonEmpty(sheet.SheetId, table.SheetId),
                Range = "",
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
            if (tokenOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                tokenOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "sheets", "+info", "--url", tokenOrUrl };
            }

            return new[] { "sheets", "+info", "--spreadsheet-token", tokenOrUrl };
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
                columns.Select(c => FirstNonEmpty(c.Key, c.DisplayName)).ToList(),
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

        private static bool UpsertProjectConfigSheet(JsonNode node, SeedTableContract table, SeedOnlineSheetResult sheet, BranchWorkspaceContract workspace)
        {
            var tableId = table.TableId;
            var target = FindProjectConfigTableNode(node, tableId, FirstNonEmpty(table.Branch, table.Profile));
            if (target == null)
            {
                var tables = FindProjectConfigTablesArray(node);
                if (tables == null)
                {
                    return false;
                }

                target = new JsonObject
                {
                    ["id"] = tableId
                };
                tables.Add(target);
            }

            var changed = false;
            changed |= SetJsonString(target, "spreadsheetToken", sheet.SpreadsheetToken);
            changed |= SetJsonString(target, "sheetId", sheet.SheetId);
            changed |= SetJsonString(target, "url", sheet.SpreadsheetUrl);
            changed |= SetJsonString(target, "onlineSheetUrl", sheet.SpreadsheetUrl);
            changed |= SetJsonString(target, "branch", FirstNonEmpty(table.Branch, workspace.FeishuBranch));
            changed |= SetJsonString(target, "profile", FirstNonEmpty(table.Profile, workspace.Profile));
            changed |= SetJsonString(target, "wikiNodeToken", FirstNonEmpty(sheet.WikiNodeToken, table.WikiNodeToken, workspace.ExistingWikiNodeToken));
            changed |= SetJsonString(target, "branchWikiNodeToken", FirstNonEmpty(table.WikiRootToken, workspace.ExistingWikiNodeToken));
            changed |= SetJsonString(target, "branchWikiNodeUrl", FirstNonEmpty(table.WikiNodeUrl, workspace.ExistingWikiNodeUrl));
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
                    SpreadsheetToken = GetJsonNodeString(target, "spreadsheetToken", "spreadsheet"),
                    SpreadsheetUrl = GetJsonNodeString(target, "spreadsheetUrl", "onlineSheetUrl", "url"),
                    SheetId = GetJsonNodeString(target, "sheetId"),
                    WikiNodeToken = GetJsonNodeString(target, "wikiNodeToken")
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
                    var candidateBranch = FirstNonEmpty(GetJsonNodeString(candidate, "branch", "feishuBranch"), GetJsonNodeString(candidate, "profile"));
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

            return RegistryLocalization.TableDisplayName(machineKey, _request.Locale);
        }

        private async Task<string> FindRegistryRecordIdAsync(string baseToken, string tableId, string keyFieldName, string keyValue)
        {
            if (string.IsNullOrWhiteSpace(keyValue))
            {
                return "";
            }

            var list = await RunLarkCliStrictAsync(_gateway, _args, new[] { "base", "+record-list", "--base-token", baseToken, "--table-id", tableId, "--offset", "0", "--limit", "200" });
            foreach (var record in FindJsonObjects(CombinedJsonOutput(list), "record_id"))
            {
                if (!record.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var field in fields.EnumerateObject())
                {
                    if (string.Equals(field.Name, keyFieldName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(JsonElementToText(field.Value), keyValue, StringComparison.OrdinalIgnoreCase))
                    {
                        return GetJsonString(record, "record_id", "recordId");
                    }
                }
            }

            return "";
        }

        private async Task<string> FindRegistryRecordIdAsync(string baseToken, string tableId, IDictionary<string, string> keys)
        {
            if (keys == null || keys.Count == 0 || keys.Values.Any(string.IsNullOrWhiteSpace))
            {
                return "";
            }

            var list = await RunLarkCliStrictAsync(_gateway, _args, new[] { "base", "+record-list", "--base-token", baseToken, "--table-id", tableId, "--offset", "0", "--limit", "200" });
            foreach (var record in FindJsonObjects(CombinedJsonOutput(list), "record_id"))
            {
                if (!record.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var matched = 0;
                foreach (var key in keys)
                {
                    foreach (var field in fields.EnumerateObject())
                    {
                        if (string.Equals(field.Name, key.Key, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(JsonElementToText(field.Value), key.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            matched++;
                            break;
                        }
                    }
                }

                if (matched == keys.Count)
                {
                    return GetJsonString(record, "record_id", "recordId");
                }
            }

            return "";
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
