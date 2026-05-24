using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        var result = await RunWithIdentityFallbackAsync(gateway, context, new[] { "docs", "+search", "--query", query, "--format", "json" }, cancellationToken).ConfigureAwait(false);
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

        foreach (var candidate in ParseSearchCandidates(CombinedOutput(result)))
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

        var import = BuildWorkbookFromReadJson(CombinedOutput(read), request, source);
        result.Workbook = import.Workbook;
        result.Workbook.ProviderId = Id;
        result.Workbook.SourceId = source;
        result.Workbook.Revision = DateTimeOffset.UtcNow.ToString("O");
        foreach (var finding in import.Report.Findings)
        {
            result.Findings.Add(ToProviderFinding(finding));
        }

        result.SemanticHash = SemanticHasher.ComputeHash(result.Workbook);
        result.ProviderRevision = result.Workbook.Revision;

        if (string.IsNullOrWhiteSpace(result.CachePath))
        {
            var semanticPath = Path.Combine(request.CacheDirectory, safeName + ".semantic.json");
            await File.WriteAllTextAsync(semanticPath, CombinedOutput(read), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            result.CachePath = semanticPath;
        }

        return result;
    }

    private static async Task<LarkCliResult> TryExportXlsxAsync(LarkCliGateway gateway, ProviderContext context, string source, string outputPath, CancellationToken cancellationToken)
    {
        var args = new List<string> { "sheets", "+export" };
        AddSourceArgument(args, source);
        args.Add("--file-extension");
        args.Add("xlsx");
        args.Add("--output-path");
        args.Add(ToCliOutputPath(context.WorkspaceRoot, outputPath));
        return await RunWithIdentityFallbackAsync(gateway, context, args, cancellationToken).ConfigureAwait(false);
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

        return await RunWithIdentityFallbackAsync(gateway, context, args, cancellationToken).ConfigureAwait(false);
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

    private static MatrixWorkbookImportResult BuildWorkbookFromReadJson(string json, ProviderExportRequest request, string source)
    {
        if (!TryParseJsonDocument(json, out var document))
        {
            var empty = MatrixWorkbookImporter.Import(new List<IList<string>>(), new MatrixWorkbookImportOptions
            {
                ProviderId = "lark",
                SourceId = source,
                SourceTitle = FirstNonEmpty(request.TableId, request.SheetId, "Lark sheet"),
                SheetId = request.SheetId,
                SheetName = FirstNonEmpty(request.TableId, request.SheetId, "Sheet1")
            });
            empty.Report.Add(FindingSeverity.Error, "lark.read_json_invalid", "lark-cli 返回的表格数据不是可读取的 JSON。", "$.provider.lark.read");
            return empty;
        }

        using (document)
        {
            if (!TryFindMatrix(document.RootElement, out var matrix))
            {
                matrix = new List<List<string>>();
            }

            return MatrixWorkbookImporter.Import(matrix.Cast<IList<string>>().ToList(), new MatrixWorkbookImportOptions
            {
                ProviderId = "lark",
                SourceId = source,
                SourceTitle = FirstNonEmpty(request.TableId, request.SheetId, "Lark sheet"),
                SheetId = request.SheetId,
                SheetName = FirstNonEmpty(request.TableId, request.SheetId, "Sheet1"),
                FieldRow = request.FieldRow,
                TypeRow = request.TypeRow,
                DescriptionRow = request.DescriptionRow,
                DataStartRow = request.DataStartRow,
                TreatUnknownTypesAsEnum = request.TreatUnknownTypesAsEnum
            });
        }
    }

    private static IEnumerable<ProviderRootCandidate> ParseSearchCandidates(string json)
    {
        var candidates = new List<ProviderRootCandidate>();
        try
        {
            if (!TryParseJsonDocument(json, out var document))
            {
                throw new JsonException();
            }

            using (document)
            {
                WalkCandidates(document.RootElement, candidates);
            }
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

            var objectRows = TryObjectArrayToMatrix(element);
            if (objectRows.Count > 0)
            {
                matrix = objectRows;
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

    private static List<List<string>> TryObjectArrayToMatrix(JsonElement element)
    {
        var rows = new List<JsonElement>();
        var headers = new List<string>();
        foreach (var rowElement in element.EnumerateArray())
        {
            if (rowElement.ValueKind != JsonValueKind.Object)
            {
                return new List<List<string>>();
            }

            rows.Add(rowElement);
            foreach (var property in rowElement.EnumerateObject())
            {
                if (!headers.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    headers.Add(property.Name);
                }
            }
        }

        if (rows.Count == 0 || headers.Count == 0)
        {
            return new List<List<string>>();
        }

        var matrix = new List<List<string>> { headers.ToList() };
        foreach (var row in rows)
        {
            var values = new List<string>();
            foreach (var header in headers)
            {
                values.Add(TryGetPropertyIgnoreCase(row, header, out var property) ? CellToText(property) : "");
            }

            matrix.Add(values);
        }

        return matrix;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement row, string name, out JsonElement value)
    {
        if (row.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in row.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(property.Name, name))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
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

    private static ProviderDoctorFinding ToProviderFinding(ValidationFinding finding)
    {
        var providerFinding = Finding(finding.Severity, finding.Code, finding.Message);
        providerFinding.Details["location"] = finding.Location;
        foreach (var pair in finding.Details)
        {
            providerFinding.Details[pair.Key] = pair.Value;
        }

        return providerFinding;
    }

    private static async Task<LarkCliResult> RunWithIdentityFallbackAsync(LarkCliGateway gateway, ProviderContext context, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var requested = GetIdentity(context);
        if (requested == "default")
        {
            return await gateway.RunAsync(args, context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        }

        var first = await gateway.RunAsync(WithIdentity(args, requested), context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (first.Success || requested != "bot")
        {
            return first;
        }

        return await gateway.RunAsync(WithIdentity(args, "user"), context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
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

    private static string GetIdentity(ProviderContext context)
    {
        if (context.Settings.TryGetValue("larkCliIdentity", out var identity) && !string.IsNullOrWhiteSpace(identity))
        {
            identity = identity.Trim().ToLowerInvariant();
            if (identity == "bot" || identity == "user" || identity == "default")
            {
                return identity;
            }
        }

        return "bot";
    }

    private static IEnumerable<string> WithIdentity(IEnumerable<string> args, string identity)
    {
        foreach (var arg in args)
        {
            yield return arg;
        }

        yield return "--as";
        yield return identity;
    }

    private static bool LooksLikePermissionFailure(LarkCliResult result)
    {
        var text = (result.Stdout + "\n" + result.Stderr).ToLowerInvariant();
        return text.Contains("permission") ||
               text.Contains("missing_scope") ||
               text.Contains("forbidden") ||
               text.Contains("unauthorized") ||
               text.Contains("auth");
    }

    private static string ToCliOutputPath(string workspaceRoot, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Path.IsPathRooted(outputPath))
        {
            return outputPath;
        }

        var relative = Path.GetRelativePath(workspaceRoot, outputPath);
        return relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(outputPath) : relative;
    }

    private static string CombinedOutput(LarkCliResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Stderr))
        {
            return result.Stdout;
        }

        if (TryParseJsonDocument(result.Stdout, out var stdoutDoc))
        {
            stdoutDoc.Dispose();
            return result.Stdout;
        }

        if (TryParseJsonDocument(result.Stderr, out var stderrDoc))
        {
            stderrDoc.Dispose();
            return result.Stderr;
        }

        return result.Stdout + "\n" + result.Stderr;
    }

    private static bool TryParseJsonDocument(string text, out JsonDocument document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        foreach (var candidate in JsonCandidates(trimmed))
        {
            try
            {
                document = JsonDocument.Parse(candidate);
                return true;
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private static IEnumerable<string> JsonCandidates(string text)
    {
        yield return text;
        var objectStart = text.IndexOf('{');
        var objectEnd = text.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
        {
            yield return text.Substring(objectStart, objectEnd - objectStart + 1);
        }

        var arrayStart = text.IndexOf('[');
        var arrayEnd = text.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            yield return text.Substring(arrayStart, arrayEnd - arrayStart + 1);
        }
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
