using System.Text.Json;
using ConfigSheetForge.Core;
using ConfigSheetForge.Cli;
using ConfigSheetForge.Providers.Lark;

var tests = new List<(string Name, Func<Task> Body)>
{
    ("semantic hash is stable across row order", () => RunSync(SemanticHashIsStableAcrossRowOrder)),
    ("portable validator catches duplicate row ids", () => RunSync(ValidatorCatchesDuplicateRowIds)),
    ("three-way merge detects real conflicts", () => RunSync(MergeDetectsConflicts)),
    ("core model round-trips through json", () => RunSync(CoreModelRoundTripsThroughJson)),
    ("lark cli discovery resolves windows npm shim", () => RunSync(LarkCliDiscoveryResolvesWindowsNpmShim)),
    ("type row import normalizes semantic values", () => RunSync(TypeRowImportNormalizesSemanticValues)),
    ("enum option drift is reported", () => RunSync(EnumOptionDriftIsReported)),
    ("lark read parser accepts wrapped values", () => RunSync(LarkReadParserAcceptsWrappedValues)),
    ("lark read parser accepts record arrays", () => RunSync(LarkReadParserAcceptsRecordArrays)),
    ("lark read parser matches object keys case-insensitively", () => RunSync(LarkReadParserMatchesObjectKeysCaseInsensitively)),
    ("datetime normalization does not assume local timezone", () => RunSync(DateTimeNormalizationDoesNotAssumeLocalTimezone)),
    ("gate can print github annotations", GateCanPrintGitHubAnnotations)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Body();
        Console.WriteLine("[pass] " + test.Name);
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine("[fail] " + test.Name);
        Console.WriteLine(ex.Message);
    }
}

return failed == 0 ? 0 : 1;

static Task RunSync(Action action)
{
    action();
    return Task.CompletedTask;
}

static void SemanticHashIsStableAcrossRowOrder()
{
    var a = SampleWorkbook();
    var b = SampleWorkbook();
    b.Sheets[0].Rows.Reverse();

    AssertEqual(SemanticHasher.ComputeHash(a), SemanticHasher.ComputeHash(b), "Row order should not change the semantic hash.");
}

static void ValidatorCatchesDuplicateRowIds()
{
    var workbook = SampleWorkbook();
    workbook.Sheets[0].Rows[1].StableId = workbook.Sheets[0].Rows[0].StableId;

    var report = PortableSubsetValidator.Validate(workbook);
    AssertTrue(report.HasErrors, "Duplicate row ids must fail validation.");
    AssertTrue(report.Findings.Any(f => f.Code == "row.duplicate_id"), "Expected row.duplicate_id.");
}

static void MergeDetectsConflicts()
{
    var baseWorkbook = SampleWorkbook();
    var ours = SampleWorkbook();
    var theirs = SampleWorkbook();

    ours.Sheets[0].Rows[0].Cells["name"].RawText = "Bronze Sword";
    ours.Sheets[0].Rows[0].Cells["name"].NormalizedText = "Bronze Sword";
    theirs.Sheets[0].Rows[0].Cells["name"].RawText = "Iron Sword";
    theirs.Sheets[0].Rows[0].Cells["name"].NormalizedText = "Iron Sword";

    var report = ThreeWayMerger.Merge(baseWorkbook, ours, theirs);
    AssertTrue(report.HasConflicts, "Both sides changing a cell differently should conflict.");
}

static void CoreModelRoundTripsThroughJson()
{
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    var json = JsonSerializer.Serialize(SampleWorkbook(), options);
    var roundTrip = JsonSerializer.Deserialize<WorkbookDocument>(json, options);
    AssertTrue(roundTrip != null, "Workbook should deserialize.");
    AssertEqual("Items", roundTrip!.Sheets[0].Name, "Sheet name should survive JSON round-trip.");
}

