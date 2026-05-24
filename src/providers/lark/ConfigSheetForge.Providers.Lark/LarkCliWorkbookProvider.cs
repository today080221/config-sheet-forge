using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConfigSheetForge.Core;

namespace ConfigSheetForge.Providers.Lark;

public sealed class LarkCliWorkbookProvider : IWorkbookProvider
{
    public string Id => "lark";

    public async Task<IList<ProviderDoctorFinding>> DoctorAsync(ProviderContext context, CancellationToken cancellationToken)
    {
        var findings = new List<ProviderDoctorFinding>();
        var gateway = CreateGateway(context);

        var doctor = await gateway.RunAsync(new[] { "doctor" }, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!doctor.Success && doctor.ExitCode == -1)
        {
            findings.Add(Finding(FindingSeverity.Error, "lark.cli_missing", "lark-cli was not found. Install @larksuite/cli and run lark-cli doctor before syncing."));
            findings[^1].Details["stderr"] = Trim(doctor.Stderr);
            AddResolutionDetails(findings[^1], doctor);
            return findings;
        }

        if (!doctor.Success)
        {
            findings.Add(Finding(FindingSeverity.Error, "lark.doctor_failed", "lark-cli doctor did not pass. Fix local app/auth configuration before using the Lark provider."));
            findings[^1].Details["exitCode"] = doctor.ExitCode.ToString();
            findings[^1].Details["stderr"] = Trim(doctor.Stderr);
            AddResolutionDetails(findings[^1], doctor);
            return findings;
        }

        findings.Add(Finding(FindingSeverity.Info, "lark.doctor_ok", "lark-cli doctor passed."));
        AddResolutionDetails(findings[^1], doctor);
        AddUpdateNotice(findings, doctor.Stdout);

        var version = await gateway.RunAsync(new[] { "--version" }, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (version.Success)
        {
            findings.Add(Finding(FindingSeverity.Info, "lark.cli_found", "lark-cli is available."));
            findings[^1].Details["version"] = Trim(version.Stdout);
            AddResolutionDetails(findings[^1], version);
        }

        var auth = await gateway.RunAsync(new[] { "auth", "status" }, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!auth.Success)
        {
            findings.Add(Finding(FindingSeverity.Warning, "lark.auth_status_failed", "Could not confirm user OAuth status. User-owned docs or sheets may require lark-cli auth login."));
            findings[^1].Details["exitCode"] = auth.ExitCode.ToString();
            findings[^1].Details["stderr"] = Trim(auth.Stderr);
            AddResolutionDetails(findings[^1], auth);
        }
        else
        {
            findings.Add(Finding(FindingSeverity.Info, "lark.auth_status_ok", "lark-cli auth status is available."));
            AddResolutionDetails(findings[^1], auth);
        }

        return findings;
    }

    public async Task<IList<ProviderRootCandidate>> DiscoverRootsAsync(ProviderContext context, string query, CancellationToken cancellationToken)
    {
        var candidates = new List<ProviderRootCandidate>();
        if (context.Settings.TryGetValue("rootUrl", out var rootUrl) && !string.IsNullOrWhiteSpace(rootUrl))
        {
            candidates.Add(new ProviderRootCandidate
            {
                ProviderId = Id,
                Title = "Configured root",
                ObjectType = context.Settings.TryGetValue("rootObjectType", out var type) ? type : "unknown",
                Url = rootUrl,
                Reason = "Already present in local config. Confirm it before treating it as the source of truth."
            });
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return candidates;
        }

        var gateway = CreateGateway(context);
        var doctor = await gateway.RunAsync(new[] { "doctor" }, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!doctor.Success)
        {
            candidates.Add(new ProviderRootCandidate
            {
                ProviderId = Id,
                Title = "Discovery unavailable",
                ObjectType = "diagnostic",
                Reason = "lark-cli doctor failed. Run tool doctor for human-readable repair steps."
            });
            return candidates;
        }

        var result = await gateway.RunAsync(new[] { "docs", "+search", "--query", query, "--format", "json" }, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            candidates.Add(new ProviderRootCandidate
            {
                ProviderId = Id,
                Title = "Search failed",
                ObjectType = "diagnostic",
                Reason = "lark-cli docs search failed. Check auth scopes for docs/wiki/sheets."
            });
            candidates[^1].Details["stderr"] = Trim(result.Stderr);
            return candidates;
        }

        foreach (var candidate in ParseSearchCandidates(result.Stdout))
        {
            candidates.Add(candidate);
        }

        return candidates;
    }

    public async Task<ProviderExportResult> ExportAsync(ProviderContext context, ProviderExportRequest request, CancellationToken cancellationToken)
    {
        var result = new ProviderExportResult();
        Directory.CreateDirectory(request.CacheDirectory);

        var gateway = CreateGateway(context);
        var doctor = await gateway.RunAsync(new[] { "doctor" }, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!doctor.Success)
        {
            result.Findings.Add(Finding(FindingSeverity.Error, "lark.doctor_failed", "lark-cli doctor did not pass. Fix local setup before sync."));
            result.Findings[^1].Details["stderr"] = Trim(doctor.Stderr);
            return result;
        }

        var source = FirstNonEmpty(request.SpreadsheetTokenOrUrl, request.RootTokenOrUrl);
        if (string.IsNullOrWhiteSpace(source))
        {
            result.Findings.Add(Finding(FindingSeverity.Error, "lark.source_missing", "No spreadsheet URL or token is configured for this table."));
            return result;
        }

        var safeName = MakeSafeFileName(FirstNonEmpty(request.TableId, request.SheetId, "lark-workbook"));
        var xlsxPath = Path.Combine(request.CacheDirectory, safeName + ".xlsx");
        var exported = await TryExportXlsxAsync(gateway, context, source, xlsxPath, cancellationToken).ConfigureAwait(false);
        if (exported.Success)
        {
            result.CachePath = xlsxPath;
        }
        else
        {
            result.Findings.Add(Finding(FindingSeverity.Warning, "lark.export_xlsx_failed", "Could not export an xlsx cache. The provider will still try to read semantic values."));
            result.Findings[^1].Details["stderr"] = Trim(exported.Stderr);
        }

        var read = await TryReadValuesAsync(gateway, context, source, request, cancellationToken).ConfigureAwait(false);
        if (!read.Success)
        {
            result.Findings.Add(Finding(FindingSeverity.Error, "lark.read_failed", "Could not read sheet values for semantic hashing. Check sheet id, range, and scopes."));
            result.Findings[^1].Details["stderr"] = Trim(read.Stderr);
            if (File.Exists(xlsxPath))
            {
                result.SemanticHash = "file:" + ComputeFileHash(xlsxPath);
            }

            return result;
        }

        result.Workbook = BuildWorkbookFromReadJson(read.Stdout, request, source);
        result.Workbook.ProviderId = Id;
        result.Workbook.SourceId = source;
        result.Workbook.Revision = DateTimeOffset.UtcNow.ToString("O");
        result.SemanticHash = SemanticHasher.ComputeHash(result.Workbook);
        result.ProviderRevision = result.Workbook.Revision;

        if (string.IsNullOrWhiteSpace(result.CachePath))
        {
            var semanticPath = Path.Combine(request.CacheDirectory, safeName + ".semantic.json");
            await File.WriteAllTextAsync(semanticPath, read.Stdout, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            result.CachePath = semanticPath;
        }

        return result;
    }

    private static async Task<LarkCliResult> TryExportXlsxAsync(LarkCliGateway gateway, ProviderContext context, string source, string outputPath, CancellationToken cancellationToken)
    {
        var args = new List<string> { "sheets", "+export" };
        AddSourceArgument(args, source);
        args.Add("--output");
        args.Add(outputPath);
        return await gateway.RunAsync(args, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<LarkCliResult> TryReadValuesAsync(LarkCliGateway gateway, ProviderContext context, string source, ProviderExportRequest request, CancellationToken cancellationToken)
    {
        var args = new List<string> { "sheets", "+read" };
        AddSourceArgument(args, source);

        if (!string.IsNullOrWhiteSpace(request.SheetId))
        {
            args.Add("--sheet-id");
            args.Add(request.SheetId);
        }

        if (!string.IsNullOrWhiteSpace(request.Range))
        {
            args.Add("--range");
            args.Add(request.Range);
        }

        args.Add("--format");
        args.Add("json");
        return await gateway.RunAsync(args, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
    }

    private static void AddSourceArgument(ICollection<string> args, string source)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--url");
            args.Add(source);
            return;
        }

        args.Add("--spreadsheet-token");
        args.Add(source);
    }

    private static WorkbookDocument BuildWorkbookFromReadJson(string json, ProviderExportRequest request, string source)
    {
        var workbook = new WorkbookDocument
        {
            ProviderId = "lark",
            SourceId = source,
            SourceTitle = FirstNonEmpty(request.TableId, request.SheetId, "Lark sheet")
        };

        var sheet = new SheetDocument
        {
            Id = request.SheetId,
            Name = FirstNonEmpty(request.TableId, request.SheetId, "Sheet1")
        };
        workbook.Sheets.Add(sheet);

        using var document = JsonDocument.Parse(json);
        if (!TryFindMatrix(document.RootElement, out var matrix) || matrix.Count == 0)
        {
            return workbook;
        }

        var headers = matrix[0].Select((value, index) => new
        {
            Index = index,
            DisplayName = value,
            Key = MakeColumnKey(value, index)
        }).ToList();

        foreach (var header in headers)
        {
            sheet.Columns.Add(new ColumnDefinition
            {
                Key = header.Key,
                DisplayName = string.IsNullOrWhiteSpace(header.DisplayName) ? header.Key : header.DisplayName,
                ValueKind = "string",
                SourceColumn = ToColumnName(header.Index)
            });
        }

        var idColumn = headers.FirstOrDefault(h => string.Equals(h.Key, "id", StringComparison.OrdinalIgnoreCase) || string.Equals(h.Key, "key", StringComparison.OrdinalIgnoreCase)) ?? headers.First();
        for (var i = 1; i < matrix.Count; i++)
        {
            var values = matrix[i];
            var row = new RowDocument
            {
                SourceIndex = i + 1,
                StableId = idColumn.Index < values.Count && !string.IsNullOrWhiteSpace(values[idColumn.Index])
                    ? values[idColumn.Index]
                    : "row-" + i.ToString("0000")
            };

            foreach (var header in headers)
            {
                var text = header.Index < values.Count ? values[header.Index] : "";
                row.Cells[header.Key] = new CellValue
                {
                    RawText = text,
                    NormalizedText = NormalizeCellText(text),
                    ValueKind = "string"
                };
            }

            sheet.Rows.Add(row);
        }

        return workbook;
    }

    private static IEnumerable<ProviderRootCandidate> ParseSearchCandidates(string json)
    {
        var candidates = new List<ProviderRootCandidate>();
        try
        {
            using var document = JsonDocument.Parse(json);
            WalkCandidates(document.RootElement, candidates);
        }
        catch (JsonException)
        {
            candidates.Add(new ProviderRootCandidate
            {
                ProviderId = "lark",
                Title = "Raw search output",
                ObjectType = "unknown",
                Reason = "lark-cli returned non-JSON output. Inspect details before selecting a root."
            });
            candidates[^1].Details["raw"] = Trim(json);
        }

        return candidates;
    }

    private static void WalkCandidates(JsonElement element, ICollection<ProviderRootCandidate> candidates)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                WalkCandidates(child, candidates);
            }
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var title = GetString(element, "title", "name");
        var token = GetString(element, "token", "obj_token", "file_token", "node_token");
        var url = GetString(element, "url", "link");
        var type = GetString(element, "doc_types", "obj_type", "type");
        if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(url) || !string.IsNullOrWhiteSpace(token))
        {
            candidates.Add(new ProviderRootCandidate
            {
                ProviderId = "lark",
                Title = FirstNonEmpty(title, token, url, "Lark candidate"),
                ObjectType = FirstNonEmpty(type, "unknown"),
                TokenOrId = token,
                Url = url,
                Reason = "Search result. Review the title and URL before choosing it as a root."
            });
        }

        foreach (var property in element.EnumerateObject())
        {
            WalkCandidates(property.Value, candidates);
        }
    }

