using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ConfigSheetForge.Core;

namespace ConfigSheetForge.Providers.Lark;

public sealed class LarkCliWorkbookProvider : IWorkbookProvider
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

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
                Reason = StrictBotFailureMessage(context, result, "lark-cli docs search failed. Check auth scopes for docs/wiki/sheets.")
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
            result.Findings.Add(Finding(FindingSeverity.Warning, "lark.export_xlsx_failed", StrictBotFailureMessage(context, exported, "Could not export an xlsx cache. The provider will still try to read semantic values.")));
            result.Findings[^1].Details["stderr"] = Trim(exported.Stderr);
        }

        var read = await TryReadValuesAsync(gateway, context, source, request, File.Exists(xlsxPath) ? xlsxPath : "", cancellationToken).ConfigureAwait(false);
        foreach (var finding in read.Findings)
        {
            result.Findings.Add(finding);
        }

        if (!read.Success)
        {
            result.Findings.Add(BuildReadFailureFinding(context, request, source, read));
            if (File.Exists(xlsxPath))
            {
                result.SemanticHash = "file:" + ComputeFileHash(xlsxPath);
            }

            return result;
        }

        var import = BuildWorkbookFromReadJson(CombinedOutput(read.Result), request, source);
        result.Workbook = import.Workbook;
        result.Workbook.ProviderId = Id;
        result.Workbook.SourceId = source;
        result.Workbook.Revision = DateTimeOffset.UtcNow.ToString("O");
        StampReadDiagnostics(result.Workbook, read);
        result.Findings.Add(BuildReadSuccessFinding(request, source, read, result.Workbook));
        foreach (var finding in import.Report.Findings)
        {
            result.Findings.Add(ToProviderFinding(finding));
        }

        result.SemanticHash = SemanticHasher.ComputeHash(result.Workbook);
        result.ProviderRevision = result.Workbook.Revision;

        if (string.IsNullOrWhiteSpace(result.CachePath))
        {
            var semanticPath = Path.Combine(request.CacheDirectory, safeName + ".semantic.json");
            await File.WriteAllTextAsync(semanticPath, CombinedOutput(read.Result), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
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

    private static async Task<LarkReadAttempt> TryReadValuesAsync(LarkCliGateway gateway, ProviderContext context, string source, ProviderExportRequest request, string exportedXlsxPath, CancellationToken cancellationToken)
    {
        var hasXlsxInfo = TryReadXlsxDimensionInfo(exportedXlsxPath, out var xlsxInfo);
        var xlsxRange = hasXlsxInfo && !string.IsNullOrWhiteSpace(request.SheetId)
            ? BuildA1Range(request.SheetId, xlsxInfo.Rows, xlsxInfo.Columns)
            : "";
        var explicitRange = FirstNonEmpty(
            request.Range,
            xlsxRange,
            await TryBuildRangeFromSheetInfoAsync(gateway, context, source, request, cancellationToken).ConfigureAwait(false));
        var initial = await RunReadAsync(gateway, context, source, request, explicitRange, cancellationToken).ConfigureAwait(false);
        var attempt = new LarkReadAttempt
        {
            Result = initial,
            AttemptedRange = explicitRange,
            FinalRange = explicitRange,
            XlsxRows = hasXlsxInfo ? xlsxInfo.Rows : 0,
            XlsxColumns = hasXlsxInfo ? xlsxInfo.Columns : 0,
            XlsxDimensionRows = hasXlsxInfo ? xlsxInfo.DimensionRows : 0,
            XlsxDimensionColumns = hasXlsxInfo ? xlsxInfo.DimensionColumns : 0,
            XlsxCellRows = hasXlsxInfo ? xlsxInfo.CellRows : 0,
            XlsxCellColumns = hasXlsxInfo ? xlsxInfo.CellColumns : 0
        };

        if (initial.Success)
        {
            return attempt;
        }

        if (!LooksLikeWrongStartRange(initial))
        {
            return attempt;
        }

        var retryRange = FirstNonEmpty(
            explicitRange,
            await TryBuildRangeFromSheetInfoAsync(gateway, context, source, request, cancellationToken).ConfigureAwait(false),
            BuildDefaultReadRange(request));
        if (string.IsNullOrWhiteSpace(retryRange))
        {
            return attempt;
        }

        if (string.Equals(retryRange, explicitRange, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(explicitRange))
        {
            return attempt;
        }

        attempt.RetryRange = retryRange;
        var retry = await RunReadAsync(gateway, context, source, request, retryRange, cancellationToken).ConfigureAwait(false);
        attempt.Result = retry;
        attempt.FinalRange = retryRange;
        if (retry.Success)
        {
            var finding = Finding(FindingSeverity.Info, "lark.read_retry_success", "首次读取在线 Sheet 返回 wrong startRange，已自动改用显式 A1 范围重试成功。");
            AddReadAttemptDetails(finding, request, source, attempt, initial);
            attempt.Findings.Add(finding);
        }

        return attempt;
    }

    private static async Task<LarkCliResult> RunReadAsync(LarkCliGateway gateway, ProviderContext context, string source, ProviderExportRequest request, string range, CancellationToken cancellationToken)
    {
        var args = new List<string> { "sheets", "+read" };
        AddSourceArgument(args, source);

        if (!string.IsNullOrWhiteSpace(request.SheetId))
        {
            args.Add("--sheet-id");
            args.Add(request.SheetId);
        }

        if (!string.IsNullOrWhiteSpace(range))
        {
            args.Add("--range");
            args.Add(range);
        }

        return await RunWithIdentityFallbackAsync(gateway, context, args, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> TryBuildRangeFromSheetInfoAsync(LarkCliGateway gateway, ProviderContext context, string source, ProviderExportRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SheetId))
        {
            return "";
        }

        var args = new List<string> { "sheets", "+info" };
        AddSourceArgument(args, source);
        var info = await RunWithIdentityFallbackAsync(gateway, context, args, cancellationToken).ConfigureAwait(false);
        if (!info.Success || !TryParseJsonDocument(CombinedOutput(info), out var document))
        {
            return "";
        }

        using (document)
        {
            if (TryFindGridDimensions(document.RootElement, request.SheetId, out var rows, out var columns))
            {
                return BuildA1Range(request.SheetId, rows, columns);
            }
        }

        return "";
    }

    private static string TryBuildRangeFromXlsx(string exportedXlsxPath, ProviderExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(exportedXlsxPath) ||
            !File.Exists(exportedXlsxPath) ||
            string.IsNullOrWhiteSpace(request.SheetId))
        {
            return "";
        }

        if (!TryReadXlsxDimensionInfo(exportedXlsxPath, out var info))
        {
            return "";
        }

        return BuildA1Range(request.SheetId, info.Rows, info.Columns);
    }

    private static bool TryReadXlsxDimensions(string path, out int rows, out int columns)
    {
        if (TryReadXlsxDimensionInfo(path, out var info))
        {
            rows = info.Rows;
            columns = info.Columns;
            return true;
        }

        rows = 0;
        columns = 0;
        return false;
    }

    private static bool TryReadXlsxDimensionInfo(string path, out XlsxDimensionInfo info)
    {
        info = new XlsxDimensionInfo();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var rows = 0;
        var columns = 0;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var sheetPath = ResolveFirstWorksheetPath(archive);
            var entry = archive.GetEntry(sheetPath);
            if (entry == null)
            {
                return false;
            }

            var document = ReadXml(entry);
            var root = document.Root;
            if (root == null)
            {
                return false;
            }

            var dimension = root.Element(SpreadsheetNs + "dimension");
            var reference = (string?)dimension?.Attribute("ref") ?? "";
            TryParseDimension(reference, out var dimensionRows, out var dimensionColumns);
            var cellRows = 0;
            var cellColumns = 0;

            foreach (var cell in root.Descendants(SpreadsheetNs + "c"))
            {
                var position = ParseA1((string?)cell.Attribute("r") ?? "");
                if (position.Row > cellRows)
                {
                    cellRows = position.Row;
                }

                if (position.Column > cellColumns)
                {
                    cellColumns = position.Column;
                }
            }

            rows = Math.Max(dimensionRows, cellRows);
            columns = Math.Max(dimensionColumns, cellColumns);
            if (rows <= 0 || columns <= 0)
            {
                return false;
            }

            info = new XlsxDimensionInfo
            {
                Rows = rows,
                Columns = columns,
                DimensionRows = dimensionRows,
                DimensionColumns = dimensionColumns,
                CellRows = cellRows,
                CellColumns = cellColumns,
                DimensionReference = reference
            };
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
        {
            return false;
        }
    }

    private static string ResolveFirstWorksheetPath(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry == null)
        {
            return "xl/worksheets/sheet1.xml";
        }

        var relationships = ReadRelationships(archive, "xl/_rels/workbook.xml.rels");
        var workbook = ReadXml(workbookEntry);
        var sheet = workbook.Descendants(SpreadsheetNs + "sheet").FirstOrDefault();
        if (sheet == null)
        {
            return "xl/worksheets/sheet1.xml";
        }

        var relationId = (string?)sheet.Attribute(RelationshipNs + "id") ?? "";
        return relationships.TryGetValue(relationId, out var target)
            ? NormalizeWorkbookTarget(target)
            : "xl/worksheets/sheet1.xml";
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
        foreach (var relationship in document.Descendants(PackageRelationshipNs + "Relationship"))
        {
            var id = (string?)relationship.Attribute("Id") ?? "";
            var target = (string?)relationship.Attribute("Target") ?? "";
            if (!string.IsNullOrWhiteSpace(id))
            {
                result[id] = target;
            }
        }

        return result;
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
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

    private static bool TryParseDimension(string reference, out int rows, out int columns)
    {
        rows = 0;
        columns = 0;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var end = reference.Contains(':') ? reference.Split(':').Last() : reference;
        var position = ParseA1(end);
        rows = position.Row;
        columns = position.Column;
        return rows > 0 && columns > 0;
    }

    private static CellPosition ParseA1(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new CellPosition(0, 0);
        }

        var column = 0;
        var index = 0;
        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            column = column * 26 + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }

        var rowText = reference.Substring(index);
        if (column == 0 || !int.TryParse(rowText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) || row <= 0)
        {
            return new CellPosition(0, 0);
        }

        return new CellPosition(row, column);
    }

    private static bool TryFindGridDimensions(JsonElement root, string sheetId, out int rows, out int columns)
    {
        var candidates = new List<(bool Match, int Rows, int Columns)>();
        CollectGridDimensionCandidates(root, sheetId, candidates);
        var match = candidates.FirstOrDefault(c => c.Match && c.Rows > 0 && c.Columns > 0);
        if (match.Rows <= 0 || match.Columns <= 0)
        {
            match = candidates.FirstOrDefault(c => c.Rows > 0 && c.Columns > 0);
        }

        rows = match.Rows;
        columns = match.Columns;
        return rows > 0 && columns > 0;
    }

    private static void CollectGridDimensionCandidates(JsonElement element, string sheetId, IList<(bool Match, int Rows, int Columns)> candidates)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectGridDimensionCandidates(child, sheetId, candidates);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var id = GetString(element, "sheet_id", "sheetId", "id");
        var rows = GetInt(element, "row_count", "rowCount", "rows", "row");
        var columns = GetInt(element, "column_count", "columnCount", "col_count", "colCount", "columns", "cols", "column");
        if (rows <= 0 || columns <= 0)
        {
            if (TryGetPropertyIgnoreCase(element, "grid_properties", out var grid) ||
                TryGetPropertyIgnoreCase(element, "gridProperties", out grid))
            {
                rows = rows <= 0 ? GetInt(grid, "row_count", "rowCount", "rows", "row") : rows;
                columns = columns <= 0 ? GetInt(grid, "column_count", "columnCount", "col_count", "colCount", "columns", "cols", "column") : columns;
            }
        }

        if (rows > 0 && columns > 0)
        {
            candidates.Add((string.IsNullOrWhiteSpace(sheetId) || string.Equals(id, sheetId, StringComparison.OrdinalIgnoreCase), rows, columns));
        }

        foreach (var property in element.EnumerateObject())
        {
            CollectGridDimensionCandidates(property.Value, sheetId, candidates);
        }
    }

    private static int GetInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return 0;
    }

    private static string BuildDefaultReadRange(ProviderExportRequest request)
    {
        return string.IsNullOrWhiteSpace(request.SheetId)
            ? ""
            : BuildA1Range(request.SheetId, 200, 20);
    }

    private static string BuildA1Range(string sheetId, int rows, int columns)
    {
        rows = Math.Max(1, rows);
        columns = Math.Max(1, columns);
        return sheetId + "!A1:" + ColumnName(columns) + rows.ToString(CultureInfo.InvariantCulture);
    }

    private static string ColumnName(int oneBasedColumn)
    {
        var value = Math.Max(1, oneBasedColumn);
        var builder = new StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + (value % 26)));
            value /= 26;
        }

        return builder.ToString();
    }

    private static bool LooksLikeWrongStartRange(LarkCliResult result)
    {
        var text = (result.Stdout + "\n" + result.Stderr).ToLowerInvariant();
        return text.Contains("90202") ||
               text.Contains("wrong startrange") ||
               text.Contains("start_range") ||
               text.Contains("startrange");
    }

    private static ProviderDoctorFinding BuildReadFailureFinding(ProviderContext context, ProviderExportRequest request, string source, LarkReadAttempt read)
    {
        var finding = Finding(FindingSeverity.Error, "lark.read_failed", StrictBotFailureMessage(context, read.Result, "无法读取在线 Sheet 的单元格数据。工具已避免把 no-range 问题误判成权限问题；请先查看 attemptedRange、retryRange 和 larkError。"));
        AddReadAttemptDetails(finding, request, source, read, read.Result);
        finding.Details["stderr"] = Trim(read.Result.Stderr);
        return finding;
    }

    private static ProviderDoctorFinding BuildReadSuccessFinding(ProviderExportRequest request, string source, LarkReadAttempt read, WorkbookDocument workbook)
    {
        var finding = Finding(FindingSeverity.Info, "lark.read_range", string.IsNullOrWhiteSpace(read.FinalRange)
            ? "已读取在线 Sheet；本次没有显式 A1 范围。"
            : "已使用显式 A1 范围读取在线 Sheet。");
        AddReadAttemptDetails(finding, request, source, read, read.Result);
        var shape = FirstSheetShape(workbook);
        finding.Details["onlineRows"] = shape.Rows.ToString(CultureInfo.InvariantCulture);
        finding.Details["onlineColumns"] = shape.Columns.ToString(CultureInfo.InvariantCulture);
        finding.Details["xlsxRows"] = read.XlsxRows.ToString(CultureInfo.InvariantCulture);
        finding.Details["xlsxColumns"] = read.XlsxColumns.ToString(CultureInfo.InvariantCulture);
        finding.Details["xlsxDimensionRows"] = read.XlsxDimensionRows.ToString(CultureInfo.InvariantCulture);
        finding.Details["xlsxDimensionColumns"] = read.XlsxDimensionColumns.ToString(CultureInfo.InvariantCulture);
        finding.Details["xlsxCellRows"] = read.XlsxCellRows.ToString(CultureInfo.InvariantCulture);
        finding.Details["xlsxCellColumns"] = read.XlsxCellColumns.ToString(CultureInfo.InvariantCulture);
        return finding;
    }

    private static void AddReadAttemptDetails(ProviderDoctorFinding finding, ProviderExportRequest request, string source, LarkReadAttempt read, LarkCliResult larkResult)
    {
        finding.Details["tableId"] = request.TableId;
        finding.Details["spreadsheetTokenMasked"] = Mask(source);
        finding.Details["sheetId"] = request.SheetId;
        finding.Details["attemptedRange"] = FirstNonEmpty(read.AttemptedRange, "(none)");
        finding.Details["retryRange"] = FirstNonEmpty(read.RetryRange, "(none)");
        finding.Details["finalRange"] = FirstNonEmpty(read.FinalRange, "(none)");
        finding.Details["retrySucceeded"] = (!string.IsNullOrWhiteSpace(read.RetryRange) && read.Result.Success).ToString().ToLowerInvariant();
        finding.Details["larkErrorCode"] = ExtractLarkErrorCode(larkResult);
        finding.Details["larkErrorMessage"] = ExtractLarkErrorMessage(larkResult);
    }

    private static void StampReadDiagnostics(WorkbookDocument workbook, LarkReadAttempt read)
    {
        if (workbook == null)
        {
            return;
        }

        workbook.Metadata["larkAttemptedRange"] = FirstNonEmpty(read.AttemptedRange, "(none)");
        workbook.Metadata["larkRetryRange"] = FirstNonEmpty(read.RetryRange, "(none)");
        workbook.Metadata["larkFinalRange"] = FirstNonEmpty(read.FinalRange, "(none)");
        workbook.Metadata["xlsxRows"] = read.XlsxRows.ToString(CultureInfo.InvariantCulture);
        workbook.Metadata["xlsxColumns"] = read.XlsxColumns.ToString(CultureInfo.InvariantCulture);
        workbook.Metadata["xlsxDimensionRows"] = read.XlsxDimensionRows.ToString(CultureInfo.InvariantCulture);
        workbook.Metadata["xlsxDimensionColumns"] = read.XlsxDimensionColumns.ToString(CultureInfo.InvariantCulture);
        workbook.Metadata["xlsxCellRows"] = read.XlsxCellRows.ToString(CultureInfo.InvariantCulture);
        workbook.Metadata["xlsxCellColumns"] = read.XlsxCellColumns.ToString(CultureInfo.InvariantCulture);
        foreach (var sheet in workbook.Sheets)
        {
            sheet.Metadata["larkFinalRange"] = FirstNonEmpty(read.FinalRange, "(none)");
        }
    }

    private static (int Rows, int Columns) FirstSheetShape(WorkbookDocument workbook)
    {
        var sheet = workbook != null ? workbook.Sheets.FirstOrDefault() : null;
        return sheet == null ? (0, 0) : (sheet.Rows.Count, sheet.Columns.Count);
    }

    private static string ExtractLarkErrorCode(LarkCliResult result)
    {
        var text = CombinedOutput(result);
        if (TryParseJsonDocument(text, out var document))
        {
            using (document)
            {
                var code = FindStringDeep(document.RootElement, "code", "error_code", "errCode");
                if (!string.IsNullOrWhiteSpace(code))
                {
                    return code;
                }
            }
        }

        var raw = result.Stdout + "\n" + result.Stderr;
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"\b\d{4,}\b");
        return match.Success ? match.Value : "";
    }

    private static string ExtractLarkErrorMessage(LarkCliResult result)
    {
        var text = CombinedOutput(result);
        if (TryParseJsonDocument(text, out var document))
        {
            using (document)
            {
                var message = FindStringDeep(document.RootElement, "msg", "message", "error", "errorMessage");
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }

        return Trim(result.Stderr + "\n" + result.Stdout);
    }

    private static string FindStringDeep(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (TryGetPropertyIgnoreCase(element, name, out var property))
                {
                    return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var value = FindStringDeep(property.Value, names);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var value = FindStringDeep(child, names);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return "";
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
        if (first.Success || requested != "bot" || !IsUserFallbackAllowed(context))
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

    private static string StrictBotFailureMessage(ProviderContext context, LarkCliResult result, string fallback)
    {
        if (GetIdentity(context) == "bot" && !IsUserFallbackAllowed(context) && LooksLikePermissionFailure(result))
        {
            return fallback + " 当前是 bot 严格模式，不会静默切换到 user。请给应用/bot 补充缺失 scope 或资源权限；只有显式传入 --allow-user-fallback 时才会尝试 user fallback。";
        }

        return fallback;
    }

    private static bool IsUserFallbackAllowed(ProviderContext context)
    {
        if (context.Settings.TryGetValue("larkAllowUserFallback", out var value))
        {
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        if (context.Settings.TryGetValue("allowUserFallback", out value))
        {
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        return false;
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

    private static string Mask(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 10
            ? "********"
            : trimmed.Substring(0, 5) + "..." + trimmed.Substring(trimmed.Length - 5);
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

    private sealed class LarkReadAttempt
    {
        public LarkCliResult Result { get; set; } = new LarkCliResult(-1, "", "", "", new LarkCliResolvedCommand("", Array.Empty<string>(), "", ""));
        public string AttemptedRange { get; set; } = "";
        public string RetryRange { get; set; } = "";
        public string FinalRange { get; set; } = "";
        public int XlsxRows { get; set; }
        public int XlsxColumns { get; set; }
        public int XlsxDimensionRows { get; set; }
        public int XlsxDimensionColumns { get; set; }
        public int XlsxCellRows { get; set; }
        public int XlsxCellColumns { get; set; }
        public List<ProviderDoctorFinding> Findings { get; } = new List<ProviderDoctorFinding>();
        public bool Success => Result.Success;
    }

    private sealed class XlsxDimensionInfo
    {
        public int Rows { get; set; }
        public int Columns { get; set; }
        public int DimensionRows { get; set; }
        public int DimensionColumns { get; set; }
        public int CellRows { get; set; }
        public int CellColumns { get; set; }
        public string DimensionReference { get; set; } = "";
    }

    private readonly struct CellPosition
    {
        public CellPosition(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public int Row { get; }
        public int Column { get; }
    }
}
