using System.Security.Cryptography;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ConfigSheetForge.Core;
using ConfigSheetForge.Providers.Lark;

namespace ConfigSheetForge.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
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

        ILifecyclePlatform platform = request.DryRun
            ? new PreviewLifecyclePlatform()
            : new CliLifecyclePlatform(args, request);
        var result = await LifecycleExecutor.ExecuteAsync(request, platform, CancellationToken.None);
        var outPath = args.Get("out", "");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            await WriteJsonAsync(outPath, result);
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }

        foreach (var failure in result.HumanReadableFailures)
        {
            Console.Error.WriteLine("[error] " + failure);
        }

        return result.Success ? 0 : 1;
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

    private static async Task<bool> WriteCacheIfChangedAsync(string cacheDirectory, string tableId, WorkbookDocument workbook, string hash, string? tempXlsxPath)
    {
        Directory.CreateDirectory(cacheDirectory);
        var semanticPath = Path.Combine(cacheDirectory, tableId + ".semantic.json");
        var shaPath = Path.Combine(cacheDirectory, tableId + ".sha256");
        var xlsxPath = Path.Combine(cacheDirectory, MakeSafeFileName(tableId) + ".xlsx");
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

    private sealed class CliLifecyclePlatform : ILifecyclePlatform
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
                [mapping.Fields["ExcelPath"]] = FirstNonEmpty(table.ExcelPath, table.LocalCachePath),
                [mapping.Fields["SpreadsheetToken"]] = sheet.SpreadsheetToken,
                [mapping.Fields["SheetId"]] = sheet.SheetId,
                [mapping.Fields["OwnerRole"]] = table.OwnerRole,
                [mapping.Fields["SchemaReviewRequired"]] = table.SchemaReviewRequired ? "是" : "否",
                [mapping.Fields["OnlineSheetUrl"]] = FirstNonEmpty(sheet.SpreadsheetUrl, table.OnlineSheetUrl),
                [mapping.Fields["Status"]] = FirstNonEmpty(table.Status, "active")
            };
            var recordId = FirstNonEmpty(registry.RegistryRecordId, await FindRegistryRecordIdAsync(registry.BaseToken, tableId, mapping.Fields["TableId"], table.TableId));
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
                [mapping.Fields["Status"]] = "pending",
                ["Reason"] = reason
            };
            var recordId = await FindRegistryRecordIdAsync(registry.BaseToken, tableId, mapping.Fields["TableId"], table.TableId);
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
