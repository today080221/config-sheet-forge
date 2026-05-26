using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.IO.Compression;
using ConfigSheetForge.Core;
using ConfigSheetForge.Cli;
using ConfigSheetForge.Providers.Lark;

var tests = new List<(string Name, Func<Task> Body)>
{
    ("semantic hash is stable across row order", () => RunSync(SemanticHashIsStableAcrossRowOrder)),
    ("portable validator catches duplicate row ids", () => RunSync(ValidatorCatchesDuplicateRowIds)),
    ("three-way merge detects real conflicts", () => RunSync(MergeDetectsConflicts)),
    ("core model round-trips through json", () => RunSync(CoreModelRoundTripsThroughJson)),
    ("lark cli discovery prefers windows ps1 shim", () => RunSync(LarkCliDiscoveryPrefersWindowsPs1Shim)),
    ("type row import normalizes semantic values", () => RunSync(TypeRowImportNormalizesSemanticValues)),
    ("enum option drift is reported", () => RunSync(EnumOptionDriftIsReported)),
    ("lark read parser accepts wrapped values", () => RunSync(LarkReadParserAcceptsWrappedValues)),
    ("lark read parser accepts record arrays", () => RunSync(LarkReadParserAcceptsRecordArrays)),
    ("lark read parser matches object keys case-insensitively", () => RunSync(LarkReadParserMatchesObjectKeysCaseInsensitively)),
    ("datetime normalization does not assume local timezone", () => RunSync(DateTimeNormalizationDoesNotAssumeLocalTimezone)),
    ("registry localization keeps machine keys", () => RunSync(RegistryLocalizationKeepsMachineKeys)),
    ("registry migration cleans default rows and fields", () => RunSync(RegistryMigrationCleansDefaults)),
    ("lark base matrix record lookup finds existing record", () => RunSync(LarkBaseMatrixRecordLookupFindsExistingRecord)),
    ("lark 1.0.40 registry fixture hydrates and reports duplicate bindings", Lark140RegistryFixtureHydratesAndReportsDuplicateBindings),
    ("registry migrate keeps table and field list argv compatible", RegistryMigrateKeepsTableAndFieldListArgvCompatible),
    ("registry migration reports branch binding cleanup risks", () => RunSync(RegistryMigrationReportsBranchBindingCleanupRisks)),
    ("branch workspace resolver creates stable slugs", () => RunSync(BranchWorkspaceResolverCreatesStableSlugs)),
    ("branch binding one-to-one conflict blocks lifecycle", BranchBindingOneToOneConflictBlocksLifecycle),
    ("duplicate branch bindings block lifecycle with record ids", DuplicateBranchBindingsBlockLifecycleWithRecordIds),
    ("sync-cache hydrates branch workspace from registry snapshot", SyncCacheHydratesBranchWorkspaceFromRegistrySnapshot),
    ("seed registry lookup reuses existing branch binding record", () => RunSync(SeedRegistryLookupReusesExistingBranchBindingRecord)),
    ("lifecycle new-table dry-run does not write local files", LifecycleNewTableDryRunDoesNotWriteFiles),
    ("lifecycle new-table apply mock completes steps", LifecycleNewTableApplyMockCompletesSteps),
    ("excel to so updater appends json settings", () => RunSync(ExcelToSoUpdaterAppendsJsonSettings)),
    ("excel to so updater updates existing json settings", () => RunSync(ExcelToSoUpdaterUpdatesExistingJsonSettings)),
    ("excel to so updater preserves json schema and encoding", () => RunSync(ExcelToSoUpdaterPreservesJsonSchemaAndEncoding)),
    ("project config upsert writes nested feishu node", () => RunSync(ProjectConfigUpsertWritesNestedFeishuNode)),
    ("project config probe reads lifecycle summary", () => RunSync(ProjectConfigProbeReadsLifecycleSummary)),
    ("project config probe skips registry base table ids", () => RunSync(ProjectConfigProbeSkipsRegistryBaseTableIds)),
    ("project config probe derives branch workspace names", () => RunSync(ProjectConfigProbeDerivesBranchWorkspaceNames)),
    ("project config probe ignores local state registry", () => RunSync(ProjectConfigProbeIgnoresLocalStateRegistry)),
    ("apply-contract pr-gate-report writes standard report", ApplyContractPrGateReportWritesStandardReport),
    ("seed dry-run plans xlsx migration without writes", SeedDryRunPlansXlsxMigrationWithoutWrites),
    ("seed manifest does not treat cache excelPath as source", SeedManifestDoesNotTreatCacheExcelPathAsSource),
    ("seed dry-run blocks merged xlsx cells", SeedDryRunBlocksMergedCells),
    ("sheet write ranges support columns past z", () => RunSync(SheetWriteRangeSupportsColumnsPastZ)),
    ("portable subset blocks unsupported structures", () => RunSync(PortableSubsetBlocksUnsupportedStructures)),
    ("triangulation passes and fails with readable diffs", () => RunSync(TriangulationPassesAndFailsWithReadableDiffs)),
    ("sync local input does not rewrite unchanged cache", SyncLocalInputDoesNotRewriteUnchangedCache),
    ("strict bot mode does not fallback to user", StrictBotModeDoesNotFallbackToUser),
    ("seed lifecycle rejects user fallback", SeedLifecycleRejectsUserFallback),
    ("gate report evaluator reports human failures", () => RunSync(GateReportEvaluatorReportsHumanFailures)),
    ("powershell json safety flags inline json params", () => RunSync(PowerShellJsonSafetyFlagsInlineJsonParams)),
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

static void LarkCliDiscoveryPrefersWindowsPs1Shim()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-lark-discovery-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var cmdShim = Path.Combine(temp, "lark-cli.cmd");
    var ps1Shim = Path.Combine(temp, "lark-cli.ps1");
    File.WriteAllText(cmdShim, "@echo off\r\nexit /b 0\r\n");
    File.WriteAllText(ps1Shim, "exit 0\r\n");

    var oldPath = Environment.GetEnvironmentVariable("PATH");
    var oldEnv = Environment.GetEnvironmentVariable("LARK_CLI_PATH");
    try
    {
        Environment.SetEnvironmentVariable("LARK_CLI_PATH", null);
        Environment.SetEnvironmentVariable("PATH", temp + Path.PathSeparator + oldPath);
        var resolved = LarkCliDiscovery.Resolve("lark-cli");
        AssertEqual(Path.GetFullPath(ps1Shim), Path.GetFullPath(resolved.DisplayPath), "Discovery should prefer the .ps1 shim on Windows.");
        AssertTrue(resolved.Source.Contains(":ps1"), "Discovery should report the ps1 launcher source.");
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

static void RegistryLocalizationKeepsMachineKeys()
{
    var mapping = RegistryLocalization.Default("zh-Hans");
    AssertEqual("配表清单", mapping.Tables["ConfigSheets"], "ConfigSheets should have a Chinese display name.");
    AssertEqual("配表ID", mapping.Fields["TableId"], "TableId should have a Chinese display name.");
    AssertTrue(mapping.Fields.ContainsKey("WikiNodeToken"), "ConfigSheets should support wiki node token for branch-aware registration.");
    AssertTrue(mapping.Fields.ContainsKey("SemanticHash"), "ConfigSheets should support semantic hash for branch-aware registration.");
    AssertTrue(mapping.Tables.ContainsKey("ConfigSheets"), "Machine key must remain in the mapping.");
}

static void RegistryMigrationCleansDefaults()
{
    var snapshot = new RegistrySnapshot();
    snapshot.Tables.Add(new RegistryTableSnapshot
    {
        MachineKey = "ConfigSheets",
        TableId = "tbl_config",
        DisplayName = "ConfigSheets",
        Fields =
        {
            new RegistryFieldSnapshot { MachineKey = "TableId", FieldId = "fld_table_id", DisplayName = "TableId" },
            new RegistryFieldSnapshot { FieldId = "fld_text", DisplayName = "Text", IsDefaultField = true }
        },
        Records =
        {
            new RegistryRecordSnapshot { RecordId = "rec_empty" }
        }
    });

    var plan = RegistryMigrator.Plan(snapshot, new RegistryMigrationOptions
    {
        Locale = "zh-Hans",
        CleanupDefaultRows = true,
        CleanupDefaultFields = true
    });

    AssertTrue(plan.Actions.Any(a => a.Action == "registry.table.rename"), "Existing English table should be renamed for UI display.");
    AssertTrue(plan.Actions.Any(a => a.Action == "registry.record.delete_empty"), "Default empty rows should be cleaned.");
    AssertTrue(plan.Actions.Any(a => a.Action == "registry.field.delete_default"), "Default fields should be cleaned.");
}

static void LarkBaseMatrixRecordLookupFindsExistingRecord()
{
    var json = """
    {
      "data": {
        "fields": ["Git分支", "配置Profile", "Wiki节点Token"],
        "data": [
          ["feature/config", "feature-config", "wik_feature"]
        ],
        "record_id_list": ["rec_branch_1"]
      }
    }
    """;

    var records = ConfigSheetForge.Cli.Program.ParseLarkBaseRecordListJson(json);
    AssertEqual("1", records.Count.ToString(), "Matrix record-list should produce one normalized record.");
    AssertEqual("rec_branch_1", records[0].RecordId, "record_id_list should map onto the normalized record.");
    var matches = ConfigSheetForge.Cli.Program.FindMatchingRegistryRecords(records, new Dictionary<string, string>
    {
        ["GitBranch"] = "feature/config",
        ["Profile"] = "feature-config"
    }, "zh-Hans");
    AssertEqual("1", matches.Count.ToString(), "Machine-key lookup should match Chinese display-name fields.");
    AssertEqual("rec_branch_1", matches[0].RecordId, "Lookup should return the existing record id.");
}

static async Task Lark140RegistryFixtureHydratesAndReportsDuplicateBindings()
{
    var tableListJson = """
    {
      "ok": true,
      "identity": "bot",
      "data": {
        "tables": [
          { "id": "tbl_branch", "name": "分支绑定" },
          { "id": "tbl_config", "name": "配表清单" }
        ],
        "total": 2
      }
    }
    """;
    var branchFieldsJson = """
    {
      "ok": true,
      "data": {
        "fields": [
          { "id": "fld_git", "name": "Git分支", "type": "text" },
          { "id": "fld_profile", "name": "配置Profile", "type": "text" },
          { "id": "fld_wiki", "name": "Wiki节点Token", "type": "text" },
          { "id": "fld_url", "name": "Wiki节点链接", "type": "url" },
          { "id": "fld_status", "name": "状态", "type": "single_select" }
        ]
      }
    }
    """;
    var configFieldsJson = """
    {
      "ok": true,
      "data": {
        "fields": [
          { "id": "fld_table", "name": "配表ID", "type": "text" },
          { "id": "fld_branch", "name": "Feishu分支", "type": "text" },
          { "id": "fld_profile", "name": "配置Profile", "type": "text" },
          { "id": "fld_token", "name": "在线表Token", "type": "text" },
          { "id": "fld_sheet", "name": "工作表ID", "type": "text" }
        ]
      }
    }
    """;
    var branchRecordsJson = """
    {
      "ok": true,
      "data": {
        "fields": ["ID", "状态", "Wiki节点链接", "创建人", "Git分支", "配置Profile", "Wiki节点Token"],
        "data": [
          ["1", "active", "https://example.feishu.cn/wiki/wik_feature", "bot", "codex/config-sheet-seed-feishu-main", "codex/config-sheet-seed-feishu-main", "wik_feature"],
          ["2", "active", "https://example.feishu.cn/wiki/wik_feature", "bot", "codex/config-sheet-seed-feishu-main", "codex/config-sheet-seed-feishu-main", "wik_feature"]
        ],
        "record_id_list": ["rec_dup_a", "rec_dup_b"]
      }
    }
    """;
    var configRecordsJson = """
    {
      "ok": true,
      "data": {
        "fields": ["配表ID", "显示名称", "Feishu分支", "配置Profile", "在线表Token", "工作表ID", "在线表链接"],
        "data": [
          ["ItemsData", "Items", "codex/config-sheet-seed-feishu-main", "codex/config-sheet-seed-feishu-main", "sht_items", "sheet_items", "https://example.feishu.cn/sheets/sht_items"]
        ],
        "record_id_list": ["rec_sheet"]
      }
    }
    """;

    var snapshot = ConfigSheetForge.Cli.Program.ParseLarkBaseRegistrySnapshotJson(
        tableListJson,
        new Dictionary<string, string>
        {
            ["tbl_branch"] = branchFieldsJson,
            ["tbl_config"] = configFieldsJson
        },
        new Dictionary<string, string>
        {
            ["tbl_branch"] = branchRecordsJson,
            ["tbl_config"] = configRecordsJson
        },
        "zh-Hans");

    AssertEqual("2", snapshot.Tables.Count.ToString(), "lark 1.0.40 data.tables[].id/name should load registry tables.");
    var branchTable = snapshot.Tables.First(t => t.MachineKey == "BranchBindings");
    AssertEqual("tbl_branch", branchTable.TableId, "BranchBindings table id should come from data.tables[].id.");
    AssertEqual("2", branchTable.Records.Count.ToString(), "Matrix record-list rows should be attached to the table snapshot.");

    var plan = RegistryMigrator.Plan(snapshot, new RegistryMigrationOptions { Locale = "zh-Hans" });
    AssertTrue(plan.Actions.Any(a => a.Action == "registry.branch_bindings.duplicate" && a.Details["recordIds"].Contains("rec_dup_a") && a.Details["recordIds"].Contains("rec_dup_b")), "registry-migrate dry-run must list duplicate BranchBindings record ids.");

    var request = new LifecycleContractRequest
    {
        Operation = "sync-cache",
        DryRun = true,
        Locale = "zh-Hans",
        Git = new ContractGitSpec { Branch = "codex/config-sheet-seed-feishu-main", Profile = "codex/config-sheet-seed-feishu-main" },
        BranchWorkspace = new BranchWorkspaceContract { RequireOneToOneBinding = true },
        Registry = new RegistryContract { BaseToken = "base_mock" }
    };

    ConfigSheetForge.Cli.Program.HydrateSyncCacheRequestFromRegistrySnapshot(request, snapshot, "");
    AssertTrue(request.BranchBindings.Count(b => b.GitBranch == "codex/config-sheet-seed-feishu-main") == 2, "sync-cache should hydrate duplicate live BranchBindings, not collapse them.");
    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
    AssertTrue(!result.Success, "sync-cache dry-run should block duplicate BranchBindings.");
    AssertTrue(result.HumanReadableFailures.Any(f => f.Contains("重复记录") && f.Contains("rec_dup_a") && f.Contains("rec_dup_b")), "sync-cache duplicate blocker should list record ids.");
}

static async Task RegistryMigrateKeepsTableAndFieldListArgvCompatible()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-registry-argv-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var oldDir = Directory.GetCurrentDirectory();
    var script = Path.Combine(temp, "lark-cli.cmd");
    var log = Path.Combine(temp, "calls.log");
    var output = Path.Combine(temp, "registry-migrate.json");
    await File.WriteAllTextAsync(script,
        "@echo off\r\n" +
        "echo %*>>\"" + log + "\"\r\n" +
        "echo %* | find \"doctor\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +table-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo %* | find \"--format\" >nul\r\n" +
        "  if not errorlevel 1 (\r\n" +
        "    echo Error: unknown flag: --format 1>&2\r\n" +
        "    exit /b 2\r\n" +
        "  )\r\n" +
        "  echo {\"ok\":true,\"data\":{\"tables\":[{\"id\":\"tbl_branch\",\"name\":\"BranchBindings\"}],\"total\":1}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +field-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo %* | find \"--format\" >nul\r\n" +
        "  if not errorlevel 1 (\r\n" +
        "    echo Error: unknown flag: --format 1>&2\r\n" +
        "    exit /b 2\r\n" +
        "  )\r\n" +
        "  echo {\"ok\":true,\"data\":{\"fields\":[{\"id\":\"fld_git\",\"name\":\"GitBranch\",\"type\":\"text\"},{\"id\":\"fld_profile\",\"name\":\"Profile\",\"type\":\"text\"}]}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +record-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true,\"data\":{\"fields\":[\"GitBranch\",\"Profile\"],\"data\":[[\"feature/config\",\"feature-config\"],[\"feature/config\",\"feature-config\"]],\"record_id_list\":[\"rec_dup_a\",\"rec_dup_b\"]}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo unexpected command %* 1>&2\r\n" +
        "exit /b 2\r\n");

    try
    {
        Directory.SetCurrentDirectory(temp);
        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[]
        {
            "registry-migrate",
            "--base", "base_mock",
            "--locale", "zh-Hans",
            "--dry-run",
            "--cleanup-duplicate-branch-bindings",
            "--lark-cli", script,
            "--out", output
        });
        AssertEqual("0", exitCode.ToString(), "registry-migrate should not pass --format to table-list or field-list.");
        var calls = File.ReadAllLines(log);
        AssertTrue(calls.Any(line => line.Contains("base +table-list") && !line.Contains("--format")), "table-list argv should avoid unsupported --format.");
        AssertTrue(calls.Any(line => line.Contains("base +field-list") && !line.Contains("--format")), "field-list argv should avoid unsupported --format.");
        AssertTrue(calls.Any(line => line.Contains("base +record-list") && line.Contains("--format json")), "record-list should still request JSON output explicitly.");
        var resultJson = await File.ReadAllTextAsync(output);
        AssertTrue(resultJson.Contains("rec_dup_a") && resultJson.Contains("rec_dup_b"), "dry-run should still list duplicate BranchBindings record ids.");
    }
    finally
    {
        Directory.SetCurrentDirectory(oldDir);
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

static void RegistryMigrationReportsBranchBindingCleanupRisks()
{
    var snapshot = new RegistrySnapshot();
    snapshot.Tables.Add(new RegistryTableSnapshot
    {
        MachineKey = "BranchBindings",
        TableId = "tbl_branch",
        DisplayName = "分支绑定",
        Fields =
        {
            new RegistryFieldSnapshot { MachineKey = "GitBranch", FieldId = "fld_git_en", DisplayName = "GitBranch" },
            new RegistryFieldSnapshot { FieldId = "fld_git_zh", DisplayName = "Git分支" },
            new RegistryFieldSnapshot { MachineKey = "Profile", FieldId = "fld_profile", DisplayName = "配置Profile" }
        },
        Records =
        {
            new RegistryRecordSnapshot { RecordId = "rec_a", Values = { ["Git分支"] = "feature/config", ["配置Profile"] = "feature-config" } },
            new RegistryRecordSnapshot { RecordId = "rec_b", Values = { ["GitBranch"] = "feature/config", ["Profile"] = "feature-config" } },
            new RegistryRecordSnapshot { RecordId = "rec_empty" }
        }
    });

    var plan = RegistryMigrator.Plan(snapshot, new RegistryMigrationOptions { Locale = "zh-Hans" });
    AssertTrue(plan.Actions.Any(a => a.Action == "registry.branch_bindings.duplicate" && a.Details["recordIds"].Contains("rec_a") && a.Details["recordIds"].Contains("rec_b")), "Duplicate BranchBindings should be listed in dry-run.");
    AssertTrue(plan.Actions.Any(a => a.Action == "registry.record.empty_default"), "Blank default rows should be listed even when cleanup is not enabled.");
    AssertTrue(plan.Actions.Any(a => a.Action == "registry.field.ambiguous_alias" && a.Details["machineKey"] == "GitBranch"), "Bilingual duplicate fields should be reported as ambiguous.");
}

static void BranchWorkspaceResolverCreatesStableSlugs()
{
    var request = new LifecycleContractRequest
    {
        Git = new ContractGitSpec { Branch = "codex/config-sheet-seed-feishu-main" },
        BranchWorkspace = new BranchWorkspaceContract
        {
            RootWikiToken = "wik_root",
            RootWikiTitle = "项目配置表",
            ProfileNameTemplate = "{gitBranch}",
            BranchNodeTitleTemplate = "branch-{slug}",
            MainGitBranch = "main",
            MainFeishuBranch = "main"
        }
    };

    var resolved = BranchWorkspaceResolver.Resolve(request);
    AssertEqual("codex-config-sheet-seed-feishu-main", resolved.Slug, "Branch slug should be filesystem and Feishu friendly.");
    AssertEqual("branch-codex-config-sheet-seed-feishu-main", resolved.NodeTitle, "Non-main branch should resolve to a branch node.");
    AssertEqual("codex/config-sheet-seed-feishu-main", resolved.Profile, "Raw profile should be preserved for registry display.");

    request.Git.Branch = "main";
    var main = BranchWorkspaceResolver.Resolve(request);
    AssertEqual("main", main.NodeTitle, "Main branch should use the main node.");
}

static async Task BranchBindingOneToOneConflictBlocksLifecycle()
{
    var request = new LifecycleContractRequest
    {
        Operation = "sync-cache",
        DryRun = true,
        Git = new ContractGitSpec { Branch = "feature/config", Profile = "feature-config" },
        BranchWorkspace = new BranchWorkspaceContract { RequireOneToOneBinding = true },
        BranchBindings =
        {
            new BranchBindingContract { GitBranch = "feature/config", Profile = "feature-config" },
            new BranchBindingContract { GitBranch = "other/config", Profile = "feature-config" }
        }
    };

    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
    AssertTrue(!result.Success, "One profile bound to multiple git branches should block lifecycle.");
    AssertTrue(result.HumanReadableFailures.Any(f => f.Contains("Feishu profile") && f.Contains("Git 分支")), "Failure should be readable for designers.");
}

static async Task DuplicateBranchBindingsBlockLifecycleWithRecordIds()
{
    var request = new LifecycleContractRequest
    {
        Operation = "sync-cache",
        DryRun = true,
        Git = new ContractGitSpec { Branch = "feature/config", Profile = "feature-config" },
        BranchWorkspace = new BranchWorkspaceContract { RequireOneToOneBinding = true },
        BranchBindings =
        {
            new BranchBindingContract { RecordId = "rec_a", GitBranch = "feature/config", Profile = "feature-config", WikiNodeToken = "wik_a" },
            new BranchBindingContract { RecordId = "rec_b", GitBranch = "feature/config", Profile = "feature-config", WikiNodeToken = "wik_b" }
        }
    };

    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
    AssertTrue(!result.Success, "Duplicate BranchBindings for the same GitBranch + Profile must block.");
    AssertTrue(result.HumanReadableFailures.Any(f => f.Contains("重复记录") && f.Contains("rec_a") && f.Contains("rec_b")), "Duplicate failure should list record ids and be readable.");
}

static async Task SyncCacheHydratesBranchWorkspaceFromRegistrySnapshot()
{
    var request = new LifecycleContractRequest
    {
        Operation = "sync-cache",
        DryRun = true,
        Locale = "zh-Hans",
        Git = new ContractGitSpec { Branch = "feature/config", Profile = "feature-config" },
        BranchWorkspace = new BranchWorkspaceContract { RequireOneToOneBinding = true },
        Registry = new RegistryContract { BaseToken = "base_mock" }
    };
    request.BranchBindings.Add(new BranchBindingContract { GitBranch = "main", Profile = "main", WikiNodeToken = "wik_main" });

    var snapshot = new RegistrySnapshot();
    snapshot.Tables.Add(new RegistryTableSnapshot
    {
        MachineKey = "BranchBindings",
        TableId = "tbl_branch",
        DisplayName = "分支绑定",
        Records =
        {
            new RegistryRecordSnapshot
            {
                RecordId = "rec_branch",
                Values =
                {
                    ["Git分支"] = "feature/config",
                    ["配置Profile"] = "feature-config",
                    ["Wiki节点Token"] = "wik_feature",
                    ["Wiki节点链接"] = "https://example.feishu.cn/wiki/wik_feature",
                    ["状态"] = "active"
                }
            }
        }
    });
    snapshot.Tables.Add(new RegistryTableSnapshot
    {
        MachineKey = "ConfigSheets",
        TableId = "tbl_config",
        DisplayName = "配表清单",
        Records =
        {
            new RegistryRecordSnapshot
            {
                RecordId = "rec_sheet",
                Values =
                {
                    ["配表ID"] = "ItemsData",
                    ["显示名称"] = "Items",
                    ["Feishu分支"] = "feature-config",
                    ["配置Profile"] = "feature-config",
                    ["在线表Token"] = "sht_items",
                    ["工作表ID"] = "sheet_items",
                    ["在线表链接"] = "https://example.feishu.cn/sheets/sht_items",
                    ["Wiki节点Token"] = "wik_feature"
                }
            }
        }
    });

    ConfigSheetForge.Cli.Program.HydrateSyncCacheRequestFromRegistrySnapshot(request, snapshot, "");
    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
    AssertTrue(result.Success, "Hydrated BranchBindings should let sync-cache dry-run continue.");
    AssertEqual("wik_feature", result.BranchWorkspace.WikiNodeToken, "Branch workspace should come from live registry data.");
    AssertTrue(request.SeedFromLocalXlsx.Tables.Any(t => t.TableId == "ItemsData" && t.SpreadsheetToken == "sht_items"), "ConfigSheets rows should hydrate sync-cache table locators.");
    AssertTrue(result.Actions.Any(a => a.Action == "sync-cache.online_read" && a.Status == "planned"), "sync-cache dry-run should plan online read.");
    AssertTrue(result.Actions.Any(a => a.Action == "sync-cache.export_xlsx" && a.Status == "planned"), "sync-cache dry-run should plan export.");
    AssertTrue(result.Actions.Any(a => a.Action == "sync-cache.triangulation_compare" && a.Status == "planned"), "sync-cache dry-run should plan triangulation.");
    AssertTrue(result.Actions.Any(a => a.Action == "sync-cache.cache_hash_gate" && a.Status == "planned"), "sync-cache dry-run should plan hash gate.");
}

static void SeedRegistryLookupReusesExistingBranchBindingRecord()
{
    var json = """
    {
      "data": {
        "fields": ["GitBranch", "Profile", "WikiNodeToken"],
        "data": [
          ["feature/config", "feature-config", "wik_feature"]
        ],
        "record_id_list": ["rec_existing"]
      }
    }
    """;

    var records = ConfigSheetForge.Cli.Program.ParseLarkBaseRecordListJson(json);
    var matches = ConfigSheetForge.Cli.Program.FindMatchingRegistryRecords(records, new Dictionary<string, string>
    {
        ["Git分支"] = "feature/config",
        ["配置Profile"] = "feature-config"
    }, "zh-Hans");
    AssertEqual("1", matches.Count.ToString(), "Seed upsert should find the existing BranchBindings record by GitBranch + Profile.");
    AssertEqual("rec_existing", matches[0].RecordId, "Seed rerun should reuse record_id instead of appending.");
}

static async Task LifecycleNewTableDryRunDoesNotWriteFiles()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-lifecycle-dry-" + Guid.NewGuid().ToString("N"));
    var settings = Path.Combine(temp, "ProjectSettings", "ExcelToScriptableObjectSettings.asset");
    try
    {
        var request = SampleNewTableRequest(settings);
        request.DryRun = true;
        var result = await LifecycleExecutor.ExecuteAsync(request, new RecordingLifecyclePlatform(), CancellationToken.None);
        AssertTrue(result.Success, "Dry-run new-table should succeed.");
        AssertTrue(!File.Exists(settings), "Dry-run must not write Unity settings.");
        AssertEqual("preview-schema-review-id", result.SchemaReviewId, "Dry-run should still preview schema review id.");
    }
    finally
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

static async Task LifecycleNewTableApplyMockCompletesSteps()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-lifecycle-apply-" + Guid.NewGuid().ToString("N"));
    var settings = Path.Combine(temp, "ProjectSettings", "ExcelToScriptableObjectSettings.asset");
    try
    {
        var request = SampleNewTableRequest(settings);
        var platform = new RecordingLifecyclePlatform();
        var result = await LifecycleExecutor.ExecuteAsync(request, platform, CancellationToken.None);
        AssertTrue(result.Success, "Apply new-table should succeed with the mock platform.");
        AssertEqual("sht_mock", result.SpreadsheetToken, "Result should include spreadsheet token.");
        AssertEqual("sheet_mock", result.SheetId, "Result should include sheet id.");
        AssertEqual("wik_mock", result.WikiNodeToken, "Result should include wiki node token.");
        AssertEqual("rec_config", result.RegistryRecordId, "Result should include registry record id.");
        AssertEqual("schema_pending", result.SchemaReviewId, "Result should include schema review id.");
        AssertTrue(platform.Calls.Contains("create-sheet"), "Mock provider should create the sheet.");
        AssertTrue(platform.Calls.Contains("write-template"), "Mock provider should write the template.");
        AssertTrue(platform.Calls.Contains("upsert-registry"), "Mock provider should upsert registry.");
        AssertTrue(platform.Calls.Contains("upsert-schema"), "Mock provider should upsert schema review.");
        AssertTrue(File.ReadAllText(settings).Contains("excelPath: Assets/Config/Items.xlsx"), "Unity settings should append the target table without rewriting unrelated entries.");
    }
    finally
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

static void ExcelToSoUpdaterAppendsJsonSettings()
{
    var json = "{\n  \"configs\": [\n    { \"tableId\": \"SkillsData\", \"excelPath\": \"Excel/SkillsData.xlsx\" }\n  ]\n}\n";
    var updated = UnityExcelToSoSettingsUpdater.UpsertText(json, new UnityExcelToSoEntry
    {
        TableId = "MonsterData",
        ExcelPath = "Excel/MonsterData.xlsx",
        ScriptableObjectType = "MonsterConfig"
    });

    AssertTrue(updated.Contains("\"tableId\": \"SkillsData\""), "Existing JSON entry should stay.");
    AssertTrue(updated.Contains("\"tableId\": \"MonsterData\""), "New JSON entry should be appended.");
    AssertTrue(updated.IndexOf("SkillsData", StringComparison.Ordinal) < updated.IndexOf("MonsterData", StringComparison.Ordinal), "Existing entry should not be reordered after the new entry.");
}

static void ExcelToSoUpdaterUpdatesExistingJsonSettings()
{
    var json = "{\n  \"configs\": [\n    { \"tableId\": \"SkillsData\", \"excelPath\": \"Excel/SkillsData.xlsx\", \"scriptableObjectType\": \"OldType\" },\n    { \"tableId\": \"RoomData\", \"excelPath\": \"Excel/RoomData.xlsx\" }\n  ]\n}\n";
    var updated = UnityExcelToSoSettingsUpdater.UpsertText(json, new UnityExcelToSoEntry
    {
        TableId = "SkillsData",
        ExcelPath = ".config-sheet-forge/excel-cache/SkillsData.xlsx",
        ScriptableObjectType = "SkillConfig"
    });

    AssertTrue(updated.Contains("\"tableId\": \"SkillsData\""), "Existing target entry should stay.");
    AssertTrue(updated.Contains(".config-sheet-forge/excel-cache/SkillsData.xlsx"), "Existing target entry should update its xlsx path.");
    AssertTrue(updated.Contains("\"scriptableObjectType\": \"SkillConfig\""), "Existing target entry should update metadata.");
    AssertTrue(updated.Contains("\"tableId\": \"RoomData\""), "Unrelated entries should stay.");
    AssertTrue(updated.IndexOf("SkillsData", StringComparison.Ordinal) < updated.IndexOf("RoomData", StringComparison.Ordinal), "Updater should not reorder unrelated entries.");
}

static void ExcelToSoUpdaterPreservesJsonSchemaAndEncoding()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-excel-to-so-json-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var path = Path.Combine(temp, "ExcelToScriptableObjectSettings.asset");
    try
    {
        var original = "{\r\n  \"configs\": [\r\n    { \"name\": \"SkillsData\", \"ExcelPath\": \"Assets\\\\Excel\\\\SkillsData.xlsx\", \"keep\": \"yes\" },\r\n    { \"name\": \"RoomData\", \"ExcelPath\": \"Assets\\\\Excel\\\\RoomData.xlsx\" }\r\n  ]\r\n}\r\n";
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(original)).ToArray());
        var update = UnityExcelToSoSettingsUpdater.UpsertFile(path, new UnityExcelToSoEntry
        {
            TableId = "SkillsData",
            ExcelPath = ".config-sheet-forge/excel-cache/SkillsData.xlsx",
            ScriptableObjectType = "SkillConfig"
        });

        var bytes = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(bytes.Skip(3).ToArray());
        AssertTrue(update.Changed, "Existing schema-specific JSON entry should be updated.");
        AssertTrue(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UTF-8 BOM should be preserved.");
        AssertTrue(text.Contains("\r\n"), "CRLF newline style should be preserved.");
        AssertTrue(text.Contains("\"ExcelPath\": \".config-sheet-forge/excel-cache/SkillsData.xlsx\""), "Existing path field naming should be preserved.");
        AssertTrue(!text.Contains("\"tableId\""), "Updater should not add a minimal tableId field to a schema that did not have one.");
        AssertTrue(text.IndexOf("SkillsData", StringComparison.Ordinal) < text.IndexOf("RoomData", StringComparison.Ordinal), "Updater should not reorder entries.");
        AssertTrue(text.Contains("\"keep\": \"yes\""), "Unrelated fields should stay untouched.");
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }
}

