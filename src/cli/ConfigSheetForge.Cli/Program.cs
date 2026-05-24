using System.Security.Cryptography;
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
        var candidates = await provider.DiscoverRootsAsync(CreateProviderContext(workspace), query, CancellationToken.None);

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
            var result = await tableProvider.ExportAsync(CreateProviderContext(workspace), new ProviderExportRequest
            {
                RootTokenOrUrl = FirstNonEmpty(table.Spreadsheet, workspace.Config.RootUrl, workspace.Config.RootToken),
                SpreadsheetTokenOrUrl = table.Spreadsheet,
                TableId = table.Id,
                SheetId = table.SheetId,
                Range = table.Range,
                CacheDirectory = workspace.Paths.CacheDirectory
            }, CancellationToken.None);

            foreach (var finding in result.Findings)
            {
                PrintFinding(finding, args.HasFlag("details"));
                if (finding.Severity == FindingSeverity.Error)
                {
                    hasError = true;
                }
            }

            if (result.Workbook != null)
            {
                var semanticPath = Path.Combine(workspace.Paths.CacheDirectory, table.Id + ".semantic.json");
                await WriteJsonAsync(semanticPath, result.Workbook);
            }

            if (!string.IsNullOrWhiteSpace(result.SemanticHash))
            {
                await File.WriteAllTextAsync(Path.Combine(workspace.Paths.CacheDirectory, table.Id + ".sha256"), result.SemanticHash + Environment.NewLine, Encoding.UTF8);
                Console.WriteLine(table.Id + ": " + result.SemanticHash);
                Console.WriteLine("  cache: " + FirstNonEmpty(result.CachePath, workspace.Paths.CacheDirectory));
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
            File.Copy(input, destination, overwrite: true);
            var fileHash = ComputeFileHash(destination);
            await File.WriteAllTextAsync(Path.Combine(workspace.Paths.CacheDirectory, tableId + ".sha256"), "file:" + fileHash + Environment.NewLine, Encoding.UTF8);
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
        var semanticPath = Path.Combine(workspace.Paths.CacheDirectory, tableId + ".semantic.json");
        await WriteJsonAsync(semanticPath, workbook);
        await File.WriteAllTextAsync(Path.Combine(workspace.Paths.CacheDirectory, tableId + ".sha256"), hash + Environment.NewLine, Encoding.UTF8);
        Console.WriteLine(tableId + ": " + hash);
        Console.WriteLine("  cache: " + semanticPath);
        return report.HasErrors ? 1 : 0;
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
        foreach (var file in files)
        {
            Console.WriteLine("Checking " + file);
            var workbook = await ReadJsonAsync<WorkbookDocument>(file);
            var report = SchemaReviewer.Review(workbook);
            foreach (var finding in report.Findings)
            {
                PrintValidation(finding, args.HasFlag("details"));
            }

            if (report.HasErrors)
            {
                hasError = true;
            }
        }

        return hasError ? 1 : 0;
    }

    private static IWorkbookProvider CreateProvider(string provider)
    {
        if (string.Equals(provider, "lark", StringComparison.OrdinalIgnoreCase) || string.Equals(provider, "feishu", StringComparison.OrdinalIgnoreCase))
        {
            return new LarkCliWorkbookProvider();
        }

        throw new CliException("Unknown provider '" + provider + "'. v0.1 supports the lark provider and keeps the provider boundary open for future implementations.", 2);
    }

    private static ProviderContext CreateProviderContext(Workspace workspace)
    {
        var context = new ProviderContext { WorkspaceRoot = workspace.Root };
        foreach (var pair in workspace.Config.ProviderSettings)
        {
            context.Settings[pair.Key] = pair.Value;
        }

        context.Settings["rootUrl"] = workspace.Config.RootUrl;
        context.Settings["rootToken"] = workspace.Config.RootToken;
        context.Settings["rootObjectType"] = workspace.Config.RootObjectType;
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
        Console.WriteLine("  config-sheet-forge new-table --id <id> --name <name> [--spreadsheet <url-or-token>] [--sheet-id <id>] [--range <A1>]");
        Console.WriteLine("  config-sheet-forge sync [--table <id>] [--input <semantic.json>]");
        Console.WriteLine("  config-sheet-forge merge --base <file> --ours <file> --theirs <file> [--out <report.md>]");
        Console.WriteLine("  config-sheet-forge gate [--cache <dir>] [--details]");
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