static void LarkCliDiscoveryResolvesWindowsNpmShim()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-lark-discovery-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var shim = Path.Combine(temp, "lark-cli.cmd");
    File.WriteAllText(shim, "@echo off\r\nexit /b 0\r\n");

    var oldPath = Environment.GetEnvironmentVariable("PATH");
    var oldEnv = Environment.GetEnvironmentVariable("LARK_CLI_PATH");
    try
    {
        Environment.SetEnvironmentVariable("LARK_CLI_PATH", null);
        Environment.SetEnvironmentVariable("PATH", temp + Path.PathSeparator + oldPath);
        var resolved = LarkCliDiscovery.Resolve("lark-cli");
        AssertEqual(Path.GetFullPath(shim), Path.GetFullPath(resolved.FileName), "Discovery should resolve the .cmd shim on Windows.");
        AssertEqual("PATH", resolved.Source, "Discovery should report PATH as the source.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("PATH", oldPath);
        Environment.SetEnvironmentVariable("LARK_CLI_PATH", oldEnv);
        Directory.Delete(temp, recursive: true);
    }
}

static void TypeRowImportNormalizesSemanticValues()
{
    var matrix = new List<IList<string>>
    {
        new List<string> { "id", "power", "enabled", "starts", "payload" },
        new List<string> { "string", "integer", "bool", "date", "json" },
        new List<string> { "稳定 ID", "数值", "开关", "日期", "对象" },
        new List<string> { "item_sword", "0010", "是", "2026/5/24", "{\"a\":1}" }
    };

    var imported = MatrixWorkbookImporter.Import(matrix, new MatrixWorkbookImportOptions
    {
        ProviderId = "test",
        SourceId = "typed",
        SheetName = "Items",
        FieldRow = 0,
        TypeRow = 1,
        DescriptionRow = 2,
        DataStartRow = 3
    });

    var row = imported.Workbook.Sheets[0].Rows[0];
    AssertEqual("integer", row.Cells["power"].ValueKind, "Power should be typed as integer.");
    AssertEqual("10", row.Cells["power"].NormalizedText, "Integer normalization should remove leading zeroes.");
    AssertEqual("true", row.Cells["enabled"].NormalizedText, "Localized boolean should normalize to true.");
    AssertEqual("2026-05-24", row.Cells["starts"].NormalizedText, "Date should normalize to ISO date.");
    AssertTrue(!imported.Report.HasErrors, "Typed import should not produce errors.");
}

static void EnumOptionDriftIsReported()
{
    var matrix = new List<IList<string>>
    {
        new List<string> { "id", "rarity" },
        new List<string> { "string", "enum:common,rare" },
        new List<string> { "item_sword", "legendary" }
    };

    var imported = MatrixWorkbookImporter.Import(matrix, new MatrixWorkbookImportOptions
    {
        ProviderId = "test",
        SourceId = "enum",
        SheetName = "Items",
        FieldRow = 0,
        TypeRow = 1,
        DataStartRow = 2
    });

    AssertTrue(imported.Report.Findings.Any(f => f.Code == "cell.enum_option_drift"), "Enum option drift should be reported.");
}

static void LarkReadParserAcceptsWrappedValues()
{
    var json = "{\"data\":{\"values\":[[\"id\",\"score\"],[\"a\",1]]}}";
    var imported = InvokeLarkImport(json);
    AssertEqual("a", imported.Workbook.Sheets[0].Rows[0].StableId, "Wrapped values should import row id.");
}

static void LarkReadParserAcceptsRecordArrays()
{
    var json = "{\"items\":[{\"id\":\"a\",\"score\":1},{\"id\":\"b\",\"score\":2}]}";
    var imported = InvokeLarkImport(json);
    AssertEqual("b", imported.Workbook.Sheets[0].Rows[1].StableId, "Record arrays should import rows.");
}

static void LarkReadParserMatchesObjectKeysCaseInsensitively()
{
    var json = "{\"items\":[{\"ID\":\"a\",\"score\":1},{\"id\":\"b\",\"Score\":2}]}";
    var imported = InvokeLarkImport(json);
    AssertEqual("b", imported.Workbook.Sheets[0].Rows[1].StableId, "Record array row lookup should ignore key casing.");
    AssertEqual("2", imported.Workbook.Sheets[0].Rows[1].Cells["score"].RawText, "Record array values should ignore key casing.");
}

