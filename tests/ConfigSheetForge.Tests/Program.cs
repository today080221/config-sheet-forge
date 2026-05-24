using System.Text.Json;
using ConfigSheetForge.Core;
using ConfigSheetForge.Providers.Lark;

var tests = new List<(string Name, Action Body)>
{
    ("semantic hash is stable across row order", SemanticHashIsStableAcrossRowOrder),
    ("portable validator catches duplicate row ids", ValidatorCatchesDuplicateRowIds),
    ("three-way merge detects real conflicts", MergeDetectsConflicts),
    ("core model round-trips through json", CoreModelRoundTripsThroughJson),
    ("lark cli discovery resolves windows npm shim", LarkCliDiscoveryResolvesWindowsNpmShim)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
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