    private static bool TryFindMatrix(JsonElement element, out List<List<string>> matrix)
    {
        matrix = new List<List<string>>();
        if (element.ValueKind == JsonValueKind.Array)
        {
            var rows = new List<List<string>>();
            foreach (var rowElement in element.EnumerateArray())
            {
                if (rowElement.ValueKind != JsonValueKind.Array)
                {
                    rows.Clear();
                    break;
                }

                rows.Add(rowElement.EnumerateArray().Select(CellToText).ToList());
            }

            if (rows.Count > 0)
            {
                matrix = rows;
                return true;
            }
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "values", StringComparison.OrdinalIgnoreCase) && TryFindMatrix(property.Value, out matrix))
                {
                    return true;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindMatrix(property.Value, out matrix))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string CellToText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText()
        };
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
            }
        }

        return "";
    }

    private static string MakeColumnKey(string displayName, int index)
    {
        var key = Regex.Replace(displayName ?? "", "[^A-Za-z0-9_]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "column_" + (index + 1);
        }

        if (char.IsDigit(key[0]))
        {
            key = "_" + key;
        }

        return key.ToLowerInvariant();
    }

    private static string ToColumnName(int zeroBasedIndex)
    {
        var dividend = zeroBasedIndex + 1;
        var columnName = "";
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }

    private static string NormalizeCellText(string value)
    {
        return (value ?? "").Trim();
    }

    private static ProviderDoctorFinding Finding(FindingSeverity severity, string code, string message)
    {
        return new ProviderDoctorFinding
        {
            Severity = severity,
            Code = code,
            Message = message
        };
    }

    private static void AddResolutionDetails(ProviderDoctorFinding finding, LarkCliResult result)
    {
        finding.Details["resolvedSource"] = result.ResolvedCommand.Source;
        finding.Details["resolvedPath"] = result.ResolvedCommand.DisplayPath;
        if (!string.Equals(result.ResolvedCommand.FileName, result.ResolvedCommand.DisplayPath, StringComparison.OrdinalIgnoreCase))
        {
            finding.Details["launcher"] = result.ResolvedCommand.FileName;
        }
    }

    private static void AddUpdateNotice(ICollection<ProviderDoctorFinding> findings, string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (!document.RootElement.TryGetProperty("_notice", out var notice) ||
                !notice.TryGetProperty("update", out var update))
            {
                return;
            }

            var finding = Finding(FindingSeverity.Warning, "lark.update_available", "A newer lark-cli version is available. Run lark-cli update when you are ready, then restart the agent before relying on updated skills.");
            finding.Details["current"] = GetString(update, "current");
            finding.Details["latest"] = GetString(update, "latest");
            finding.Details["command"] = GetString(update, "command");
            findings.Add(finding);
        }
        catch (JsonException)
        {
        }
    }

    private static LarkCliGateway CreateGateway(ProviderContext context)
    {
        context.Settings.TryGetValue("larkCliPath", out var executable);
        return new LarkCliGateway(executable);
    }

    private static string FirstNonEmpty(params string?[] values)
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

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();
        foreach (var c in value)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        return builder.Length == 0 ? "workbook" : builder.ToString();
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string Trim(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        value = value.Trim();
        return value.Length <= 4000 ? value : value.Substring(0, 4000);
    }
}