static void DateTimeNormalizationDoesNotAssumeLocalTimezone()
{
    var withoutOffset = MatrixWorkbookImporter.NormalizeCell("2026-05-24 10:00", "datetime");
    var withOffset = MatrixWorkbookImporter.NormalizeCell("2026-05-24T10:00:00+08:00", "datetime");
    AssertEqual("2026-05-24T10:00:00.000Z", withoutOffset.NormalizedText, "Timezone-less datetime values should be treated as UTC.");
    AssertEqual("2026-05-24T02:00:00.000Z", withOffset.NormalizedText, "Offset datetimes should normalize to UTC.");
}

static MatrixWorkbookImportResult InvokeLarkImport(string json)
{
    var method = typeof(LarkCliWorkbookProvider).GetMethod("BuildWorkbookFromReadJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    AssertTrue(method != null, "Expected provider import method.");
    var request = new ProviderExportRequest { TableId = "items", SheetId = "sheet", FieldRow = 0 };
    return (MatrixWorkbookImportResult)method!.Invoke(null, new object[] { json, request, "source" })!;
}

static async Task GateCanPrintGitHubAnnotations()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-gate-" + Guid.NewGuid().ToString("N"));
    var cache = Path.Combine(temp, ".config-sheet-forge", "cache");
    Directory.CreateDirectory(cache);
    try
    {
        var workbook = SampleWorkbook();
        workbook.Sheets[0].Rows[1].StableId = workbook.Sheets[0].Rows[0].StableId;
        var json = JsonSerializer.Serialize(workbook, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(Path.Combine(cache, "items.semantic.json"), json);

        var oldOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        var oldDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(temp);
            var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "gate", "--cache", cache, "--annotations", "github" });
            AssertEqual("1", exitCode.ToString(), "Gate should fail for duplicate rows.");
        }
        finally
        {
            Directory.SetCurrentDirectory(oldDir);
            Console.SetOut(oldOut);
        }

        AssertTrue(writer.ToString().Contains("::error file="), "Gate should print GitHub annotations.");
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }
}

static WorkbookDocument SampleWorkbook()
{
    var workbook = new WorkbookDocument
    {
        ProviderId = "test",
        SourceId = "example",
        SourceTitle = "Example"
    };

    var sheet = new SheetDocument
    {
        Id = "items",
        Name = "Items"
    };
    sheet.Columns.Add(new ColumnDefinition { Key = "id", DisplayName = "ID", ValueKind = "string", Required = true });
    sheet.Columns.Add(new ColumnDefinition { Key = "name", DisplayName = "Name", ValueKind = "string", Required = true });
    sheet.Columns.Add(new ColumnDefinition { Key = "power", DisplayName = "Power", ValueKind = "number" });

    var row1 = new RowDocument { StableId = "item_sword", SourceIndex = 2 };
    row1.Cells["id"] = new CellValue { RawText = "item_sword", NormalizedText = "item_sword" };
    row1.Cells["name"] = new CellValue { RawText = "Sword", NormalizedText = "Sword" };
    row1.Cells["power"] = new CellValue { ValueKind = "number", RawText = "10", NormalizedText = "10" };

    var row2 = new RowDocument { StableId = "item_shield", SourceIndex = 3 };
    row2.Cells["id"] = new CellValue { RawText = "item_shield", NormalizedText = "item_shield" };
    row2.Cells["name"] = new CellValue { RawText = "Shield", NormalizedText = "Shield" };
    row2.Cells["power"] = new CellValue { ValueKind = "number", RawText = "4", NormalizedText = "4" };

    sheet.Rows.Add(row1);
    sheet.Rows.Add(row2);
    workbook.Sheets.Add(sheet);
    return workbook;
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(message + " Expected '" + expected + "', got '" + actual + "'.");
    }
}