static void ProjectConfigUpsertWritesNestedFeishuNode()
{
    var json = JsonNode.Parse("""
    {
      "tables": [
        {
          "id": "SkillsData",
          "displayName": "Skills",
          "feishu": {
            "spreadsheetToken": "",
            "sheetId": "",
            "url": "",
            "branch": "",
            "profile": "",
            "wikiNodeToken": ""
          }
        }
      ]
    }
    """);
    var nested = typeof(ConfigSheetForge.Cli.Program).GetNestedType("CliLifecyclePlatform", System.Reflection.BindingFlags.NonPublic);
    var method = nested!.GetMethod("UpsertProjectConfigSheet", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    AssertTrue(method != null, "Expected project config upsert helper.");
    var changed = (bool)method!.Invoke(null, new object[]
    {
        json!,
        new SeedTableContract { TableId = "SkillsData", Branch = "feature/config", Profile = "feature/config" },
        new SeedOnlineSheetResult { SpreadsheetToken = "sht_123", SheetId = "sheet_1", SpreadsheetUrl = "https://example.feishu.cn/sheets/sht_123", WikiNodeToken = "wik_sheet" },
        new BranchWorkspaceContract { FeishuBranch = "feature/config", Profile = "feature/config", ExistingWikiNodeToken = "wik_branch" }
    })!;

    var text = json!.ToJsonString(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    AssertTrue(changed, "Nested feishu config should be updated.");
    AssertTrue(text.Contains("\"feishu\""), "Nested feishu object should remain.");
    AssertTrue(text.Contains("\"spreadsheetToken\":\"sht_123\""), "Spreadsheet token should be written inside feishu.");
    var tableObject = (JsonObject)((JsonArray)json!["tables"]!)[0]!;
    AssertTrue(!tableObject.Any(p => p.Key == "spreadsheetToken" || p.Key == "onlineSheetUrl"), "Updater should not add parallel top-level fields when feishu exists.");
}

static void ProjectConfigProbeReadsLifecycleSummary()
{
    var json = """
    {
      "schemaVersion": "example.config-source/v1",
      "lifecycleApplyMode": "dry-run-only",
      "toolkit": { "defaultGateReportPath": "Temp/ConfigSheetForge/pr-gate-report.json" },
      "adapterScript": "tools/config_bridge.py",
      "contractArgs": ["--config", "{projectConfig}", "--operation", "{operation}", "--out", "{request}"],
      "gitBranch": "feature/config",
      "profile": "feature-config",
      "tables": [
        { "id": "ItemsData" },
        { "id": "SkillsData" }
      ]
    }
    """;

    var summary = ProjectConfigProbe.ProbeJson("ProjectSettings/Example.ConfigSheetForge.json", json);

    AssertTrue(summary.Exists, "Project config should be marked as existing.");
    AssertEqual("example.config-source/v1", summary.SchemaVersion, "schemaVersion should be read.");
    AssertEqual("2", summary.TableCount.ToString(), "table count should be read from tables array.");
    AssertEqual("dry-run-only", summary.LifecycleApplyMode, "lifecycle mode should be read.");
    AssertEqual("Temp/ConfigSheetForge/pr-gate-report.json", summary.GateReportPath, "gate report path should be read.");
    AssertEqual("feature/config", summary.GitBranch, "git branch should be read.");
    AssertEqual("feature-config", summary.Profile, "profile should be read.");
    AssertEqual("tools/config_bridge.py", summary.AdapterScript, "adapter script should be read.");
    AssertEqual("6", summary.ContractArguments.Count.ToString(), "contract args should be read.");
    AssertTrue(summary.HasLifecycleAdapter, "adapterScript should enable project lifecycle mode.");
}

static void ProjectConfigProbeSkipsRegistryBaseTableIds()
{
    var json = """
    {
      "feishu": {
        "registryBase": {
          "tables": {
            "BranchBindings": "tbl_branch",
            "ConfigSheets": "tbl_config"
          }
        }
      },
      "tables": [
        { "id": "ItemsData", "displayName": "道具" },
        { "id": "SkillsData", "displayName": "技能" },
        { "id": "MonstersData", "displayName": "怪物" }
      ]
    }
    """;

    var summary = ProjectConfigProbe.ProbeJson("ProjectSettings/Example.ConfigSheetForge.json", json, "feature/config");

    AssertEqual("3", summary.TableCount.ToString(), "top-level tables array should win over feishu.registryBase.tables object.");
    AssertEqual("3", summary.CurrentBranchTableCount.ToString(), "current branch table count should use the shared top-level table list when no live ConfigSheets are present.");
}

static void ProjectConfigProbeDerivesBranchWorkspaceNames()
{
    var json = """
    {
      "branchWorkspace": {
        "rootWikiUrl": "https://example.feishu.cn/wiki/root_node",
        "profileNameTemplate": "{gitBranch}",
        "branchNodeTitleTemplate": "branch-{slug}"
      },
      "branchBindings": [
        {
          "gitBranch": "codex/config-sheet-seed-feishu-main",
          "profile": "codex/config-sheet-seed-feishu-main",
          "wikiNodeToken": "BuIEwbzcNiEYIfkj3Aac2kfOnvc"
        }
      ],
      "tables": [
        { "id": "ItemsData" }
      ]
    }
    """;

    var summary = ProjectConfigProbe.ProbeJson("ProjectSettings/Example.ConfigSheetForge.json", json, "codex/config-sheet-seed-feishu-main");

    AssertEqual("codex/config-sheet-seed-feishu-main", summary.Profile, "profile template should use the current git branch.");
    AssertEqual("branch-codex-config-sheet-seed-feishu-main", summary.BranchWikiNodeTitle, "branch node title should use the slug template.");
    AssertEqual("BuIEwbzcNiEYIfkj3Aac2kfOnvc", summary.BranchWikiNodeToken, "matching BranchBindings token should hydrate the branch node.");
    AssertEqual("https://example.feishu.cn/wiki/BuIEwbzcNiEYIfkj3Aac2kfOnvc", summary.BranchWikiNodeUrl, "wiki token should render as a URL when a root wiki URL provides the host.");
}

static void ProjectConfigProbeIgnoresLocalStateRegistry()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-probe-local-state-" + Guid.NewGuid().ToString("N"));
    try
    {
        var projectSettings = Path.Combine(root, "ProjectSettings");
        Directory.CreateDirectory(projectSettings);
        Directory.CreateDirectory(Path.Combine(root, ".config-sheet-forge"));
        var config = Path.Combine(projectSettings, "Example.ConfigSheetForge.json");
        File.WriteAllText(config, """
        {
          "branchWorkspace": {
            "profileNameTemplate": "{gitBranch}",
            "branchNodeTitleTemplate": "branch-{slug}"
          },
          "tables": [
            { "id": "ItemsData" },
            { "id": "SkillsData" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(root, ".config-sheet-forge", "registry.json"), """
        {
          "tables": [
            { "id": "old_smoke_table" }
          ]
        }
        """);

        var summary = ProjectConfigProbe.ProbeFile(config, "feature/live");

        AssertEqual("2", summary.TableCount.ToString(), "local ignored registry must not affect project table count.");
        AssertEqual("feature/live", summary.Profile, "local ignored registry must not affect derived profile.");
        AssertTrue(!summary.CurrentBranchTables.Any(t => t.TableId == "old_smoke_table"), "local ignored registry tables must not appear in current branch list.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ApplyContractPrGateReportWritesStandardReport()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-pr-gate-lifecycle-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
        var requestPath = Path.Combine(root, "request.json");
        var resultPath = Path.Combine(root, "Temp", "ConfigSheetForge", "unity-lifecycle", "pr-gate-report.result.json");
        var gateReportPath = Path.Combine("Temp", "ConfigSheetForge", "pr-gate-report.json");
        var request = new LifecycleContractRequest
        {
            Operation = "pr-gate-report",
            GateReportPath = gateReportPath,
            GateReport = new PrGateReport
            {
                GitHead = "abc123",
                Branch = "feature/config",
                MergeReview = new GateReviewState { Status = "approved" },
                PortableSubset = new GateCheckState { Passed = true },
                Triangulation = new GateCheckState { Passed = true },
                SchemaReview = new GateReviewState { Status = "approved" }
            }
        };
        File.WriteAllText(requestPath, JsonSerializer.Serialize(request));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "apply-contract", "--request", requestPath, "--out", resultPath });
        AssertEqual("0", exitCode.ToString(), "pr-gate-report lifecycle should pass.");

        var finalGateReport = Path.Combine(root, gateReportPath);
        AssertTrue(File.Exists(finalGateReport), "apply-contract should write the standard gate report path.");
        var gateJson = File.ReadAllText(finalGateReport);
        AssertTrue(gateJson.Contains("\"gitHead\""), "standard gate report should be a PrGateReport JSON object.");
        AssertTrue(!gateJson.Contains("\"prGateReport\""), "standard gate report should not wrap LifecycleContractResult.");

        var resultJson = File.ReadAllText(resultPath);
        AssertTrue(resultJson.Contains("\"prGateReport\""), "lifecycle result should still contain the nested report.");
        AssertTrue(resultJson.Contains("\"gateReportPath\""), "lifecycle result should record the final report path.");
    }
    finally
    {
        Directory.SetCurrentDirectory(old);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}

static async Task SeedDryRunPlansXlsxMigrationWithoutWrites()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-seed-dry-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        var xlsx = Path.Combine(root, "ItemsData.xlsx");
        CreateMinimalXlsx(xlsx, withMergedCells: false);
        var resultPath = Path.Combine(root, "seed.result.json");

        Directory.SetCurrentDirectory(root);
        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "seed-from-xlsx", "--table", "ItemsData", "--source-xlsx", xlsx, "--branch", "codex/config-sheet-seed-feishu-main", "--dry-run", "--out", resultPath });

        AssertEqual("0", exitCode.ToString(), "Seed dry-run should pass for a simple portable xlsx.");
        var resultJson = File.ReadAllText(resultPath);
        AssertTrue(resultJson.Contains("\"seedTables\""), "Result should expose per-table seed results for Unity.");
        AssertTrue(resultJson.Contains("branch-codex-config-sheet-seed-feishu-main"), "Dry-run should show the branch workspace node.");
        AssertTrue(resultJson.Contains("seed.sheet.import_or_create"), "Dry-run should plan online sheet create/import.");
        AssertTrue(resultJson.Contains("seed.cache.write_preview"), "Dry-run should plan cache write preview.");
        AssertTrue(!Directory.Exists(Path.Combine(root, ".config-sheet-forge", "cache")), "Dry-run must not write semantic cache.");
        AssertTrue(!Directory.Exists(Path.Combine(root, ".config-sheet-forge", "excel-cache")), "Dry-run must not write xlsx cache.");
    }
    finally
    {
        Directory.SetCurrentDirectory(old);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task SeedManifestDoesNotTreatCacheExcelPathAsSource()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-seed-manifest-source-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        var cache = Path.Combine(root, ".config-sheet-forge", "excel-cache", "ItemsData.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        CreateMinimalXlsx(cache, withMergedCells: false);
        var manifest = Path.Combine(root, "ProjectSettings", "Example.ConfigSheetForge.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        await File.WriteAllTextAsync(manifest, """
        {
          "tables": [
            { "id": "ItemsData", "displayName": "Items", "excelPath": ".config-sheet-forge/excel-cache/ItemsData.xlsx" }
          ]
        }
        """);

        Directory.SetCurrentDirectory(root);
        var resultPath = Path.Combine(root, "seed.result.json");
        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "seed-from-xlsx", "--all", "--manifest", manifest, "--dry-run", "--out", resultPath });
        AssertEqual("1", exitCode.ToString(), "Manifest excelPath is a cache path and must not be treated as source xlsx.");
        var resultJson = File.ReadAllText(resultPath);
        AssertTrue(resultJson.Contains("找不到本地 xlsx 源文件"), "Failure should ask for an explicit sourceXlsxPath.");
        AssertTrue(!Directory.Exists(Path.Combine(root, ".config-sheet-forge", "cache")), "Blocked manifest dry-run must not write semantic cache.");
    }
    finally
    {
        Directory.SetCurrentDirectory(old);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task SeedDryRunBlocksMergedCells()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-seed-merge-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        var xlsx = Path.Combine(root, "SkillsData.xlsx");
        CreateMinimalXlsx(xlsx, withMergedCells: true);
        var resultPath = Path.Combine(root, "seed.result.json");

        Directory.SetCurrentDirectory(root);
        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "seed-from-xlsx", "--table", "SkillsData", "--source-xlsx", xlsx, "--dry-run", "--out", resultPath });

        AssertEqual("1", exitCode.ToString(), "Merged cells should block seed dry-run.");
        var resultJson = File.ReadAllText(resultPath);
        AssertTrue(resultJson.Contains("B2:C2"), "Failure should include the merged range.");
        AssertTrue(resultJson.Contains("请取消合并"), "Failure should include a human-readable repair suggestion.");
        AssertTrue(!Directory.Exists(Path.Combine(root, ".config-sheet-forge", "cache")), "Blocked dry-run must not write semantic cache.");
    }
    finally
    {
        Directory.SetCurrentDirectory(old);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void SheetWriteRangeSupportsColumnsPastZ()
{
    var nested = typeof(ConfigSheetForge.Cli.Program).GetNestedType("CliLifecyclePlatform", System.Reflection.BindingFlags.NonPublic);
    AssertTrue(nested != null, "Expected CLI lifecycle platform type.");
    var method = nested!.GetMethod("BuildA1Range", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    AssertTrue(method != null, "Expected A1 range helper.");
    var range = (string)method!.Invoke(null, new object[] { "sheet123", 1, 1, 20, 29 })!;
    AssertEqual("sheet123!A1:AC20", range, "Range should be explicit rectangular A1 and support columns beyond Z.");
}

static void PortableSubsetBlocksUnsupportedStructures()
{
    var blocked = new[] { "formula", "floatingImage", "cellImage", "image", "mergedRange", "richText", "crossSheetReference", "mentionUser", "mentionDoc", "dateObject", "unsupportedCellType" };
    foreach (var key in blocked)
    {
        var workbook = SampleWorkbook();
        var cell = workbook.Sheets[0].Rows[0].Cells["name"];
        cell.Details[key] = key == "mergedRange" ? "B2:C2" : "true";
        cell.Details["sourceA1"] = "C12";
        var report = PortableStructureValidator.Validate(workbook);
        AssertTrue(report.HasErrors, key + " should fail portable structure validation.");
        AssertTrue(report.Findings[0].Message.Contains("Items!C12"), key + " should include a human-readable cell location.");
    }
}

static void TriangulationPassesAndFailsWithReadableDiffs()
{
    var online = SampleWorkbook();
    var exported = SampleWorkbook();
    exported.ProviderId = "xlsx";
    exported.SourceId = "temp.xlsx";
    exported.Sheets[0].Id = "different-sheet-id";
    exported.Sheets[0].Name = "导出表";
    exported.Sheets[0].Columns[0].DisplayName = "id";
    var normalized = SemanticTriangulator.Normalize(online);
    var pass = SemanticTriangulator.Compare(online, exported, normalized);
    AssertTrue(pass.Passed, "Identical semantic content should pass triangulation even when provider/source/sheet/display identities differ.");

    exported.Sheets[0].Rows[0].Cells["power"].NormalizedText = "999";
    var fail = SemanticTriangulator.Compare(online, exported, normalized);
    AssertTrue(!fail.Passed, "Changed xlsx semantic should fail triangulation.");
    AssertTrue(fail.DiffSummary.Any(d => d.Contains("item_sword") && d.Contains("power")), "Diff should identify the changed row and column.");
}

static async Task SyncLocalInputDoesNotRewriteUnchangedCache()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-sync-mtime-" + Guid.NewGuid().ToString("N"));
    try
    {
        var state = Path.Combine(temp, ".config-sheet-forge");
        Directory.CreateDirectory(state);
        await File.WriteAllTextAsync(Path.Combine(state, "config.json"), JsonSerializer.Serialize(new ForgeConfig(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var input = Path.Combine(temp, "items.semantic.json");
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(SampleWorkbook(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var oldDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(temp);
            var first = await ConfigSheetForge.Cli.Program.Main(new[] { "sync", "--input", input, "--table", "items" });
            AssertEqual("0", first.ToString(), "First sync should pass.");
            var semantic = Path.Combine(state, "cache", "items.semantic.json");
            var sha = Path.Combine(state, "cache", "items.sha256");
            var semanticTime = File.GetLastWriteTimeUtc(semantic);
            var shaTime = File.GetLastWriteTimeUtc(sha);
            await Task.Delay(1300);
            var second = await ConfigSheetForge.Cli.Program.Main(new[] { "sync", "--input", input, "--table", "items" });
            AssertEqual("0", second.ToString(), "Second sync should pass.");
            AssertEqual(semanticTime.Ticks.ToString(), File.GetLastWriteTimeUtc(semantic).Ticks.ToString(), "Unchanged semantic cache mtime should not change.");
            AssertEqual(shaTime.Ticks.ToString(), File.GetLastWriteTimeUtc(sha).Ticks.ToString(), "Unchanged sha mtime should not change.");
        }
        finally
        {
            Directory.SetCurrentDirectory(oldDir);
        }
    }
    finally
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

static async Task StrictBotModeDoesNotFallbackToUser()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-strict-bot-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var log = Path.Combine(temp, "calls.log");
    var script = Path.Combine(temp, "lark-cli.cmd");
    await File.WriteAllTextAsync(script,
        "@echo off\r\n" +
        "echo %*>>\"" + log + "\"\r\n" +
        "echo %* | find \"doctor\" >nul\r\n" +
        "if %ERRORLEVEL%==0 exit /b 0\r\n" +
        "echo %* | find \"--as bot\" >nul\r\n" +
        "if %ERRORLEVEL%==0 (\r\n" +
        "  echo missing_scope 1>&2\r\n" +
        "  exit /b 3\r\n" +
        ")\r\n" +
        "echo {\"data\":{\"values\":[[\"id\"],[\"a\"]]}}\r\n" +
        "exit /b 0\r\n");

    try
    {
        var provider = new LarkCliWorkbookProvider();
        var context = new ProviderContext { WorkspaceRoot = temp };
        context.Settings["larkCliPath"] = script;
        context.Settings["larkCliIdentity"] = "bot";
        var candidates = await provider.DiscoverRootsAsync(context, "配置根", CancellationToken.None);
        var calls = File.ReadAllText(log);
        AssertTrue(candidates.Any(c => c.Title == "Search failed"), "Bot permission failure should be returned to the caller.");
        AssertTrue(calls.Contains("--as bot"), "Provider should try bot identity.");
        AssertTrue(!calls.Contains("--as user"), "Strict bot mode must not fallback to user.");
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }
}

static async Task SeedLifecycleRejectsUserFallback()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-seed-strict-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(temp);
        var xlsx = Path.Combine(temp, "ItemsData.xlsx");
        CreateMinimalXlsx(xlsx, withMergedCells: false);
        Directory.SetCurrentDirectory(temp);
        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "seed-from-xlsx", "--table", "ItemsData", "--source-xlsx", xlsx, "--dry-run", "--allow-user-fallback" });
        AssertEqual("2", exitCode.ToString(), "Seed lifecycle should reject user fallback even when explicitly requested.");
    }
    finally
    {
        Directory.SetCurrentDirectory(old);
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

static void GateReportEvaluatorReportsHumanFailures()
{
    var missing = PrGateReportEvaluator.Evaluate(new PrGateReport());
    AssertTrue(missing.HumanReadableFailures.Any(f => f.Contains("gitHead")), "Missing gitHead should be reported.");

    var noPermission = PrGateReportEvaluator.Evaluate(new PrGateReport
    {
        GitHead = "abc",
        Branch = "feature/config",
        Permissions = new GatePermissions { CanReadRegistry = false, CanReadSheets = true },
        MergeReview = new GateReviewState { Status = "approved" },
        SchemaReview = new GateReviewState { Status = "approved" }
    });
    AssertTrue(noPermission.HumanReadableFailures.Any(f => f.Contains("Base 注册中心")), "Registry permission failure should be readable.");

    var schemaPending = PrGateReportEvaluator.Evaluate(new PrGateReport
    {
        GitHead = "abc",
        Branch = "feature/config",
        MergeReview = new GateReviewState { Status = "approved" },
        SchemaChangeDetected = true,
        SchemaReview = new GateReviewState { Status = "pending" }
    });
    AssertTrue(schemaPending.HumanReadableFailures.Any(f => f.Contains("Schema 审查")), "Pending schema review should block gate.");

    var bindingConflict = PrGateReportEvaluator.Evaluate(new PrGateReport
    {
        GitHead = "abc",
        Branch = "feature/config",
        BranchBinding = new GateReviewState { Status = "conflict", Message = "BranchBindings 中当前分支绑定冲突。" },
        MergeReview = new GateReviewState { Status = "approved" },
        SchemaReview = new GateReviewState { Status = "approved" }
    });
    AssertTrue(bindingConflict.HumanReadableFailures.Any(f => f.Contains("BranchBindings")), "Branch binding conflict should block gate with readable copy.");

    var expiredWaiver = PrGateReportEvaluator.Evaluate(new PrGateReport
    {
        GitHead = "abc",
        Branch = "feature/config",
        MergeReview = new GateReviewState { Status = "approved" },
        SchemaReview = new GateReviewState { Status = "approved" },
        Waiver = new GateWaiverState { Approved = true, ApprovedByRole = "designer", ExpiresAt = "2020-01-01T00:00:00Z", Branch = "feature/config", RecordId = "rec" }
    });
    AssertTrue(expiredWaiver.HumanReadableFailures.Any(f => f.Contains("配置负责人")), "Waiver must be approved by configOwner.");
    AssertTrue(expiredWaiver.HumanReadableFailures.Any(f => f.Contains("过期")), "Expired waiver should block gate.");
}

static void PowerShellJsonSafetyFlagsInlineJsonParams()
{
    AssertTrue(PowerShellJsonSafety.ShouldAvoidInlineJson("powershell", new[] { "wiki", "spaces", "get_node", "--params", "{\"token\":\"abc\"}" }), "PowerShell inline --params JSON should be flagged.");
    AssertTrue(!PowerShellJsonSafety.ShouldAvoidInlineJson("bash", new[] { "--params", "{\"token\":\"abc\"}" }), "Non-PowerShell shells should not be flagged by the Windows-specific guard.");
    AssertTrue(PowerShellJsonSafety.Recommendation().Contains("request 文件"), "Recommendation should point users toward file-based JSON.");
}

static LifecycleContractRequest SampleNewTableRequest(string settings)
{
    return new LifecycleContractRequest
    {
        Operation = "new-table",
        Registry = new RegistryContract { BaseToken = "base", BaseUrl = "https://example.feishu.cn/base/base" },
        Git = new ContractGitSpec { Branch = "feature/config", FeishuBranch = "feature/config", Head = "abc123" },
        Table = new ContractTableSpec
        {
            TableId = "items",
            DisplayName = "道具",
            ExcelPath = "Assets/Config/Items.xlsx",
            WikiRootToken = "wik_root",
            OwnerRole = "configOwner",
            Fields =
            {
                new ContractFieldSpec { Key = "id", DisplayName = "ID", ValueKind = "string", Description = "稳定ID" },
                new ContractFieldSpec { Key = "power", DisplayName = "战力", ValueKind = "integer", Description = "战斗力" }
            }
        },
        UnityExcelToSo = new UnityExcelToSoContract
        {
            SettingsPath = settings,
            TableId = "items",
            ExcelPath = "Assets/Config/Items.xlsx",
            ScriptableObjectType = "ItemConfig"
        }
    };
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

static void CreateMinimalXlsx(string path, bool withMergedCells)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    AddZipText(archive, "[Content_Types].xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """);
    AddZipText(archive, "_rels/.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """);
    AddZipText(archive, "xl/workbook.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Data" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """);
    AddZipText(archive, "xl/_rels/workbook.xml.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """);

    var merge = withMergedCells ? "<mergeCells count=\"1\"><mergeCell ref=\"B2:C2\"/></mergeCells>" : "";
    AddZipText(archive, "xl/worksheets/sheet1.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1" t="inlineStr"><is><t>id</t></is></c>
              <c r="B1" t="inlineStr"><is><t>name</t></is></c>
            </row>
            <row r="2">
              <c r="A2" t="inlineStr"><is><t>item_001</t></is></c>
              <c r="B2" t="inlineStr"><is><t>Sword</t></is></c>
            </row>
          </sheetData>
        """ + merge + "</worksheet>");
}

static void AddZipText(ZipArchive archive, string path, string text)
{
    var entry = archive.CreateEntry(path);
    using var writer = new StreamWriter(entry.Open());
    writer.Write(text.Trim());
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

sealed class RecordingLifecyclePlatform : ILifecyclePlatform
{
    public List<string> Calls { get; } = new();

    public Task<RegistrySnapshot> GetRegistrySnapshotAsync(RegistryContract registry, CancellationToken cancellationToken)
    {
        Calls.Add("snapshot");
        return Task.FromResult(new RegistrySnapshot());
    }

    public Task<LifecycleActionResult> EnsureRegistryAsync(RegistryContract registry, RegistryDisplayMapping mapping, CancellationToken cancellationToken)
    {
        Calls.Add("ensure-registry");
        return Task.FromResult(new LifecycleActionResult { Action = "registry.ensure", Status = "done", Message = "mock registry ensured" });
    }

    public Task<SheetCreationResult> CreateOnlineSheetAsync(ContractTableSpec table, CancellationToken cancellationToken)
    {
        Calls.Add("create-sheet");
        return Task.FromResult(new SheetCreationResult
        {
            SpreadsheetToken = "sht_mock",
            SpreadsheetUrl = "https://example.feishu.cn/sheets/sht_mock",
            SheetId = "sheet_mock",
            WikiNodeToken = "wik_mock"
        });
    }

    public Task<LifecycleActionResult> WriteSheetTemplateAsync(SheetCreationResult sheet, IList<IList<string>> templateRows, CancellationToken cancellationToken)
    {
        Calls.Add("write-template");
        var action = new LifecycleActionResult { Action = "sheet.template.write", Status = "done", Message = "mock template written" };
        action.Details["rows"] = templateRows.Count.ToString();
        return Task.FromResult(action);
    }

    public Task<LifecycleActionResult> UpsertRegistryRecordAsync(RegistryContract registry, ContractTableSpec table, SheetCreationResult sheet, CancellationToken cancellationToken)
    {
        Calls.Add("upsert-registry");
        var action = new LifecycleActionResult { Action = "registry.config_sheets.upsert", Status = "done", Message = "mock registry upsert" };
        action.Details["recordId"] = "rec_config";
        return Task.FromResult(action);
    }

    public Task<LifecycleActionResult> UpsertSchemaReviewAsync(RegistryContract registry, ContractTableSpec table, ContractGitSpec git, string reason, CancellationToken cancellationToken)
    {
        Calls.Add("upsert-schema");
        var action = new LifecycleActionResult { Action = "registry.schema_reviews.upsert", Status = "done", Message = "mock schema review" };
        action.Details["schemaReviewId"] = "schema_pending";
        action.Details["reason"] = reason;
        return Task.FromResult(action);
    }

    public Task<LifecycleActionResult> ApplyRegistryMigrationAsync(RegistryContract registry, RegistryMigrationPlan plan, CancellationToken cancellationToken)
    {
        Calls.Add("apply-migration");
        return Task.FromResult(new LifecycleActionResult { Action = "registry.migration.apply", Status = "done", Message = "mock migration" });
    }
}
