using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.IO.Compression;
using System.Reflection;
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
    ("lark cli discovery accepts config sheet forge env", () => RunSync(LarkCliDiscoveryAcceptsConfigSheetForgeEnv)),
    ("type row import normalizes semantic values", () => RunSync(TypeRowImportNormalizesSemanticValues)),
    ("enum option drift is reported", () => RunSync(EnumOptionDriftIsReported)),
    ("lark read parser accepts wrapped values", () => RunSync(LarkReadParserAcceptsWrappedValues)),
    ("lark read parser accepts record arrays", () => RunSync(LarkReadParserAcceptsRecordArrays)),
    ("lark read parser matches object keys case-insensitively", () => RunSync(LarkReadParserMatchesObjectKeysCaseInsensitively)),
    ("datetime normalization does not assume local timezone", () => RunSync(DateTimeNormalizationDoesNotAssumeLocalTimezone)),
    ("registry localization keeps machine keys", () => RunSync(RegistryLocalizationKeepsMachineKeys)),
    ("registry migration cleans default rows and fields", () => RunSync(RegistryMigrationCleansDefaults)),
    ("registry migration detects governance status options", () => RunSync(RegistryMigrationDetectsGovernanceStatusOptions)),
    ("registry migration status option plan is idempotent", () => RunSync(RegistryMigrationStatusOptionPlanIsIdempotent)),
    ("registry migration review-status-only narrows actions", () => RunSync(RegistryMigrationReviewStatusOnlyNarrowsActions)),
    ("lark base matrix record lookup finds existing record", () => RunSync(LarkBaseMatrixRecordLookupFindsExistingRecord)),
    ("lark 1.0.40 registry fixture hydrates and reports duplicate bindings", Lark140RegistryFixtureHydratesAndReportsDuplicateBindings),
    ("registry migrate keeps table and field list argv compatible", RegistryMigrateKeepsTableAndFieldListArgvCompatible),
    ("registry migrate review-status-only apply skips schema cleanup", RegistryMigrateReviewStatusOnlyApplySkipsSchemaCleanup),
    ("registry migration reports branch binding cleanup risks", () => RunSync(RegistryMigrationReportsBranchBindingCleanupRisks)),
    ("branch workspace resolver creates stable slugs", () => RunSync(BranchWorkspaceResolverCreatesStableSlugs)),
    ("branch binding one-to-one conflict blocks lifecycle", BranchBindingOneToOneConflictBlocksLifecycle),
    ("duplicate branch bindings block lifecycle with record ids", DuplicateBranchBindingsBlockLifecycleWithRecordIds),
    ("sync-cache hydrates branch workspace from registry snapshot", SyncCacheHydratesBranchWorkspaceFromRegistrySnapshot),
    ("sync-cache exposes live registry branch status", SyncCacheExposesLiveRegistryBranchStatus),
    ("project config probe trusts live registry locators over empty project settings", () => RunSync(ProjectConfigProbeTrustsLiveRegistryLocatorsOverEmptyProjectSettings)),
    ("sync-cache dry-run summary drives next action", () => RunSync(SyncCacheDryRunSummaryDrivesNextAction)),
    ("sync-cache lifecycle result mirrors summary to top level", () => RunSync(SyncCacheLifecycleResultMirrorsSummaryToTopLevel)),
    ("sync-cache emit output mirrors summary to top level", () => RunSync(SyncCacheEmitOutputMirrorsSummaryToTopLevel)),
    ("sync-status inspects local cache without online reads", SyncStatusInspectsLocalCacheWithoutOnlineReads),
    ("current branch bootstrap from target plans instead of seed", CurrentBranchBootstrapFromTargetPlansInsteadOfSeed),
    ("compare-merge dry-run hydrates workspaces and table scope", CompareMergeDryRunHydratesWorkspacesAndTableScope),
    ("compare-merge dry-run fails without target workspace", CompareMergeDryRunFailsWithoutTargetWorkspace),
    ("compare-merge dry-run fingerprints merge review input", CompareMergeDryRunFingerprintsMergeReviewInput),
    ("review status normalizes feishu single select values", () => RunSync(ReviewStatusNormalizesFeishuSingleSelectValues)),
    ("pr gate hydrates live merge review records", PrGateHydratesLiveMergeReviewRecords),
    ("pr gate hydrates json array merge review status", PrGateHydratesJsonArrayMergeReviewStatus),
    ("lark record id parser accepts nested upsert result", () => RunSync(LarkRecordIdParserAcceptsNestedUpsertResult)),
    ("submit-merge-review apply blocks missing status options", SubmitMergeReviewApplyBlocksMissingStatusOptions),
    ("submit-merge-review apply returns nested record id", SubmitMergeReviewApplyReturnsNestedRecordId),
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
    ("project config probe reads documentation targets", () => RunSync(ProjectConfigProbeReadsDocumentationTargets)),
    ("project config probe reads roles and new table options", () => RunSync(ProjectConfigProbeReadsRolesAndNewTableOptions)),
    ("project config probe reads unity excel to so defaults", () => RunSync(ProjectConfigProbeReadsUnityExcelToSoDefaults)),
    ("project config probe ignores local state registry", () => RunSync(ProjectConfigProbeIgnoresLocalStateRegistry)),
    ("apply-contract pr-gate-report writes standard report", ApplyContractPrGateReportWritesStandardReport),
    ("cli machine json outputs are utf8 without bom", CliMachineJsonOutputsAreUtf8WithoutBom),
    ("cli normalizes verbatim out and request paths", CliNormalizesVerbatimOutAndRequestPaths),
    ("apply-contract sync-cache apply requires confirmation", ApplyContractSyncCacheApplyRequiresConfirmation),
    ("sync-cache apply requires preview result", SyncCacheApplyRequiresPreviewResult),
    ("seed dry-run plans xlsx migration without writes", SeedDryRunPlansXlsxMigrationWithoutWrites),
    ("target branch bootstrap dry-run overrides seed target", TargetBranchBootstrapDryRunOverridesSeedTarget),
    ("target branch bootstrap safe apply skips all local writes", TargetBranchBootstrapSafeApplySkipsAllLocalWrites),
    ("target branch bootstrap apply skips project config without confirmation", TargetBranchBootstrapSkipsProjectConfigWithoutConfirmation),
    ("target branch bootstrap apply skips excel to so without confirmation", TargetBranchBootstrapSkipsExcelToSoWithoutConfirmation),
    ("target branch bootstrap repeated apply reuses online sheets", TargetBranchBootstrapRepeatedApplyReusesOnlineSheets),
    ("target branch bootstrap postflight failure blocks apply result", TargetBranchBootstrapPostflightFailureBlocksApplyResult),
    ("apply-contract target branch bootstrap requires preview result", ApplyContractTargetBranchBootstrapRequiresPreviewResult),
    ("apply-contract target branch bootstrap rejects preview fingerprint mismatch", ApplyContractTargetBranchBootstrapRejectsPreviewFingerprintMismatch),
    ("apply-contract current branch bootstrap requires preview result", ApplyContractCurrentBranchBootstrapRequiresPreviewResult),
    ("seed manifest does not treat cache excelPath as source", SeedManifestDoesNotTreatCacheExcelPathAsSource),
    ("seed dry-run blocks merged xlsx cells", SeedDryRunBlocksMergedCells),
    ("sheet write ranges support columns past z", () => RunSync(SheetWriteRangeSupportsColumnsPastZ)),
    ("portable subset blocks unsupported structures", () => RunSync(PortableSubsetBlocksUnsupportedStructures)),
    ("triangulation passes and fails with readable diffs", () => RunSync(TriangulationPassesAndFailsWithReadableDiffs)),
    ("triangulation reports right-side extra shape", () => RunSync(TriangulationReportsRightSideExtraShape)),
    ("xlsx dimension a1 uses sheet data used range", () => RunSync(XlsxDimensionA1UsesSheetDataUsedRange)),
    ("lark read wrong startRange retries explicit range", LarkReadWrongStartRangeRetriesExplicitRange),
    ("lark read uses xlsx sheet data range when dimension is stale", LarkReadUsesXlsxSheetDataRangeWhenDimensionIsStale),
    ("excel to so cache dialect maps portable primitive aliases", () => RunSync(ExcelToSoCacheDialectMapsPortablePrimitiveAliases)),
    ("excel to so cache dialect restores json arrays from source xlsx", () => RunSync(ExcelToSoCacheDialectRestoresJsonArraysFromSourceXlsx)),
    ("excel to so cache dialect restores json with inferred type row", () => RunSync(ExcelToSoCacheDialectRestoresJsonWithInferredTypeRow)),
    ("excel to so cache dialect blocks unresolved json", () => RunSync(ExcelToSoCacheDialectBlocksUnresolvedJson)),
    ("repair-cache-dialect rewrites xlsx type row offline", RepairCacheDialectRewritesXlsxTypeRowOffline),
    ("repair-cache-dialect scans stale dimension right-side columns", RepairCacheDialectScansStaleDimensionRightSideColumns),
    ("sync local input does not rewrite unchanged cache", SyncLocalInputDoesNotRewriteUnchangedCache),
    ("strict bot mode does not fallback to user", StrictBotModeDoesNotFallbackToUser),
    ("seed lifecycle rejects user fallback", SeedLifecycleRejectsUserFallback),
    ("gate report evaluator reports human failures", () => RunSync(GateReportEvaluatorReportsHumanFailures)),
    ("gate report evaluator marks valid waiver as waived", () => RunSync(GateReportEvaluatorMarksValidWaiverAsWaived)),
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

static JsonSerializerOptions CamelJsonOptions()
{
    return new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
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
    var oldForgeEnv = Environment.GetEnvironmentVariable("CONFIG_SHEET_FORGE_LARK_CLI");
    try
    {
        Environment.SetEnvironmentVariable("LARK_CLI_PATH", null);
        Environment.SetEnvironmentVariable("CONFIG_SHEET_FORGE_LARK_CLI", null);
        Environment.SetEnvironmentVariable("PATH", temp + Path.PathSeparator + oldPath);
        var resolved = LarkCliDiscovery.Resolve("lark-cli");
        AssertEqual(Path.GetFullPath(ps1Shim), Path.GetFullPath(resolved.DisplayPath), "Discovery should prefer the .ps1 shim on Windows.");
        AssertTrue(resolved.Source.Contains(":ps1"), "Discovery should report the ps1 launcher source.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("PATH", oldPath);
        Environment.SetEnvironmentVariable("LARK_CLI_PATH", oldEnv);
        Environment.SetEnvironmentVariable("CONFIG_SHEET_FORGE_LARK_CLI", oldForgeEnv);
        Directory.Delete(temp, recursive: true);
    }
}

static void LarkCliDiscoveryAcceptsConfigSheetForgeEnv()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-lark-env-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var ps1Shim = Path.Combine(temp, "lark-cli.ps1");
    File.WriteAllText(ps1Shim, "exit 0\r\n");

    var oldEnv = Environment.GetEnvironmentVariable("CONFIG_SHEET_FORGE_LARK_CLI");
    var oldLarkEnv = Environment.GetEnvironmentVariable("LARK_CLI_PATH");
    try
    {
        Environment.SetEnvironmentVariable("CONFIG_SHEET_FORGE_LARK_CLI", ps1Shim);
        Environment.SetEnvironmentVariable("LARK_CLI_PATH", null);
        var resolved = LarkCliDiscovery.Resolve("lark-cli");
        AssertEqual(Path.GetFullPath(ps1Shim), Path.GetFullPath(resolved.DisplayPath), "Discovery should honor CONFIG_SHEET_FORGE_LARK_CLI.");
        AssertTrue(resolved.Source.Contains("CONFIG_SHEET_FORGE_LARK_CLI"), "Discovery should report the config-sheet-forge env source.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CONFIG_SHEET_FORGE_LARK_CLI", oldEnv);
        Environment.SetEnvironmentVariable("LARK_CLI_PATH", oldLarkEnv);
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

static void RegistryMigrationDetectsGovernanceStatusOptions()
{
    var snapshot = new RegistrySnapshot();
    snapshot.Tables.Add(BuildGovernanceTable("MergeReviews", "合并审查", "tbl_merge", Array.Empty<string>()));
    snapshot.Tables.Add(BuildGovernanceTable("SchemaReviews", "Schema 审查", "tbl_schema", new[] { "approved" }));
    snapshot.Tables.Add(BuildGovernanceTable("Waivers", "同步豁免", "tbl_waiver", new[] { "approved", "completed" }));

    var plan = RegistryMigrator.Plan(snapshot, new RegistryMigrationOptions { Locale = "zh-Hans" });
    var optionActions = plan.Actions.Where(a => a.Action == "registry.field.options.ensure").ToList();
    AssertTrue(optionActions.Any(a => a.Details["tableMachineKey"] == "MergeReviews" && a.Details["missingOptions"].Contains("approved")), "MergeReviews empty select options should be planned.");
    AssertTrue(optionActions.Any(a => a.Details["tableMachineKey"] == "SchemaReviews" && a.Details["missingOptions"].Contains("pending")), "SchemaReviews missing pending should be planned.");
    AssertTrue(optionActions.Any(a => a.Details["tableMachineKey"] == "Waivers" && a.Details["missingOptions"].Contains("expired")), "Waivers missing expired should be planned.");
}

static void RegistryMigrationStatusOptionPlanIsIdempotent()
{
    var snapshot = new RegistrySnapshot();
    snapshot.Tables.Add(BuildGovernanceTable("MergeReviews", "合并审查", "tbl_merge", new[] { "approved", "completed", "passed" }));
    snapshot.Tables.Add(BuildGovernanceTable("SchemaReviews", "Schema 审查", "tbl_schema", new[] { "pending", "approved", "completed", "passed", "rejected" }));
    snapshot.Tables.Add(BuildGovernanceTable("Waivers", "同步豁免", "tbl_waiver", new[] { "approved", "completed", "passed", "rejected", "expired" }));

    var plan = RegistryMigrator.Plan(snapshot, new RegistryMigrationOptions { Locale = "zh-Hans" });
    AssertTrue(!plan.Actions.Any(a => a.Action == "registry.field.options.ensure"), "registry-migrate should be idempotent when governance status options already exist.");
}

static void RegistryMigrationReviewStatusOnlyNarrowsActions()
{
    var snapshot = new RegistrySnapshot();
    var merge = BuildGovernanceTable("MergeReviews", "MergeReviews", "tbl_merge", Array.Empty<string>());
    merge.Fields.Add(new RegistryFieldSnapshot { MachineKey = "GitBranch", FieldId = "fld_branch", DisplayName = "旧Git分支", Type = "text" });
    merge.Fields.Add(new RegistryFieldSnapshot { MachineKey = "GitBranch", FieldId = "fld_branch_dup", DisplayName = "Git分支", Type = "text" });
    snapshot.Tables.Add(merge);
    snapshot.Tables.Add(new RegistryTableSnapshot
    {
        MachineKey = "SchemaReviews",
        DisplayName = "SchemaReviews",
        TableId = "tbl_schema",
        Fields =
        {
            new RegistryFieldSnapshot { MachineKey = "Status", FieldId = "fld_schema_status", DisplayName = "状态", Type = "text" }
        }
    });
    snapshot.Tables.Add(BuildGovernanceTable("Waivers", "Waivers", "tbl_waiver", new[] { "approved" }));

    var plan = RegistryMigrator.Plan(snapshot, new RegistryMigrationOptions { Locale = "zh-Hans", Only = "review-status-options", CleanupDefaultFields = true, CleanupDefaultRows = true });
    AssertTrue(plan.Actions.Count > 0, "narrow migration should still report governance status actions.");
    AssertTrue(plan.Actions.All(a => a.Action == "registry.field.options.ensure" || a.Action == "registry.field.status_select_mismatch"), "narrow migration must not include field ensure/rename/ambiguous cleanup.");
    AssertTrue(plan.Actions.Any(a => a.Action == "registry.field.options.ensure" && a.Details["tableMachineKey"] == "MergeReviews"), "narrow migration should include MergeReviews status options.");
    AssertTrue(plan.Actions.Any(a => a.Action == "registry.field.options.ensure" && a.Details["tableMachineKey"] == "Waivers"), "narrow migration should include Waivers status options.");
    AssertTrue(plan.Actions.Any(a => a.Action == "registry.field.status_select_mismatch" && a.Details["tableMachineKey"] == "SchemaReviews"), "narrow migration should warn when SchemaReviews status is not select.");
}

static RegistryTableSnapshot BuildGovernanceTable(string machineKey, string displayName, string tableId, IEnumerable<string> options)
{
    return new RegistryTableSnapshot
    {
        MachineKey = machineKey,
        DisplayName = displayName,
        TableId = tableId,
        Fields =
        {
            new RegistryFieldSnapshot
            {
                MachineKey = "Status",
                FieldId = "fld_status_" + machineKey,
                DisplayName = "状态",
                Type = "single_select",
                Options = options.ToList()
            }
        }
    };
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

static async Task RegistryMigrateReviewStatusOnlyApplySkipsSchemaCleanup()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-registry-status-only-" + Guid.NewGuid().ToString("N"));
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
        "  echo {\"ok\":true,\"data\":{\"tables\":[{\"id\":\"tbl_merge\",\"name\":\"MergeReviews\"},{\"id\":\"tbl_schema\",\"name\":\"SchemaReviews\"}],\"total\":2}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +field-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo %* | find \"tbl_merge\" >nul\r\n" +
        "  if not errorlevel 1 (\r\n" +
        "    echo {\"ok\":true,\"data\":{\"fields\":[{\"id\":\"fld_merge_status\",\"name\":\"Status\",\"type\":\"single_select\",\"options\":[]},{\"id\":\"fld_old_branch\",\"name\":\"旧Git分支\",\"type\":\"text\"},{\"id\":\"fld_branch\",\"name\":\"GitBranch\",\"type\":\"text\"}]}}\r\n" +
        "    exit /b 0\r\n" +
        "  )\r\n" +
        "  echo {\"ok\":true,\"data\":{\"fields\":[{\"id\":\"fld_schema_status\",\"name\":\"Status\",\"type\":\"text\"}]}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +record-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true,\"data\":{\"fields\":[\"Status\"],\"data\":[],\"record_id_list\":[]}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +field-update\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo %* | find \"fld_schema_status\" >nul\r\n" +
        "  if not errorlevel 1 (\r\n" +
        "    echo schema status must not be converted 1>&2\r\n" +
        "    exit /b 3\r\n" +
        "  )\r\n" +
        "  echo {\"ok\":true,\"updated\":true,\"field\":{\"field_id\":\"fld_merge_status\"}}\r\n" +
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
            "--only", "review-status-options",
            "--locale", "zh-Hans",
            "--yes",
            "--lark-cli", script,
            "--out", output
        });
        AssertEqual("0", exitCode.ToString(), "review-status-options apply should succeed without trying schema type conversion.");
        var calls = File.ReadAllText(log);
        AssertTrue(calls.Contains("base +field-update") && calls.Contains("fld_merge_status"), "narrow apply should update MergeReviews status options.");
        AssertTrue(!calls.Contains("base +table-update"), "narrow apply must not rename tables.");
        AssertTrue(!calls.Contains("fld_old_branch --yes"), "narrow apply must not rename unrelated fields.");
        AssertTrue(!calls.Contains("fld_schema_status --yes"), "SchemaReviews text status must not be auto-converted.");
        var resultJson = await File.ReadAllTextAsync(output);
        AssertTrue(resultJson.Contains("registry.field.options.ensure"), "result should include options ensure action.");
        AssertTrue(resultJson.Contains("registry.field.status_select_mismatch"), "result should include schema status mismatch warning.");
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

static async Task SyncCacheExposesLiveRegistryBranchStatus()
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

    request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract { TableId = "ItemsData", DisplayName = "Items" });
    request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract { TableId = "SkillsData", DisplayName = "Skills" });
    var snapshot = SampleBranchStatusSnapshot();
    ConfigSheetForge.Cli.Program.HydrateSyncCacheRequestFromRegistrySnapshot(request, snapshot, "");

    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);

    AssertTrue(result.Success, "Live BranchBindings + ConfigSheets should make sync-cache preview valid even when ProjectSettings locators are empty.");
    AssertEqual("ok", result.BranchStatus.BranchBindingStatus, "Branch status should be ok.");
    AssertEqual("2", result.BranchStatus.TableCountRegistered.ToString(), "Branch status should count registered online tables.");
    AssertEqual("2", result.ResolvedOnlineTables.Count.ToString(), "Result should expose resolvedOnlineTables for Unity UI.");
    AssertTrue(result.SeedTables.Any(t => t.TableId == "ItemsData" && t.SpreadsheetToken == "sht_items" && t.SheetId == "sheet_items"), "Result seedTables should carry hydrated live locators.");
}

static void ProjectConfigProbeTrustsLiveRegistryLocatorsOverEmptyProjectSettings()
{
    var json = """
{
  "git": { "branch": "feature/config", "profile": "feature-config" },
  "branchWorkspace": {
    "profileNameTemplate": "{gitBranch}",
    "branchNodeTitleTemplate": "branch-{slug}",
    "mainGitBranch": "main",
    "mainFeishuBranch": "main"
  },
  "tables": [
    { "tableId": "ItemsData", "displayName": "Items", "profile": "feature-config" },
    { "tableId": "SkillsData", "displayName": "Skills", "profile": "feature-config" }
  ],
  "branchStatus": {
    "currentGitBranch": "feature/config",
    "currentProfile": "feature-config",
    "branchBindingStatus": "ok",
    "branchWikiNodeToken": "wik_feature",
    "branchWikiNodeUrl": "https://example.feishu.cn/wiki/wik_feature",
    "tableCountExpected": 2,
    "tableCountRegistered": 2,
    "missingTables": [],
    "missingLocators": [],
    "resolvedOnlineTables": [
      { "tableId": "ItemsData", "displayName": "Items", "profile": "feature-config", "spreadsheetToken": "sht_items", "sheetId": "sheet_items", "onlineSheetUrl": "https://example.feishu.cn/sheets/sht_items" },
      { "tableId": "SkillsData", "displayName": "Skills", "profile": "feature-config", "spreadsheetToken": "sht_skills", "sheetId": "sheet_skills", "onlineSheetUrl": "https://example.feishu.cn/sheets/sht_skills" }
    ]
  }
}
""";
    var summary = ProjectConfigProbe.ProbeJson("Temp/registry-status.result.json", json, "feature/config");

    AssertEqual("2", summary.CurrentBranchTableCount.ToString(), "Live registry tables should override empty ProjectSettings locators.");
    AssertEqual("live-registry", summary.CurrentBranchTableSource, "Current branch table source should be live registry.");
    AssertTrue(summary.CurrentBranchTables.All(t => string.IsNullOrWhiteSpace(t.BlockingReason)), "Hydrated live tables should not be marked as missing online locators.");
    AssertEqual("ok", summary.LiveBranchBindingStatus, "Probe should read branch status.");
}

static void SyncCacheDryRunSummaryDrivesNextAction()
{
    var json = """
{
  "operation": "sync-cache",
  "dryRun": true,
  "success": true,
  "branchWorkspace": { "wikiNodeToken": "wik_feature", "wikiNodeUrl": "https://example.feishu.cn/wiki/wik_feature", "nodeTitle": "branch-feature-config", "profile": "feature-config" },
  "branchStatus": {
    "currentGitBranch": "feature/config",
    "currentProfile": "feature-config",
    "branchBindingStatus": "ok",
    "tableCountExpected": 1,
    "tableCountRegistered": 1,
    "missingTables": [],
    "missingLocators": [],
    "resolvedOnlineTables": [
      { "tableId": "ItemsData", "profile": "feature-config", "spreadsheetToken": "sht_items", "sheetId": "sheet_items" }
    ]
  },
  "syncCacheSummary": {
    "cacheStatus": "upToDate",
    "upToDateTables": [ "ItemsData" ],
    "changedTables": [],
    "missingCacheTables": [],
    "blockedTables": []
  }
}
""";
    var summary = ProjectConfigProbe.ProbeJson("Temp/sync-cache.result.json", json, "feature/config");
    AssertEqual("upToDate", summary.SyncCacheStatus, "Probe should read sync cache status.");
    AssertEqual("1", summary.SyncCacheUpToDateTables.Count.ToString(), "Probe should read up-to-date tables.");
    AssertTrue(summary.LiveMissingTables.Count == 0 && summary.LiveMissingLocators.Count == 0, "Live status should not invent missing tables.");
}

static void SyncCacheLifecycleResultMirrorsSummaryToTopLevel()
{
    var result = new LifecycleContractResult
    {
        Operation = "sync-cache",
        DryRun = true,
        Success = true,
        RequestFingerprint = "fp-sync"
    };
    var summary = new SyncCacheSummary
    {
        CacheStatus = "needsUpdate",
        PreviewFingerprint = "fp-sync",
        CanApplyCache = true,
        NextAction = "write-cache",
        TableCount = 2
    };
    summary.ChangedTables.AddRange(new[] { "ItemsData", "SkillsData" });
    summary.Tables.Add(new SyncTableCacheStatus { TableId = "ItemsData", DisplayName = "Items", CacheStatus = "needsUpdate", NeedsWriteCache = true });
    summary.Tables.Add(new SyncTableCacheStatus { TableId = "SkillsData", DisplayName = "Skills", CacheStatus = "needsUpdate", NeedsWriteCache = true });

    var method = typeof(ConfigSheetForge.Cli.Program).GetMethod("ApplySyncCacheSummary", BindingFlags.NonPublic | BindingFlags.Static);
    AssertTrue(method != null, "ApplySyncCacheSummary should remain available for lifecycle contract tests.");
    method!.Invoke(null, new object[] { result, summary });

    AssertEqual("config-sheet-forge.lifecycle/v1", result.SchemaVersion, "Lifecycle result should declare schema version.");
    AssertEqual("needsUpdate", result.CacheStatus, "Top-level cacheStatus should mirror syncCacheSummary.");
    AssertEqual("true", result.CanApplyCache.ToString().ToLowerInvariant(), "Top-level canApplyCache should mirror syncCacheSummary.");
    AssertEqual("write-cache", result.NextAction, "Top-level nextAction should mirror syncCacheSummary.");
    AssertEqual("2", result.ChangedTables.Count.ToString(), "Top-level changedTables should mirror syncCacheSummary.");
    AssertEqual("2", result.Tables.Count.ToString(), "Top-level tables should mirror syncCacheSummary.");
}

static void SyncCacheEmitOutputMirrorsSummaryToTopLevel()
{
    var result = new LifecycleContractResult
    {
        Operation = "sync-cache",
        DryRun = true,
        Success = true,
        RequestFingerprint = "fp-emit",
        SyncCacheSummary = new SyncCacheSummary
        {
            CacheStatus = "needsUpdate",
            CanApplyCache = true,
            NextAction = "write-cache",
            TableCount = 1
        }
    };
    result.SyncCacheSummary.ChangedTables.Add("ItemsData");
    result.SyncCacheSummary.Tables.Add(new SyncTableCacheStatus
    {
        TableId = "ItemsData",
        DisplayName = "Items",
        CacheStatus = "needsUpdate",
        NeedsWriteCache = true
    });

    var method = typeof(ConfigSheetForge.Cli.Program).GetMethod("PrepareLifecycleResultForOutput", BindingFlags.NonPublic | BindingFlags.Static);
    AssertTrue(method != null, "PrepareLifecycleResultForOutput should mirror syncCacheSummary before writing JSON.");
    method!.Invoke(null, new object[] { result });

    AssertEqual("fp-emit", result.PreviewFingerprint, "Emit preparation should preserve preview fingerprint fallback.");
    AssertEqual("needsUpdate", result.CacheStatus, "Emit preparation should mirror cacheStatus.");
    AssertEqual("true", result.CanApplyCache.ToString().ToLowerInvariant(), "Emit preparation should mirror canApplyCache.");
    AssertEqual("write-cache", result.NextAction, "Emit preparation should mirror nextAction.");
    AssertEqual("1", result.ChangedTables.Count.ToString(), "Emit preparation should mirror changedTables.");
    AssertEqual("1", result.Tables.Count.ToString(), "Emit preparation should mirror tables.");
}

static async Task SyncStatusInspectsLocalCacheWithoutOnlineReads()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-sync-status-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
        var cacheDir = Path.Combine(root, ".config-sheet-forge", "cache");
        var excelDir = Path.Combine(root, ".config-sheet-forge", "excel-cache");
        Directory.CreateDirectory(cacheDir);
        Directory.CreateDirectory(excelDir);
        File.WriteAllText(Path.Combine(cacheDir, "ItemsData.semantic.json"), "{\"tableId\":\"ItemsData\"}");
        File.WriteAllText(Path.Combine(cacheDir, "ItemsData.sha256"), "hash_items");
        File.WriteAllText(Path.Combine(excelDir, "ItemsData.xlsx"), "placeholder");

        var request = new LifecycleContractRequest
        {
            Operation = "sync-status",
            DryRun = true,
            Git = new ContractGitSpec { Branch = "feature/config", Profile = "feature-config" },
            BranchWorkspace = new BranchWorkspaceContract
            {
                GitBranch = "feature/config",
                Profile = "feature-config",
                ExistingWikiNodeToken = "wik_feature",
                ExistingWikiNodeUrl = "https://example.feishu.cn/wiki/wik_feature"
            },
            SyncCache = new SyncCacheContract
            {
                CacheDirectory = cacheDir,
                ExcelCacheDirectory = excelDir
            }
        };
        request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract
        {
            TableId = "ItemsData",
            DisplayName = "Items",
            Profile = "feature-config",
            SpreadsheetToken = "sht_items",
            SheetId = "sheet_items",
            SemanticHash = "hash_items"
        });
        request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract
        {
            TableId = "SkillsData",
            DisplayName = "Skills",
            Profile = "feature-config",
            SpreadsheetToken = "sht_skills",
            SheetId = "sheet_skills",
            SemanticHash = "hash_skills"
        });

        var requestPath = Path.Combine(root, "request.json");
        var resultPath = Path.Combine(root, "sync-status.result.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, CamelJsonOptions()));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "apply-contract", "--request", requestPath, "--out", resultPath });

        AssertEqual("0", exitCode.ToString(), "sync-status should be a read-only successful lifecycle.");
        var json = await File.ReadAllTextAsync(resultPath);
        AssertTrue(json.Contains("\"cacheStatus\":\"missingCache\"") || json.Contains("\"cacheStatus\": \"missingCache\""), "sync-status should compute local cache state instead of unknown.");
        AssertTrue(json.Contains("sync-status.local_cache.inspect"), "sync-status should record the read-only local cache inspection action.");
        AssertTrue(json.Contains("\"upToDateTables\"") && json.Contains("ItemsData"), "sync-status should mark local matching cache as up-to-date.");
        AssertTrue(json.Contains("\"missingCacheTables\"") && json.Contains("SkillsData"), "sync-status should report missing local cache per table.");
        AssertTrue(!json.Contains("sync-cache.online_read"), "sync-status must not read online Sheet values.");
        AssertTrue(!json.Contains("sync-cache.export_xlsx"), "sync-status must not export online xlsx.");
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

static async Task CurrentBranchBootstrapFromTargetPlansInsteadOfSeed()
{
    var request = new LifecycleContractRequest
    {
        Operation = "bootstrap-current-branch-from-target",
        DryRun = true,
        Git = new ContractGitSpec { Branch = "feature/config", Profile = "feature-config" },
        BranchWorkspace = new BranchWorkspaceContract
        {
            MainGitBranch = "main",
            MainFeishuBranch = "main",
            ProfileNameTemplate = "{gitBranch}",
            BranchNodeTitleTemplate = "branch-{slug}"
        },
        MergeInputs = new MergeInputsContract { TargetBranch = "main", TargetFeishuProfile = "main" }
    };
    request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract { TableId = "ItemsData", DisplayName = "Items" });
    request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract { TableId = "SkillsData", DisplayName = "Skills" });
    request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract { TableId = "ItemsData", DisplayName = "Items", Profile = "main", SpreadsheetToken = "sht_items_main", SheetId = "sheet_items" });
    request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract { TableId = "SkillsData", DisplayName = "Skills", Profile = "main", SpreadsheetToken = "sht_skills_main", SheetId = "sheet_skills" });

    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);

    AssertTrue(result.Success, "Current branch bootstrap dry-run should be a first-class safe plan.");
    AssertTrue(!string.IsNullOrWhiteSpace(result.RequestFingerprint), "Current branch bootstrap preview should produce a request fingerprint for safe apply.");
    AssertTrue(result.Actions.Any(a => a.Action == "current_branch.bootstrap_from_target.plan" && a.Details["targetBranch"] == "main"), "Plan should identify target branch.");
    AssertTrue(result.Actions.Any(a => a.Action == "current_branch.sheets.copy_from_target"), "Plan should describe deriving online sheets from target branch, not local Excel Seed.");
}

static RegistrySnapshot SampleBranchStatusSnapshot()
{
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
                RecordId = "rec_items",
                Values =
                {
                    ["配表ID"] = "ItemsData",
                    ["显示名称"] = "Items",
                    ["配置Profile"] = "feature-config",
                    ["在线表Token"] = "sht_items",
                    ["工作表ID"] = "sheet_items",
                    ["在线表链接"] = "https://example.feishu.cn/sheets/sht_items",
                    ["Wiki节点Token"] = "wik_feature"
                }
            },
            new RegistryRecordSnapshot
            {
                RecordId = "rec_skills",
                Values =
                {
                    ["配表ID"] = "SkillsData",
                    ["显示名称"] = "Skills",
                    ["配置Profile"] = "feature-config",
                    ["在线表Token"] = "sht_skills",
                    ["工作表ID"] = "sheet_skills",
                    ["在线表链接"] = "https://example.feishu.cn/sheets/sht_skills",
                    ["Wiki节点Token"] = "wik_feature"
                }
            }
        }
    });
    return snapshot;
}

static async Task CompareMergeDryRunHydratesWorkspacesAndTableScope()
{
    var request = SampleCompareMergeRequest();
    var snapshot = SampleCompareMergeRegistrySnapshot(includeTarget: true);
    ConfigSheetForge.Cli.Program.HydrateCompareMergeRequestFromRegistrySnapshot(request, snapshot, "");

    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);

    AssertTrue(result.Success, "compare-merge dry-run should succeed when source/target workspaces and table locators are hydrated.");
    AssertTrue(result.Actions.Any(a => a.Action == "merge.inputs.prepare" && a.Details.TryGetValue("tableCount", out var count) && count == "2"), "compare-merge should expose tableCount in merge.inputs.prepare details.");
    AssertTrue(result.Actions.Any(a => a.Action == "merge.compare" && a.Details.TryGetValue("targetWikiNodeToken", out var token) && token == "wik_main"), "compare-merge should expose target workspace details.");
    AssertTrue(result.Actions.Any(a => a.Action == "merge.preview" && a.Details.TryGetValue("mergeReportPath", out var path) && path.Contains("merge-report.md")), "compare-merge should include merge report path.");
    AssertTrue(result.Actions.Any(a => a.Action == "merge.preview" && a.Details.TryGetValue("tableIds", out var tables) && tables.Contains("ItemsData") && tables.Contains("SkillsData")), "compare-merge preview should list compared tables.");
}

static async Task CompareMergeDryRunFailsWithoutTargetWorkspace()
{
    var request = SampleCompareMergeRequest();
    var snapshot = SampleCompareMergeRegistrySnapshot(includeTarget: false);
    ConfigSheetForge.Cli.Program.HydrateCompareMergeRequestFromRegistrySnapshot(request, snapshot, "");

    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);

    AssertTrue(!result.Success, "compare-merge dry-run must not report success when target workspace/table locators are missing.");
    AssertTrue(result.Actions.Any(a => a.Action == "merge.inputs.prepare" && a.Status == "blocked"), "blocked compare-merge should still show the missing input plan action.");
    AssertTrue(result.HumanReadableFailures.Any(f => f.Contains("目标分支") && (f.Contains("BranchBindings") || f.Contains("ConfigSheets"))), "failure should explain that target branch registry data is missing.");
}

static async Task CompareMergeDryRunFingerprintsMergeReviewInput()
{
    var request = SampleCompareMergeRequest();
    var snapshot = SampleCompareMergeRegistrySnapshot(includeTarget: true);
    ConfigSheetForge.Cli.Program.HydrateCompareMergeRequestFromRegistrySnapshot(request, snapshot, "");

    var compare = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
    AssertTrue(compare.Success, "compare-merge dry-run should pass.");
    AssertTrue(!string.IsNullOrWhiteSpace(compare.RequestFingerprint), "compare-merge result should include requestFingerprint for later review submission.");
    AssertEqual("feature/config", compare.RequestSummary["sourceBranch"], "fingerprint summary should include source branch.");
    AssertEqual("main", compare.RequestSummary["targetBranch"], "fingerprint summary should include target branch.");
    AssertTrue(compare.RequestSummary["tableIds"].Contains("ItemsData") && compare.RequestSummary["tableIds"].Contains("SkillsData"), "fingerprint summary should include table scope.");

    var reviewRequest = new LifecycleContractRequest
    {
        Operation = "submit-merge-review",
        DryRun = true,
        Locale = "zh-Hans",
        Registry = new RegistryContract { BaseToken = "base_mock" },
        Git = new ContractGitSpec { Branch = "feature/config" },
        MergeInputs = request.MergeInputs,
        MergeReview = new MergeReviewContract
        {
            SourceBranch = "feature/config",
            TargetBranch = "main",
            TableIds = new List<string> { "ItemsData", "SkillsData" },
            PrNumber = "480",
            PrUrl = "https://github.example/pull/480",
            MergeReportPath = "Temp/ConfigSheetForge/merge-report.md",
            MergedPath = "Temp/ConfigSheetForge/merged.semantic.json",
            RequestFingerprint = compare.RequestFingerprint,
            ConfirmSubmit = true
        }
    };

    var review = await LifecycleExecutor.ExecuteAsync(reviewRequest, new PreviewLifecyclePlatform(), CancellationToken.None);
    AssertTrue(review.Success, "submit-merge-review dry-run should pass when fingerprint matches.");
    AssertEqual(compare.RequestFingerprint, review.RequestFingerprint, "merge review fingerprint must match the compare preview fingerprint.");
    AssertTrue(review.Actions.Any(a => a.Action == "registry.merge_reviews.upsert"), "submit-merge-review should plan a MergeReviews upsert.");
}

static void ReviewStatusNormalizesFeishuSingleSelectValues()
{
    AssertEqual("approved", PrGateReportEvaluator.NormalizeReviewStatus("[\n  \"approved\"\n]"), "JSON array string should normalize to the selected option.");
    AssertEqual("completed", PrGateReportEvaluator.NormalizeReviewStatus("[{\"text\":\"completed\"}]"), "Option object text should normalize.");
    AssertEqual("passed", PrGateReportEvaluator.NormalizeReviewStatus("{\"name\":\"passed\"}"), "Option object name should normalize.");
    AssertTrue(PrGateReportEvaluator.ReviewPassed("[{\"value\":\"通过\"}]"), "Option object value should be accepted for Chinese passed status.");
    AssertTrue(PrGateReportEvaluator.ReviewPassed("[\"approved\"]"), "Feishu single-select arrays should pass gate review checks.");
    AssertTrue(!PrGateReportEvaluator.ReviewPassed("[\"pending\"]"), "Pending single-select arrays should still block.");
}

static async Task PrGateHydratesLiveMergeReviewRecords()
{
    var request = new LifecycleContractRequest
    {
        Operation = "pr-gate-report",
        Locale = "zh-Hans",
        Registry = new RegistryContract { BaseToken = "base_mock" },
        Git = new ContractGitSpec { Branch = "feature/config", Head = "abc123" },
        GateReport = new PrGateReport
        {
            GitHead = "abc123",
            Branch = "feature/config",
            Permissions = new GatePermissions { CanReadRegistry = true, CanReadSheets = true },
            PortableSubset = new GateCheckState { Passed = true },
            Triangulation = new GateCheckState { Passed = true },
            ChangedTables = { "ItemsData" },
            CacheHashes = { ["ItemsData"] = "hash" }
        }
    };
    var snapshot = new RegistrySnapshot();
    snapshot.Tables.Add(new RegistryTableSnapshot
    {
        MachineKey = "MergeReviews",
        TableId = "tbl_merge",
        DisplayName = "合并审查",
        Records =
        {
            new RegistryRecordSnapshot
            {
                RecordId = "rec_merge",
                Values =
                {
                    ["审查ID"] = "merge-feature-config-to-main-20260527-abc123",
                    ["配表ID"] = "__project_pr_gate__",
                    ["Git分支"] = "feature/config",
                    ["状态"] = "已通过",
                    ["ApproverRole"] = "configOwner",
                    ["更新时间"] = "2026-05-27T12:00:00Z"
                }
            }
        }
    });

    ConfigSheetForge.Cli.Program.HydratePrGateReportFromRegistrySnapshot(request, snapshot);
    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
    AssertTrue(result.Success, "PR gate should pass when live MergeReviews has an approved project-level record.");
    AssertEqual("rec_merge", result.PrGateReport.MergeReview.RecordId, "gate report should include live MergeReviews record id.");
    AssertEqual("merge-feature-config-to-main-20260527-abc123", result.PrGateReport.MergeReview.ReviewId, "gate report should include review id.");
    AssertEqual("configOwner", result.PrGateReport.MergeReview.ApproverRole, "gate report should include approver role.");
    AssertEqual("__project_pr_gate__", result.PrGateReport.MergeReview.TableId, "gate report should include table id.");
}

static async Task PrGateHydratesJsonArrayMergeReviewStatus()
{
    var request = new LifecycleContractRequest
    {
        Operation = "pr-gate-report",
        Locale = "zh-Hans",
        Registry = new RegistryContract { BaseToken = "base_mock" },
        Git = new ContractGitSpec { Branch = "feature/config", Head = "abc123" },
        GateReport = new PrGateReport
        {
            GitHead = "abc123",
            Branch = "feature/config",
            Permissions = new GatePermissions { CanReadRegistry = true, CanReadSheets = true },
            PortableSubset = new GateCheckState { Passed = true },
            Triangulation = new GateCheckState { Passed = true },
            ChangedTables = { "ItemsData" },
            CacheHashes = { ["ItemsData"] = "hash" }
        }
    };
    var snapshot = new RegistrySnapshot();
    snapshot.Tables.Add(new RegistryTableSnapshot
    {
        MachineKey = "MergeReviews",
        TableId = "tbl_merge",
        DisplayName = "合并审查",
        Records =
        {
            new RegistryRecordSnapshot
            {
                RecordId = "rec_merge_json_array",
                Values =
                {
                    ["审查ID"] = "merge-feature-config-to-main-20260527-abc123",
                    ["配表ID"] = "__project_pr_gate__",
                    ["Git分支"] = "feature/config",
                    ["状态"] = "[\n          \"approved\"\n        ]",
                    ["ApproverRole"] = "configOwner",
                    ["更新时间"] = "2026-05-27T12:00:00Z"
                }
            }
        }
    });

    ConfigSheetForge.Cli.Program.HydratePrGateReportFromRegistrySnapshot(request, snapshot);
    var result = await LifecycleExecutor.ExecuteAsync(request, new PreviewLifecyclePlatform(), CancellationToken.None);
    AssertTrue(result.Success, "PR gate should pass when Feishu single-select status is a JSON array string.");
    AssertEqual("approved", result.PrGateReport.MergeReview.Status, "gate report should store normalized merge review status.");
    AssertEqual("rec_merge_json_array", result.PrGateReport.MergeReview.RecordId, "gate report should include the MergeReviews record id.");
    AssertEqual("passed", result.PrGateReport.GateState, "gateState should be passed instead of waived.");
    AssertTrue(!result.PrGateReport.Waived, "valid MergeReviews should not rely on waiver.");
    AssertTrue(result.PrGateReport.HumanReadableFailures.Count == 0, "valid MergeReviews should clear ordinary failures.");
    AssertTrue(result.PrGateReport.WaivedFailures.Count == 0, "valid MergeReviews should not leave waived failures.");
}

static void LarkRecordIdParserAcceptsNestedUpsertResult()
{
    AssertEqual("rec_nested", ConfigSheetForge.Cli.Program.ParseLarkRecordId("{\"ok\":true,\"data\":{\"record\":{\"record_id\":\"rec_nested\"}}}"), "record_id inside data.record should be parsed.");
    AssertEqual("rec_list", ConfigSheetForge.Cli.Program.ParseLarkRecordId("{\"data\":{\"records\":[{\"recordId\":\"rec_list\"}]}}"), "recordId inside data.records should be parsed.");
    AssertEqual("rec_short", ConfigSheetForge.Cli.Program.ParseLarkRecordId("{\"data\":{\"id\":\"rec_short\"}}"), "record-like id should be accepted as a fallback.");
}

static async Task SubmitMergeReviewApplyBlocksMissingStatusOptions()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-merge-review-options-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var oldDir = Directory.GetCurrentDirectory();
    var script = Path.Combine(temp, "lark-cli.cmd");
    var log = Path.Combine(temp, "calls.log");
    var requestPath = Path.Combine(temp, "submit.contract.json");
    var previewPath = Path.Combine(temp, "compare.result.json");
    var resultPath = Path.Combine(temp, "submit.result.json");
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
        "  echo {\"ok\":true,\"data\":{\"tables\":[{\"id\":\"tbl_merge\",\"name\":\"MergeReviews\"}],\"total\":1}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +field-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true,\"data\":{\"fields\":[{\"id\":\"fld_review\",\"name\":\"ReviewId\",\"type\":\"text\"},{\"id\":\"fld_table\",\"name\":\"TableId\",\"type\":\"text\"},{\"id\":\"fld_branch\",\"name\":\"GitBranch\",\"type\":\"text\"},{\"id\":\"fld_status\",\"name\":\"Status\",\"type\":\"single_select\",\"options\":[]},{\"id\":\"fld_role\",\"name\":\"ApproverRole\",\"type\":\"text\"},{\"id\":\"fld_update\",\"name\":\"UpdatedAt\",\"type\":\"datetime\"}]}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +record-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true,\"data\":{\"fields\":[\"ReviewId\",\"TableId\",\"GitBranch\",\"Status\"],\"data\":[],\"record_id_list\":[]}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo unexpected command %* 1>&2\r\n" +
        "exit /b 2\r\n");

    try
    {
        Directory.SetCurrentDirectory(temp);
        var request = new LifecycleContractRequest
        {
            Operation = "submit-merge-review",
            DryRun = false,
            Locale = "zh-Hans",
            Registry = new RegistryContract { BaseToken = "base_mock" },
            Git = new ContractGitSpec { Branch = "feature/config", Head = "abc123" },
            MergeInputs = new MergeInputsContract
            {
                SourceBranch = "feature/config",
                TargetBranch = "main",
                PrNumber = "480",
                PrUrl = "https://github.example/pull/480",
                MergeReportPath = "Temp/merge-report.md",
                MergedPath = "Temp/merged.semantic.json"
            },
            MergeReview = new MergeReviewContract
            {
                SourceBranch = "feature/config",
                TargetBranch = "main",
                TableId = "__project_pr_gate__",
                TableIds = { "ItemsData" },
                ConfirmSubmit = true
            }
        };
        var summary = LifecycleExecutor.BuildMergeReviewInputSummary(request, request.MergeReview.TableIds);
        request.MergeReview.RequestFingerprint = summary.Fingerprint;
        var preview = new LifecycleContractResult
        {
            Operation = "compare-merge",
            DryRun = true,
            Success = true,
            RequestFingerprint = summary.Fingerprint,
            RequestSummary =
            {
                ["sourceBranch"] = summary.SourceBranch,
                ["targetBranch"] = summary.TargetBranch,
                ["tableIds"] = summary.TableIdsText,
                ["prNumber"] = request.MergeInputs.PrNumber,
                ["prUrl"] = request.MergeInputs.PrUrl,
                ["mergeReportPath"] = request.MergeInputs.MergeReportPath,
                ["mergedPath"] = request.MergeInputs.MergedPath
            }
        };
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, CamelJsonOptions()));
        await File.WriteAllTextAsync(previewPath, JsonSerializer.Serialize(preview, CamelJsonOptions()));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[]
        {
            "apply-contract",
            "--request", requestPath,
            "--preview-result", previewPath,
            "--confirm",
            "--lark-cli", script,
            "--out", resultPath
        });
        AssertEqual("1", exitCode.ToString(), "submit-merge-review apply should fail before writing when status options are missing.");
        var resultJson = await File.ReadAllTextAsync(resultPath);
        AssertTrue(resultJson.Contains("MergeReviews.状态") && resultJson.Contains("registry-migrate"), "failure should tell users to run registry-migrate first.");
        var calls = File.Exists(log) ? File.ReadAllText(log) : "";
        AssertTrue(!calls.Contains("base +record-upsert"), "preflight should block before record-upsert.");
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

static async Task SubmitMergeReviewApplyReturnsNestedRecordId()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-merge-review-record-id-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var oldDir = Directory.GetCurrentDirectory();
    var script = Path.Combine(temp, "lark-cli.cmd");
    var requestPath = Path.Combine(temp, "submit.contract.json");
    var previewPath = Path.Combine(temp, "compare.result.json");
    var resultPath = Path.Combine(temp, "submit.result.json");
    await File.WriteAllTextAsync(script,
        "@echo off\r\n" +
        "echo %* | find \"doctor\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +table-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true,\"data\":{\"tables\":[{\"id\":\"tbl_merge\",\"name\":\"MergeReviews\"}],\"total\":1}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +field-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true,\"data\":{\"fields\":[{\"id\":\"fld_review\",\"name\":\"ReviewId\",\"type\":\"text\"},{\"id\":\"fld_table\",\"name\":\"TableId\",\"type\":\"text\"},{\"id\":\"fld_branch\",\"name\":\"GitBranch\",\"type\":\"text\"},{\"id\":\"fld_status\",\"name\":\"Status\",\"type\":\"single_select\",\"options\":[{\"name\":\"approved\"},{\"name\":\"completed\"},{\"name\":\"passed\"}]},{\"id\":\"fld_role\",\"name\":\"ApproverRole\",\"type\":\"text\"},{\"id\":\"fld_update\",\"name\":\"UpdatedAt\",\"type\":\"datetime\"}]}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +record-list\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true,\"data\":{\"fields\":[\"ReviewId\",\"TableId\",\"GitBranch\",\"Status\"],\"data\":[],\"record_id_list\":[]}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo %* | find \"base +record-upsert\" >nul\r\n" +
        "if not errorlevel 1 (\r\n" +
        "  echo {\"ok\":true,\"data\":{\"record\":{\"record_id\":\"rec_written\"}}}\r\n" +
        "  exit /b 0\r\n" +
        ")\r\n" +
        "echo unexpected command %* 1>&2\r\n" +
        "exit /b 2\r\n");

    try
    {
        Directory.SetCurrentDirectory(temp);
        var request = new LifecycleContractRequest
        {
            Operation = "submit-merge-review",
            DryRun = false,
            Locale = "zh-Hans",
            Registry = new RegistryContract { BaseToken = "base_mock" },
            Git = new ContractGitSpec { Branch = "feature/config", Head = "abc123" },
            MergeInputs = new MergeInputsContract
            {
                SourceBranch = "feature/config",
                TargetBranch = "main",
                MergeReportPath = "Temp/merge-report.md",
                MergedPath = "Temp/merged.semantic.json"
            },
            MergeReview = new MergeReviewContract
            {
                SourceBranch = "feature/config",
                TargetBranch = "main",
                TableId = "__project_pr_gate__",
                TableIds = { "ItemsData" },
                ConfirmSubmit = true
            }
        };
        var summary = LifecycleExecutor.BuildMergeReviewInputSummary(request, request.MergeReview.TableIds);
        request.MergeReview.RequestFingerprint = summary.Fingerprint;
        var preview = new LifecycleContractResult
        {
            Operation = "compare-merge",
            DryRun = true,
            Success = true,
            RequestFingerprint = summary.Fingerprint,
            RequestSummary =
            {
                ["sourceBranch"] = summary.SourceBranch,
                ["targetBranch"] = summary.TargetBranch,
                ["tableIds"] = summary.TableIdsText,
                ["mergeReportPath"] = request.MergeInputs.MergeReportPath,
                ["mergedPath"] = request.MergeInputs.MergedPath
            }
        };
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, CamelJsonOptions()));
        await File.WriteAllTextAsync(previewPath, JsonSerializer.Serialize(preview, CamelJsonOptions()));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[]
        {
            "apply-contract",
            "--request", requestPath,
            "--preview-result", previewPath,
            "--confirm",
            "--lark-cli", script,
            "--out", resultPath
        });
        AssertEqual("0", exitCode.ToString(), "submit-merge-review apply should succeed when status options are ready.");
        var resultJson = await File.ReadAllTextAsync(resultPath);
        AssertTrue(resultJson.Contains("\"recordId\": \"rec_written\"") || resultJson.Contains("\"recordId\":\"rec_written\""), "submit result should include nested upsert record_id.");
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

static LifecycleContractRequest SampleCompareMergeRequest()
{
    return new LifecycleContractRequest
    {
        Operation = "compare-merge",
        DryRun = true,
        Locale = "zh-Hans",
        Registry = new RegistryContract { BaseToken = "base_mock" },
        Git = new ContractGitSpec { Branch = "feature/config", Profile = "feature-config" },
        BranchWorkspace = new BranchWorkspaceContract
        {
            RequireOneToOneBinding = true,
            MainGitBranch = "main",
            MainFeishuBranch = "main",
            ProfileNameTemplate = "{gitBranch}",
            BranchNodeTitleTemplate = "branch-{slug}"
        },
        MergeInputs = new MergeInputsContract
        {
            SourceBranch = "feature/config",
            TargetBranch = "main",
            TargetFeishuProfile = "main",
            BasePath = "Temp/ConfigSheetForge/merge-inputs/main_base.semantic.json",
            OursPath = "Temp/ConfigSheetForge/merge-inputs/feature-config_ours.semantic.json",
            TheirsPath = "Temp/ConfigSheetForge/merge-inputs/main_theirs.semantic.json",
            MergeReportPath = "Temp/ConfigSheetForge/merge-report.md",
            MergedPath = "Temp/ConfigSheetForge/merged.semantic.json",
            PrNumber = "480",
            PrUrl = "https://github.example/pull/480"
        },
        MergePolicy = new MergePolicyContract { LowRisk = true }
    };
}

static RegistrySnapshot SampleCompareMergeRegistrySnapshot(bool includeTarget)
{
    var snapshot = new RegistrySnapshot();
    var branchTable = new RegistryTableSnapshot
    {
        MachineKey = "BranchBindings",
        TableId = "tbl_branch",
        DisplayName = "分支绑定"
    };
    branchTable.Records.Add(new RegistryRecordSnapshot
    {
        RecordId = "rec_feature",
        Values =
        {
            ["Git分支"] = "feature/config",
            ["配置Profile"] = "feature-config",
            ["Wiki节点Token"] = "wik_feature",
            ["Wiki节点链接"] = "https://example.feishu.cn/wiki/wik_feature",
            ["状态"] = "active"
        }
    });
    if (includeTarget)
    {
        branchTable.Records.Add(new RegistryRecordSnapshot
        {
            RecordId = "rec_main",
            Values =
            {
                ["Git分支"] = "main",
                ["配置Profile"] = "main",
                ["Wiki节点Token"] = "wik_main",
                ["Wiki节点链接"] = "https://example.feishu.cn/wiki/wik_main",
                ["状态"] = "active"
            }
        });
    }

    snapshot.Tables.Add(branchTable);
    var configTable = new RegistryTableSnapshot
    {
        MachineKey = "ConfigSheets",
        TableId = "tbl_config",
        DisplayName = "配表清单"
    };
    foreach (var tableId in new[] { "ItemsData", "SkillsData" })
    {
        configTable.Records.Add(new RegistryRecordSnapshot
        {
            RecordId = "rec_" + tableId + "_feature",
            Values =
            {
                ["配表ID"] = tableId,
                ["显示名称"] = tableId.Replace("Data", ""),
                ["配置Profile"] = "feature-config",
                ["在线表Token"] = "sht_" + tableId + "_feature",
                ["工作表ID"] = "sheet_" + tableId + "_feature",
                ["在线表链接"] = "https://example.feishu.cn/sheets/" + tableId + "_feature",
                ["Wiki节点Token"] = "wik_feature"
            }
        });
        if (includeTarget)
        {
            configTable.Records.Add(new RegistryRecordSnapshot
            {
                RecordId = "rec_" + tableId + "_main",
                Values =
                {
                    ["配表ID"] = tableId,
                    ["显示名称"] = tableId.Replace("Data", ""),
                    ["配置Profile"] = "main",
                    ["在线表Token"] = "sht_" + tableId + "_main",
                    ["工作表ID"] = "sheet_" + tableId + "_main",
                    ["在线表链接"] = "https://example.feishu.cn/sheets/" + tableId + "_main",
                    ["Wiki节点Token"] = "wik_main"
                }
            });
        }
    }

    snapshot.Tables.Add(configTable);
    return snapshot;
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
      "toolkit": {
        "defaultGateReportPath": "Temp/ConfigSheetForge/pr-gate-report.json",
        "coreCliEnvironmentVariable": "CONFIG_SHEET_FORGE_CLI",
        "sourceCheckoutEnvironmentVariable": "CONFIG_SHEET_FORGE_ROOT",
        "sourceCliProjectRelativePath": "src/cli/ConfigSheetForge.Cli",
        "larkCliPath": "C:/Users/dev/AppData/Roaming/npm/lark-cli.ps1",
        "larkCliEnvironmentVariable": "CONFIG_SHEET_FORGE_LARK_CLI",
        "allowUserFallback": false,
        "defaultTargetBranch": "main",
        "githubRepository": "today080221/config-sheet-forge",
        "allowPrAutoDetect": true
      },
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
    AssertEqual("CONFIG_SHEET_FORGE_CLI", summary.CoreCliEnvironmentVariable, "CLI env var should be read.");
    AssertEqual("CONFIG_SHEET_FORGE_ROOT", summary.SourceCheckoutEnvironmentVariable, "source checkout env var should be read.");
    AssertEqual("src/cli/ConfigSheetForge.Cli", summary.SourceCliProjectRelativePath, "source CLI project path should be read.");
    AssertEqual("C:/Users/dev/AppData/Roaming/npm/lark-cli.ps1", summary.LarkCliPath, "lark-cli path should be read.");
    AssertEqual("CONFIG_SHEET_FORGE_LARK_CLI", summary.LarkCliEnvironmentVariable, "lark-cli env var should be read.");
    AssertTrue(!summary.AllowUserFallback, "strict bot should be the default project setting.");
    AssertEqual("main", summary.DefaultTargetBranch, "default target branch should be read.");
    AssertEqual("today080221/config-sheet-forge", summary.GithubRepository, "GitHub repository should be read.");
    AssertTrue(summary.AllowPrAutoDetect, "PR auto detect should be read.");
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

static void ProjectConfigProbeReadsDocumentationTargets()
{
    var json = """
    {
      "documentationTargets": {
        "5 分钟入门": "docs/tooling/config-sheet-source-of-truth.md",
        "Feishu entry": "https://example.feishu.cn/wiki/root"
      },
      "toolkit": {
        "localDocs": [
          "docs/tooling/designer-flow.md"
        ],
        "documentationTargets": {
          "PR 合并流程": {
            "title": "PR 合并流程",
            "path": "docs/tooling/pr-merge.md"
          }
        }
      }
    }
    """;

    var summary = ProjectConfigProbe.ProbeJson("ProjectSettings/Example.ConfigSheetForge.json", json);

    AssertEqual("docs/tooling/config-sheet-source-of-truth.md", summary.DocumentationTargets["5 分钟入门"], "named doc link should be read.");
    AssertEqual("https://example.feishu.cn/wiki/root", summary.DocumentationTargets["Feishu entry"], "feishu doc link should be read.");
    AssertEqual("docs/tooling/designer-flow.md", summary.DocumentationTargets["projectDocs"], "localDocs should become project docs.");
    AssertEqual("docs/tooling/pr-merge.md", summary.DocumentationTargets["PR 合并流程"], "object doc target should use path.");
}

static void ProjectConfigProbeReadsRolesAndNewTableOptions()
{
    var json = """
    {
      "roles": {
        "configOwner": {
          "displayName": "配置负责人",
          "canApproveWaiver": true,
          "canApproveMainWriteBack": true
        },
        "schemaReviewer": {
          "displayName": "Schema 审查人",
          "canApproveSchemaReview": true
        },
        "tableOwner": {
          "displayName": "配表负责人",
          "canRequestMerge": true
        }
      },
      "newTable": {
        "defaultOwnerRole": "tableOwner",
        "supportedFieldTypes": ["string", "integer", "number", "bool", "date", "datetime", "enum", "json"],
        "defaultFields": [
          { "key": "id", "displayName": "ID", "valueKind": "string", "description": "唯一ID", "isPrimary": true },
          { "key": "name", "displayName": "名称", "valueKind": "string", "description": "显示名称" }
        ]
      },
      "github": {
        "requiredForPrAutoDetect": false,
        "installHelpUrl": "https://cli.github.com/"
      }
    }
    """;

    var summary = ProjectConfigProbe.ProbeJson("ProjectSettings/Example.ConfigSheetForge.json", json);

    AssertEqual("3", summary.Roles.Count.ToString(), "roles should be read.");
    AssertEqual("配表负责人", summary.Roles.First(r => r.Key == "tableOwner").DisplayName, "role display name should be read.");
    AssertTrue(summary.Roles.First(r => r.Key == "schemaReviewer").CanApproveSchemaReview, "schema reviewer permission should be read.");
    AssertTrue(summary.Roles.First(r => r.Key == "configOwner").CanApproveWaiver, "waiver permission should be read.");
    AssertTrue(summary.Roles.First(r => r.Key == "configOwner").CanApproveMainWriteBack, "main write-back permission should be read.");
    AssertEqual("tableOwner", summary.NewTableDefaultOwnerRole, "default owner role should be read.");
    AssertEqual("8", summary.NewTableSupportedFieldTypes.Count.ToString(), "supported field types should be read.");
    AssertEqual("2", summary.NewTableDefaultFields.Count.ToString(), "default fields should be read.");
    AssertEqual("id", summary.NewTableDefaultFields[0].Key, "first default field key should be read.");
    AssertTrue(summary.NewTableDefaultFields[0].IsPrimary, "primary default field should be read.");
    AssertTrue(!summary.GithubRequiredForPrAutoDetect, "github required flag should be read.");
    AssertEqual("https://cli.github.com/", summary.GithubInstallHelpUrl, "github install help URL should be read.");
}

static void ProjectConfigProbeReadsUnityExcelToSoDefaults()
{
    var json = """
    {
      "unityExcelToSo": {
        "scriptDirectory": "Assets/Scripts/Data",
        "assetDirectory": "Assets/DataConfig/Auto-generated",
        "namespace": "Assets.Scripts.Data"
      },
      "tables": [
        {
          "id": "ProjectileData",
          "displayName": "投射物数据",
          "oldExcelPath": "Excel/ProjectileData.xlsx"
        },
        {
          "id": "RoomData",
          "displayName": "房间数据",
          "scriptDirectory": "Assets/Scripts/RoomData",
          "assetDirectory": "Assets/DataConfig/Rooms",
          "namespace": "Assets.Scripts.RoomData"
        }
      ]
    }
    """;

    var summary = ProjectConfigProbe.ProbeJson("ProjectSettings/Example.ConfigSheetForge.json", json);

    AssertEqual("Assets/Scripts/Data", summary.UnityExcelToSoScriptDirectory, "project-level ExcelToSO script directory should be read.");
    AssertEqual("Assets/DataConfig/Auto-generated", summary.UnityExcelToSoAssetDirectory, "project-level ExcelToSO asset directory should be read.");
    AssertEqual("Assets.Scripts.Data", summary.UnityExcelToSoNamespace, "project-level ExcelToSO namespace should be read.");
    AssertEqual("", summary.Tables[0].ScriptDirectory, "tables without overrides should keep table-level script dir empty so Unity can apply project defaults.");
    AssertEqual("Assets/Scripts/RoomData", summary.Tables[1].ScriptDirectory, "table-level script directory override should be read.");
    AssertEqual("Assets/DataConfig/Rooms", summary.Tables[1].AssetDirectory, "table-level asset directory override should be read.");
    AssertEqual("Assets.Scripts.RoomData", summary.Tables[1].Namespace, "table-level namespace override should be read.");
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
        AssertNoUtf8Bom(finalGateReport, "standard gate report");
        var gateJson = File.ReadAllText(finalGateReport);
        AssertTrue(gateJson.Contains("\"gitHead\""), "standard gate report should be a PrGateReport JSON object.");
        AssertTrue(!gateJson.Contains("\"prGateReport\""), "standard gate report should not wrap LifecycleContractResult.");

        AssertNoUtf8Bom(resultPath, "lifecycle result");
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

static async Task CliMachineJsonOutputsAreUtf8WithoutBom()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-json-encoding-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
        var requestPath = Path.Combine(root, "request.json");
        var resultPath = Path.Combine(root, "Temp", "ConfigSheetForge", "desktop", "sync-cache.result.json");
        var request = new LifecycleContractRequest
        {
            Operation = "pr-gate-report",
            GateReportPath = Path.Combine("Temp", "ConfigSheetForge", "pr-gate-report.json"),
            GateReport = new PrGateReport
            {
                Branch = "feature/config",
                GitHead = "abc123",
                MergeReview = new GateReviewState { Status = "approved" },
                PortableSubset = new GateCheckState { Passed = true },
                Triangulation = new GateCheckState { Passed = true },
                SchemaReview = new GateReviewState { Status = "approved" }
            }
        };
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[]
        {
            "apply-contract",
            "--request",
            requestPath,
            "--out",
            resultPath
        });

        AssertEqual("0", exitCode.ToString(), "CLI JSON encoding fixture should pass.");
        AssertNoUtf8Bom(resultPath, "desktop lifecycle result");
        AssertNoUtf8Bom(Path.Combine(root, "Temp", "ConfigSheetForge", "pr-gate-report.json"), "standard PR gate report");
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

static async Task CliNormalizesVerbatimOutAndRequestPaths()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var root = Path.Combine(Path.GetTempPath(), "csforge-verbatim-path-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
        var requestPath = Path.Combine(root, "request.json");
        var resultPath = Path.Combine(root, "Temp", "ConfigSheetForge", "desktop", "sync-cache.result.json");
        var request = new LifecycleContractRequest
        {
            Operation = "pr-gate-report",
            GateReportPath = Path.Combine("Temp", "ConfigSheetForge", "pr-gate-report.json"),
            GateReport = new PrGateReport
            {
                Branch = "feature/config",
                GitHead = "abc123",
                MergeReview = new GateReviewState { Status = "approved" },
                PortableSubset = new GateCheckState { Passed = true },
                Triangulation = new GateCheckState { Passed = true },
                SchemaReview = new GateReviewState { Status = "approved" }
            }
        };
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[]
        {
            "apply-contract",
            "--request",
            ToVerbatimMixedPath(requestPath),
            "--out",
            ToVerbatimMixedPath(resultPath)
        });

        AssertEqual("0", exitCode.ToString(), "CLI should normalize Windows verbatim paths with mixed separators.");
        AssertTrue(File.Exists(resultPath), "normalized --out path should be written.");
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

static async Task ApplyContractSyncCacheApplyRequiresConfirmation()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-sync-cache-confirm-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
        var requestPath = Path.Combine(root, "request.json");
        var resultPath = Path.Combine(root, "result.json");
        var request = new LifecycleContractRequest
        {
            Operation = "sync-cache",
            DryRun = false,
            Git = new ContractGitSpec
            {
                Branch = "feature/config",
                Profile = "feature/config"
            },
            BranchWorkspace = new BranchWorkspaceContract
            {
                GitBranch = "feature/config",
                Profile = "feature/config",
                ExistingWikiNodeToken = "wik_feature"
            },
            SyncCache = new SyncCacheContract
            {
                ConfirmApply = false
            }
        };
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "apply-contract", "--request", requestPath, "--out", resultPath });

        AssertEqual("2", exitCode.ToString(), "sync-cache apply-contract should require explicit confirmation.");
        AssertTrue(!File.Exists(resultPath), "Unconfirmed sync-cache apply should fail before writing a lifecycle result.");
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

static async Task SyncCacheApplyRequiresPreviewResult()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-sync-preview-required-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
        var requestPath = Path.Combine(root, "sync-request.json");
        var resultPath = Path.Combine(root, "sync-result.json");
        var request = new LifecycleContractRequest
        {
            Operation = "sync-cache",
            DryRun = false,
            Git = new ContractGitSpec
            {
                Branch = "feature/config",
                Profile = "feature/config"
            },
            BranchWorkspace = new BranchWorkspaceContract
            {
                GitBranch = "feature/config",
                Profile = "feature/config",
                ExistingWikiNodeToken = "wik_feature"
            },
            SyncCache = new SyncCacheContract
            {
                ConfirmApply = true
            }
        };
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "sync-cache", "--manifest", requestPath, "--yes", "--out", resultPath });

        AssertEqual("2", exitCode.ToString(), "sync-cache apply should require --preview-result even when --yes is present.");
        AssertTrue(!File.Exists(resultPath), "sync-cache apply without preview-result should fail before writing a lifecycle result.");
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

static async Task TargetBranchBootstrapDryRunOverridesSeedTarget()
{
    var request = SampleTargetBootstrapRequest(dryRun: true);
    request.SeedFromLocalXlsx.Tables[0].Branch = "codex/config-sheet-seed-feishu-main";
    request.SeedFromLocalXlsx.Tables[0].Profile = "codex/config-sheet-seed-feishu-main";
    request.SeedFromLocalXlsx.Tables[0].SpreadsheetToken = "sht_source_should_not_leak";
    request.TargetBranchBootstrap.TableIds.Add("ItemsData");

    var result = await LifecycleExecutor.ExecuteAsync(request, new SeedRecordingPlatform(), CancellationToken.None);

    AssertTrue(result.Success, "Target bootstrap dry-run should produce a plan.");
    AssertEqual("main", result.Branch, "Bootstrap should switch lifecycle branch to target main.");
    AssertEqual("main", result.BranchWorkspace.NodeTitle, "Bootstrap should use the target node title.");
    var table = request.SeedFromLocalXlsx.Tables[0];
    AssertEqual("main", table.Profile, "Seed table profile should be overridden to target profile.");
    AssertEqual("", table.SpreadsheetToken, "Source branch spreadsheet locator must not leak into target bootstrap.");
    AssertTrue(result.Actions.Any(a => a.Action == "target_branch.bootstrap.plan"), "Result should expose a target bootstrap plan action.");
    AssertTrue(result.Actions.Any(a => a.Details.TryGetValue("tableCount", out var count) && count == "1"), "Plan should include table count.");
    AssertTrue(result.Actions.Any(a => a.Details.TryGetValue("requestFingerprint", out var fingerprint) && !string.IsNullOrWhiteSpace(fingerprint)), "Plan should include a request fingerprint for apply proof.");
}

static async Task TargetBranchBootstrapSafeApplySkipsAllLocalWrites()
{
    var request = SampleTargetBootstrapRequest(dryRun: false);
    request.TargetBranchBootstrap.ConfirmCreateOnlineSheets = true;
    request.TargetBranchBootstrap.ConfirmRegistryUpsert = true;
    request.TargetBranchBootstrap.ConfirmSchemaReviews = true;
    request.TargetBranchBootstrap.ConfirmWriteLocalCache = false;
    request.TargetBranchBootstrap.ConfirmWriteProjectConfig = false;
    request.TargetBranchBootstrap.ConfirmExcelToSoSettings = false;
    request.SeedFromLocalXlsx.Tables[0].UnityExcelToSo.SettingsPath = "ProjectSettings/ExcelToSO.json";
    var platform = new SeedRecordingPlatform();

    var result = await LifecycleExecutor.ExecuteAsync(request, platform, CancellationToken.None);

    AssertTrue(result.Success, "Safe target bootstrap apply should succeed with only online sheets, registry, and schema reviews confirmed.");
    AssertTrue(platform.Calls.Contains("ensure-online"), "Online sheet create/reuse must run.");
    AssertTrue(platform.Calls.Contains("branch-binding"), "BranchBindings upsert must run.");
    AssertTrue(platform.Calls.Contains("seed-registry"), "ConfigSheets upsert must run.");
    AssertTrue(platform.Calls.Contains("seed-schema"), "SchemaReviews upsert must run.");
    AssertTrue(!platform.Calls.Contains("cache"), "Local cache must not be written when confirmWriteLocalCache=false.");
    AssertTrue(!platform.Calls.Contains("project-config"), "ProjectSettings must not be written when confirmWriteProjectConfig=false.");
    AssertTrue(!platform.Calls.Contains("excel-to-so"), "ExcelToSO settings must not be written when confirmExcelToSoSettings=false.");
    AssertTrue(result.Actions.Any(a => a.Action == "target_branch.bootstrap.summary" && a.Details["localCacheWrite"] == "skipped" && a.Details["projectConfigWrite"] == "skipped" && a.Details["excelToSoWrite"] == "skipped"), "Summary should clearly mark local writes as skipped.");
    AssertTrue(result.Actions.Any(a => a.Action == "target_branch.bootstrap.postflight" && a.Status == "passed"), "Apply should run and pass postflight.");
}

static async Task TargetBranchBootstrapSkipsProjectConfigWithoutConfirmation()
{
    var request = SampleTargetBootstrapRequest(dryRun: false);
    request.TargetBranchBootstrap.ConfirmCreateOnlineSheets = true;
    request.TargetBranchBootstrap.ConfirmRegistryUpsert = true;
    request.TargetBranchBootstrap.ConfirmSchemaReviews = true;
    request.TargetBranchBootstrap.ConfirmWriteLocalCache = true;
    request.TargetBranchBootstrap.ConfirmWriteProjectConfig = false;
    var platform = new SeedRecordingPlatform();

    var result = await LifecycleExecutor.ExecuteAsync(request, platform, CancellationToken.None);

    AssertTrue(result.Success, "Target bootstrap apply should succeed when optional ProjectSettings write is not confirmed.");
    AssertTrue(!platform.Calls.Contains("project-config"), "ProjectSettings update must not run when confirmWriteProjectConfig=false.");
    AssertTrue(result.Actions.Any(a => a.Action == "seed.project_config.update" && a.Status == "skipped"), "Result should make skipped ProjectSettings write visible.");
}

static async Task TargetBranchBootstrapSkipsExcelToSoWithoutConfirmation()
{
    var request = SampleTargetBootstrapRequest(dryRun: false);
    request.SeedFromLocalXlsx.Tables[0].UnityExcelToSo.SettingsPath = "ProjectSettings/ExcelToSO.json";
    request.TargetBranchBootstrap.ConfirmCreateOnlineSheets = true;
    request.TargetBranchBootstrap.ConfirmRegistryUpsert = true;
    request.TargetBranchBootstrap.ConfirmSchemaReviews = true;
    request.TargetBranchBootstrap.ConfirmWriteLocalCache = true;
    request.TargetBranchBootstrap.ConfirmExcelToSoSettings = false;
    var platform = new SeedRecordingPlatform();

    var result = await LifecycleExecutor.ExecuteAsync(request, platform, CancellationToken.None);

    AssertTrue(result.Success, "Target bootstrap apply should not require ExcelToSO when that write is not confirmed.");
    AssertTrue(!platform.Calls.Contains("excel-to-so"), "ExcelToSO updater must not run when confirmExcelToSoSettings=false.");
    AssertTrue(result.Actions.Any(a => a.Action == "seed.unity.excel_to_so.upsert" && a.Status == "skipped"), "Result should make skipped ExcelToSO write visible.");
}

static async Task TargetBranchBootstrapRepeatedApplyReusesOnlineSheets()
{
    var platform = new SeedRecordingPlatform();
    var first = SampleTargetBootstrapRequest(dryRun: false);
    first.TargetBranchBootstrap.ConfirmCreateOnlineSheets = true;
    first.TargetBranchBootstrap.ConfirmRegistryUpsert = true;
    first.TargetBranchBootstrap.ConfirmSchemaReviews = true;
    var firstResult = await LifecycleExecutor.ExecuteAsync(first, platform, CancellationToken.None);
    AssertTrue(firstResult.Success, "First target bootstrap apply should pass.");

    var second = SampleTargetBootstrapRequest(dryRun: false);
    second.TargetBranchBootstrap.ConfirmCreateOnlineSheets = true;
    second.TargetBranchBootstrap.ConfirmRegistryUpsert = true;
    second.TargetBranchBootstrap.ConfirmSchemaReviews = true;
    var secondResult = await LifecycleExecutor.ExecuteAsync(second, platform, CancellationToken.None);

    AssertTrue(secondResult.Success, "Repeated target bootstrap apply should be idempotent in the platform contract.");
    var importAction = secondResult.SeedTables[0].Actions.First(a => a.Action == "seed.sheet.import_or_create");
    AssertEqual("true", importAction.Details["reused"], "Second run should reuse an existing online Sheet instead of reporting a new create.");
}

static async Task TargetBranchBootstrapPostflightFailureBlocksApplyResult()
{
    var request = SampleTargetBootstrapRequest(dryRun: false);
    request.TargetBranchBootstrap.ConfirmCreateOnlineSheets = true;
    request.TargetBranchBootstrap.ConfirmRegistryUpsert = true;
    request.TargetBranchBootstrap.ConfirmSchemaReviews = true;
    var platform = new SeedRecordingPlatform { PostflightMissingLocator = true };

    var result = await LifecycleExecutor.ExecuteAsync(request, platform, CancellationToken.None);

    AssertTrue(!result.Success, "Postflight missing locator should fail the lifecycle result.");
    AssertTrue(result.Actions.Any(a => a.Action == "target_branch.bootstrap.postflight" && a.Status == "failed"), "Failed postflight action should be visible.");
    AssertTrue(result.HumanReadableFailures.Any(f => f.Contains("postflight")), "Failure should be human-readable and mention postflight.");
}

static async Task ApplyContractTargetBranchBootstrapRequiresPreviewResult()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-bootstrap-preview-required-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        var request = SampleTargetBootstrapRequest(dryRun: false);
        request.TargetBranchBootstrap.ConfirmCreateOnlineSheets = true;
        request.TargetBranchBootstrap.ConfirmRegistryUpsert = true;
        request.TargetBranchBootstrap.ConfirmSchemaReviews = true;
        var requestPath = Path.Combine(root, "request.json");
        var resultPath = Path.Combine(root, "result.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, CamelJsonOptions()));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "apply-contract", "--request", requestPath, "--out", resultPath });

        AssertEqual("2", exitCode.ToString(), "Target bootstrap apply-contract must require a matching dry-run result.");
        AssertTrue(!File.Exists(resultPath), "Blocked apply should not emit a lifecycle result after preview validation failure.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ApplyContractTargetBranchBootstrapRejectsPreviewFingerprintMismatch()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-bootstrap-preview-mismatch-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        var request = SampleTargetBootstrapRequest(dryRun: false);
        request.TargetBranchBootstrap.ConfirmCreateOnlineSheets = true;
        request.TargetBranchBootstrap.ConfirmRegistryUpsert = true;
        request.TargetBranchBootstrap.ConfirmSchemaReviews = true;
        var requestPath = Path.Combine(root, "request.json");
        var resultPath = Path.Combine(root, "result.json");
        var previewPath = Path.Combine(root, "preview.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, CamelJsonOptions()));
        await File.WriteAllTextAsync(previewPath, JsonSerializer.Serialize(new LifecycleContractResult
        {
            Operation = "bootstrap-target-branch-from-local-xlsx",
            DryRun = true,
            Success = true,
            RequestFingerprint = "wrong-fingerprint"
        }, CamelJsonOptions()));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "apply-contract", "--request", requestPath, "--out", resultPath, "--preview-result", previewPath });

        AssertEqual("2", exitCode.ToString(), "Target bootstrap apply-contract should reject mismatched dry-run fingerprints.");
        AssertTrue(!File.Exists(resultPath), "Blocked mismatch apply should not write a result file.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ApplyContractCurrentBranchBootstrapRequiresPreviewResult()
{
    var root = Path.Combine(Path.GetTempPath(), "csforge-current-bootstrap-preview-required-" + Guid.NewGuid().ToString("N"));
    var old = Directory.GetCurrentDirectory();
    try
    {
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
        var request = new LifecycleContractRequest
        {
            Operation = "bootstrap-current-branch-from-target",
            DryRun = false,
            Git = new ContractGitSpec { Branch = "feature/config", Profile = "feature-config" },
            BranchWorkspace = new BranchWorkspaceContract
            {
                MainGitBranch = "main",
                MainFeishuBranch = "main",
                ProfileNameTemplate = "{gitBranch}",
                BranchNodeTitleTemplate = "branch-{slug}"
            },
            MergeInputs = new MergeInputsContract { TargetBranch = "main", TargetFeishuProfile = "main" },
            TargetBranchBootstrap = new TargetBranchBootstrapContract
            {
                ConfirmCreateOnlineSheets = true,
                ConfirmRegistryUpsert = true,
                ConfirmSchemaReviews = true
            }
        };
        request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract { TableId = "ItemsData", DisplayName = "Items" });
        request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract
        {
            TableId = "ItemsData",
            DisplayName = "Items",
            Profile = "main",
            SpreadsheetToken = "sht_items_main",
            SheetId = "sheet_items"
        });

        var requestPath = Path.Combine(root, "request.json");
        var resultPath = Path.Combine(root, "result.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, CamelJsonOptions()));

        var exitCode = await ConfigSheetForge.Cli.Program.Main(new[] { "apply-contract", "--request", requestPath, "--out", resultPath });

        AssertEqual("2", exitCode.ToString(), "Current branch bootstrap apply-contract must require a matching dry-run result.");
        AssertTrue(!File.Exists(resultPath), "Blocked current branch apply should fail before emitting a lifecycle result.");
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

static void TriangulationReportsRightSideExtraShape()
{
    var online = SampleWorkbook();
    var exported = SampleWorkbook();
    exported.Sheets[0].Columns.Add(new ColumnDefinition { Key = "bonus", DisplayName = "bonus", ValueKind = "string" });
    exported.Sheets[0].Rows[0].Cells["bonus"] = new CellValue { ValueKind = "string", RawText = "extra", NormalizedText = "extra" };
    exported.Sheets[0].Rows.Add(new RowDocument
    {
        StableId = "item_extra",
        SourceIndex = 99,
        Cells =
        {
            ["id"] = new CellValue { ValueKind = "string", RawText = "item_extra", NormalizedText = "item_extra" },
            ["name"] = new CellValue { ValueKind = "string", RawText = "Extra", NormalizedText = "Extra" },
            ["power"] = new CellValue { ValueKind = "integer", RawText = "1", NormalizedText = "1" }
        }
    });

    var fail = SemanticTriangulator.Compare(online, exported, SemanticTriangulator.Normalize(online));

    AssertTrue(!fail.Passed, "Right-side extra columns/rows should fail triangulation.");
    AssertTrue(fail.DiffSummary.Any(d => d.Contains("多出列") && d.Contains("bonus")), "Diff should report columns that only exist in exported-xlsx.");
    AssertTrue(fail.DiffSummary.Any(d => d.Contains("多出行") && d.Contains("item_extra")), "Diff should report rows that only exist in exported-xlsx.");
}

static void XlsxDimensionA1UsesSheetDataUsedRange()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-xlsx-dim-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var xlsx = Path.Combine(temp, "BuffData.xlsx");
        CreateMinimalXlsx(xlsx, withMergedCells: false, dimensionRef: "A1");
        var method = typeof(LarkCliWorkbookProvider).GetMethod("TryReadXlsxDimensions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        AssertTrue(method != null, "Expected private TryReadXlsxDimensions helper.");
        var parameters = new object[] { xlsx, 0, 0 };
        var ok = (bool)method!.Invoke(null, parameters)!;
        AssertTrue(ok, "Dimension helper should read xlsx dimensions.");
        AssertEqual("2", parameters[1].ToString() ?? "", "A stale A1 dimension should be expanded from sheetData rows.");
        AssertEqual("2", parameters[2].ToString() ?? "", "A stale A1 dimension should be expanded from sheetData columns.");
    }
    finally
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

static async Task LarkReadWrongStartRangeRetriesExplicitRange()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-lark-range-retry-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var log = Path.Combine(temp, "calls.log");
    var script = Path.Combine(temp, "lark-cli.ps1");
    await File.WriteAllTextAsync(script,
        "$ErrorActionPreference = 'Continue'\n" +
        "$log = '" + EscapePowerShellSingleQuoted(log) + "'\n" +
        "Add-Content -LiteralPath $log -Value ($args -join ' ')\n" +
        "if ($args -contains 'doctor') { exit 0 }\n" +
        "if ($args -contains '+export') { Write-Error '{\"code\":90202,\"msg\":\"export unavailable in test\"}'; exit 7 }\n" +
        "if ($args -contains '+info') { Write-Error '{\"code\":90202,\"msg\":\"info unavailable in test\"}'; exit 8 }\n" +
        "if ($args -contains '+read') {\n" +
        "  if (-not ($args -contains '--range')) { Write-Error '{\"code\":90202,\"msg\":\"wrong startRange=f83835\"}'; exit 3 }\n" +
        "  $i = [array]::IndexOf($args, '--range')\n" +
        "  $range = $args[$i + 1]\n" +
        "  if ($range -ne 'f83835!A1:T200') { Write-Error ('unexpected range ' + $range); exit 4 }\n" +
        "  Write-Output '{\"data\":{\"values\":[[\"id\",\"name\"],[\"item_001\",\"Sword\"]]}}'\n" +
        "  exit 0\n" +
        "}\n" +
        "Write-Output '{}'\n" +
        "exit 0\n");

    try
    {
        var provider = new LarkCliWorkbookProvider();
        var context = new ProviderContext { WorkspaceRoot = temp };
        context.Settings["larkCliPath"] = script;
        context.Settings["larkCliIdentity"] = "bot";
        var result = await provider.ExportAsync(context, new ProviderExportRequest
        {
            SpreadsheetTokenOrUrl = "TCkxsBImChmzXTt5HnEcOYOSnqf",
            TableId = "BuffData",
            SheetId = "f83835",
            CacheDirectory = Path.Combine(temp, "cache")
        }, CancellationToken.None);

        var calls = File.ReadAllText(log);
        AssertTrue(result.Workbook != null, "Provider should recover from wrong startRange by retrying an explicit A1 range.");
        AssertTrue(!result.Findings.Any(f => f.Severity == FindingSeverity.Error && f.Code == "lark.read_failed"), "Retry success should not leave lark.read_failed.");
        AssertTrue(result.Findings.Any(f => f.Code == "lark.read_retry_success" && f.Details.TryGetValue("retryRange", out var range) && range == "f83835!A1:T200"), "Retry finding should expose the explicit range.");
        AssertTrue(calls.Contains("sheets +read --spreadsheet-token TCkxsBImChmzXTt5HnEcOYOSnqf --sheet-id f83835 --as bot"), "The first read should exercise the no-range failure path with strict bot.");
        AssertTrue(calls.Contains("--range f83835!A1:T200"), "The provider should retry with an explicit range.");
        AssertTrue(!calls.Contains("--as user"), "Strict bot mode must not fallback to user.");
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }
}

static string EscapePowerShellSingleQuoted(string value)
{
    return (value ?? "").Replace("'", "''");
}

static async Task LarkReadUsesXlsxSheetDataRangeWhenDimensionIsStale()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "csforge-lark-stale-dimension-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    var sourceXlsx = Path.Combine(temp, "source.xlsx");
    CreateMinimalXlsx(sourceXlsx, withMergedCells: false, dimensionRef: "A1");
    var log = Path.Combine(temp, "calls.log");
    var script = Path.Combine(temp, "lark-cli.ps1");
    await File.WriteAllTextAsync(script,
        "$ErrorActionPreference = 'Continue'\n" +
        "$log = '" + EscapePowerShellSingleQuoted(log) + "'\n" +
        "$source = '" + EscapePowerShellSingleQuoted(sourceXlsx) + "'\n" +
        "Add-Content -LiteralPath $log -Value ($args -join ' ')\n" +
        "if ($args -contains 'doctor') { exit 0 }\n" +
        "if ($args -contains '+export') {\n" +
        "  $i = [array]::IndexOf($args, '--output-path')\n" +
        "  $out = $args[$i + 1]\n" +
        "  $parent = Split-Path -Parent $out\n" +
        "  if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }\n" +
        "  Copy-Item -LiteralPath $source -Destination $out -Force\n" +
        "  Write-Output '{\"ok\":true}'\n" +
        "  exit 0\n" +
        "}\n" +
        "if ($args -contains '+read') {\n" +
        "  $i = [array]::IndexOf($args, '--range')\n" +
        "  if ($i -lt 0) { Write-Error 'range required'; exit 3 }\n" +
        "  $range = $args[$i + 1]\n" +
        "  if ($range -ne 'f83835!A1:B2') { Write-Error ('unexpected range ' + $range); exit 4 }\n" +
        "  Write-Output '{\"data\":{\"values\":[[\"id\",\"name\"],[\"item_001\",\"Sword\"]]}}'\n" +
        "  exit 0\n" +
        "}\n" +
        "Write-Output '{}'\n" +
        "exit 0\n");

    try
    {
        var provider = new LarkCliWorkbookProvider();
        var context = new ProviderContext { WorkspaceRoot = temp };
        context.Settings["larkCliPath"] = script;
        context.Settings["larkCliIdentity"] = "bot";
        var result = await provider.ExportAsync(context, new ProviderExportRequest
        {
            SpreadsheetTokenOrUrl = "TCkxsBImChmzXTt5HnEcOYOSnqf",
            TableId = "BuffData",
            SheetId = "f83835",
            CacheDirectory = Path.Combine(temp, "cache")
        }, CancellationToken.None);

        AssertTrue(result.Workbook != null, "Provider should import online read workbook.");
        AssertTrue(!result.Findings.Any(f => f.Severity == FindingSeverity.Error), "Stale xlsx dimension should not block provider read.");
        AssertTrue(result.Findings.Any(f => f.Code == "lark.read_range" && f.Details.TryGetValue("finalRange", out var range) && range == "f83835!A1:B2"), "Read diagnostics should expose the corrected final range.");
        AssertTrue(result.Findings.Any(f => f.Code == "lark.read_range" && f.Details.TryGetValue("xlsxCellRows", out var rows) && rows == "2"), "Read diagnostics should expose xlsx sheetData row count.");
        var xlsxImport = XlsxWorkbookReader.Import(sourceXlsx, new MatrixWorkbookImportOptions
        {
            ProviderId = "xlsx",
            SourceId = sourceXlsx,
            SourceTitle = "BuffData",
            SheetId = "f83835",
            SheetName = "BuffData"
        });
        var triangulation = SemanticTriangulator.Compare(result.Workbook, xlsxImport.Workbook, SemanticTriangulator.Normalize(result.Workbook));
        AssertTrue(triangulation.Passed, "Complete online-read and exported-xlsx should triangulate even when xlsx dimension is stale A1.");
    }
    finally
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
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

static void ExcelToSoCacheDialectMapsPortablePrimitiveAliases()
{
    var workbook = SampleWorkbookWithColumns(("id", "integer"), ("damage", "number"), ("name", "string"), ("enabled", "bool"));
    var table = new TableConfig
    {
        Id = "ProjectileData",
        Name = "ProjectileData",
        UseExcelToSoCacheDialect = true,
        FieldRow = 0,
        TypeRow = 1,
        DescriptionRow = 2,
        DataStartRow = 3
    };

    var plan = BuildExcelToSoDialectPlanForTest(workbook, table, Directory.GetCurrentDirectory());

    AssertTrue(!plan.Errors.Any(), "Primitive portable aliases should not block ExcelToSO dialect planning.");
    AssertEqual("int,float,string,bool", string.Join(",", plan.TypeRow), "Primitive portable aliases should be written as ExcelToSO dialect.");
}

static void ExcelToSoCacheDialectRestoresJsonArraysFromSourceXlsx()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-excel-to-so-dialect-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var source = Path.Combine(temp, "SkillsData.xlsx");
        CreateTypeHintXlsx(source,
            new[] { "id", "tags", "weights", "ids" },
            new[] { "Int", "string[]", "float[]", "int[]" });
        var workbook = SampleWorkbookWithColumns(("id", "integer"), ("tags", "json"), ("weights", "json"), ("ids", "json"));
        var table = new TableConfig
        {
            Id = "SkillsData",
            Name = "SkillsData",
            LocalSourcePath = source,
            UseExcelToSoCacheDialect = true,
            FieldRow = 0,
            TypeRow = 1,
            DescriptionRow = 2,
            DataStartRow = 3
        };

        var plan = BuildExcelToSoDialectPlanForTest(workbook, table, temp);

        AssertTrue(!plan.Errors.Any(), "Json array columns with old Excel type hints should not block.");
        AssertEqual("int,string[],float[],int[]", string.Join(",", plan.TypeRow), "Json array columns should restore concrete ExcelToSO array types.");

        var output = Path.Combine(temp, "SkillsData.cache.xlsx");
        WriteExcelToSoCacheXlsxForTest(output, workbook, table, plan.TypeRow);
        var writtenTypes = ReadTypeRowFromXlsx(output, typeRow: 1);
        AssertEqual("int,string[],float[],int[]", string.Join(",", writtenTypes), "Formal cache xlsx should not contain canonical json/integer/number type tokens.");
    }
    finally
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

static void ExcelToSoCacheDialectRestoresJsonWithInferredTypeRow()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-excel-to-so-inferred-type-row-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var source = Path.Combine(temp, "SkillsData.xlsx");
        CreateTypeHintXlsx(source,
            new[] { "id", "fullicon", "activecooldown", "keys", "values", "costtype" },
            new[] { "Int", "String[]", "float[]", "string[]", "float[]", "int[]" },
            dimensionRef: "A1");
        var workbook = SampleWorkbookWithColumns(
            ("id", "integer"),
            ("fullicon", "json"),
            ("activecooldown", "json"),
            ("keys", "json"),
            ("values", "json"),
            ("costtype", "json"));
        var table = new TableConfig
        {
            Id = "SkillsData",
            Name = "SkillsData",
            LocalSourcePath = source,
            UseExcelToSoCacheDialect = true,
            FieldRow = 0,
            TypeRow = -1,
            DescriptionRow = -1,
            DataStartRow = -1
        };

        var plan = BuildExcelToSoDialectPlanForTest(workbook, table, temp);

        AssertTrue(!plan.Errors.Any(), "TypeRow=-1 should infer row 2 and still restore json columns from the old Excel type row.");
        AssertEqual("int,string[],float[],string[],float[],int[]", string.Join(",", plan.TypeRow), "Inferred type row should restore project-style json array columns.");
    }
    finally
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

static void ExcelToSoCacheDialectBlocksUnresolvedJson()
{
    var workbook = SampleWorkbookWithColumns(("id", "integer"), ("skillset", "json"));
    var table = new TableConfig
    {
        Id = "NpcUnitData",
        Name = "NpcUnitData",
        UseExcelToSoCacheDialect = true,
        FieldRow = 0,
        TypeRow = 1,
        DescriptionRow = 2,
        DataStartRow = 3
    };

    var plan = BuildExcelToSoDialectPlanForTest(workbook, table, Directory.GetCurrentDirectory());

    AssertTrue(plan.Errors.Any(e => e.Contains("json") && e.Contains("excelToSoType")), "Unresolved json should block with a schema/originalType repair hint.");
}

static async Task RepairCacheDialectRewritesXlsxTypeRowOffline()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-repair-dialect-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var state = Path.Combine(temp, ".config-sheet-forge");
        var cache = Path.Combine(state, "cache");
        var excelCache = Path.Combine(state, "excel-cache");
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(excelCache);
        await File.WriteAllTextAsync(Path.Combine(state, "config.json"), JsonSerializer.Serialize(new ForgeConfig(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var workbook = SampleWorkbookWithColumns(("id", "integer"), ("damage", "number"), ("name", "string"));
        await File.WriteAllTextAsync(Path.Combine(cache, "ProjectileData.semantic.json"), JsonSerializer.Serialize(workbook, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        await File.WriteAllTextAsync(Path.Combine(cache, "ProjectileData.sha256"), SemanticHasher.ComputeHash(workbook) + Environment.NewLine);
        var xlsx = Path.Combine(excelCache, "ProjectileData.xlsx");
        CreateTypeHintXlsx(xlsx, new[] { "id", "damage", "name" }, new[] { "integer", "number", "string" });

        var request = new LifecycleContractRequest
        {
            Operation = "repair-cache-dialect",
            DryRun = true,
            SeedFromLocalXlsx = new SeedFromLocalXlsxContract
            {
                CacheDirectory = ".config-sheet-forge/cache",
                ExcelCacheDirectory = ".config-sheet-forge/excel-cache"
            }
        };
        request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract
        {
            TableId = "ProjectileData",
            DisplayName = "ProjectileData",
            CacheXlsxPath = ".config-sheet-forge/excel-cache/ProjectileData.xlsx",
            SemanticCachePath = ".config-sheet-forge/cache/ProjectileData.semantic.json",
            FieldRow = 0,
            TypeRow = 1,
            DescriptionRow = 2,
            DataStartRow = 3,
            UnityExcelToSo = new UnityExcelToSoContract { SettingsPath = "ProjectSettings/ExcelToScriptableObjectSettings.asset", ExcelPath = ".config-sheet-forge/excel-cache/ProjectileData.xlsx" }
        });
        var manifest = Path.Combine(temp, "contract.json");
        var dryResultPath = Path.Combine(temp, "dry-result.json");
        var applyResultPath = Path.Combine(temp, "apply-result.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var oldDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(temp);
            var dry = await ConfigSheetForge.Cli.Program.Main(new[] { "repair-cache-dialect", "--manifest", manifest, "--dry-run", "--out", dryResultPath });
            AssertEqual("0", dry.ToString(), "repair-cache-dialect dry-run should pass.");
            AssertEqual("integer,number,string", string.Join(",", ReadTypeRowFromXlsx(xlsx, 1)), "dry-run must not rewrite xlsx.");

            var apply = await ConfigSheetForge.Cli.Program.Main(new[] { "repair-cache-dialect", "--manifest", manifest, "--yes", "--out", applyResultPath });
            AssertEqual("0", apply.ToString(), "repair-cache-dialect apply should pass.");
            AssertEqual("int,float,string", string.Join(",", ReadTypeRowFromXlsx(xlsx, 1)), "apply should rewrite only the physical ExcelToSO type row.");
            var result = JsonSerializer.Deserialize<LifecycleContractResult>(await File.ReadAllTextAsync(applyResultPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            AssertTrue(result != null && result.SyncCacheSummary.CacheStatus == "upToDate", "Apply result should explain the cache is importable after dialect repair.");
            AssertEqual("import-unity", result!.SyncCacheSummary.NextAction, "After repair apply, next action should import Unity assets.");
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

static async Task RepairCacheDialectScansStaleDimensionRightSideColumns()
{
    var temp = Path.Combine(Path.GetTempPath(), "csforge-repair-dialect-stale-dimension-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var state = Path.Combine(temp, ".config-sheet-forge");
        var cache = Path.Combine(state, "cache");
        var excelCache = Path.Combine(state, "excel-cache");
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(excelCache);
        await File.WriteAllTextAsync(Path.Combine(state, "config.json"), JsonSerializer.Serialize(new ForgeConfig(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var source = Path.Combine(temp, "Excel", "SkillsData.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        CreateTypeHintXlsx(source,
            new[] { "id", "fullicon", "activecooldown", "keys", "values", "costtype" },
            new[] { "Int", "String[]", "float[]", "string[]", "float[]", "int[]" },
            dimensionRef: "A1");

        var workbook = SampleWorkbookWithColumns(
            ("id", "integer"),
            ("fullicon", "json"),
            ("activecooldown", "json"),
            ("keys", "json"),
            ("values", "json"),
            ("costtype", "json"));
        await File.WriteAllTextAsync(Path.Combine(cache, "SkillsData.semantic.json"), JsonSerializer.Serialize(workbook, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        await File.WriteAllTextAsync(Path.Combine(cache, "SkillsData.sha256"), SemanticHasher.ComputeHash(workbook) + Environment.NewLine);
        var xlsx = Path.Combine(excelCache, "SkillsData.xlsx");
        CreateTypeHintXlsx(xlsx,
            new[] { "id", "fullicon", "activecooldown", "keys", "values", "costtype" },
            new[] { "integer", "json", "json", "json", "json", "json" },
            dimensionRef: "A1");

        var request = new LifecycleContractRequest
        {
            Operation = "repair-cache-dialect",
            DryRun = true,
            SeedFromLocalXlsx = new SeedFromLocalXlsxContract
            {
                CacheDirectory = ".config-sheet-forge/cache",
                ExcelCacheDirectory = ".config-sheet-forge/excel-cache"
            }
        };
        request.SeedFromLocalXlsx.Tables.Add(new SeedTableContract
        {
            TableId = "SkillsData",
            DisplayName = "SkillsData",
            SourceXlsxPath = "Excel/SkillsData.xlsx",
            CacheXlsxPath = ".config-sheet-forge/excel-cache/SkillsData.xlsx",
            SemanticCachePath = ".config-sheet-forge/cache/SkillsData.semantic.json",
            HashCachePath = ".config-sheet-forge/cache/SkillsData.sha256",
            FieldRow = 0,
            TypeRow = -1,
            DescriptionRow = -1,
            DataStartRow = -1,
            UnityExcelToSo = new UnityExcelToSoContract { SettingsPath = "ProjectSettings/ExcelToScriptableObjectSettings.asset", ExcelPath = ".config-sheet-forge/excel-cache/SkillsData.xlsx" }
        });

        var manifest = Path.Combine(temp, "contract.json");
        var dryResultPath = Path.Combine(temp, "dry-result.json");
        var applyResultPath = Path.Combine(temp, "apply-result.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var oldDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(temp);
            var dry = await ConfigSheetForge.Cli.Program.Main(new[] { "repair-cache-dialect", "--manifest", manifest, "--dry-run", "--out", dryResultPath });
            AssertEqual("0", dry.ToString(), "repair-cache-dialect dry-run should not skip stale-dimension right-side json cells.");
            var dryResult = JsonSerializer.Deserialize<LifecycleContractResult>(await File.ReadAllTextAsync(dryResultPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            AssertTrue(dryResult != null && dryResult.SyncCacheSummary.CacheStatus == "dialectOutdated", "Dry-run should detect dialectOutdated.");
            AssertTrue(dryResult!.SyncCacheSummary.ChangedTables.Contains("SkillsData"), "Dry-run should list SkillsData for repair.");

            var apply = await ConfigSheetForge.Cli.Program.Main(new[] { "repair-cache-dialect", "--manifest", manifest, "--yes", "--out", applyResultPath });
            AssertEqual("0", apply.ToString(), "repair-cache-dialect apply should pass.");
            AssertEqual("int,string[],float[],string[],float[],int[]", string.Join(",", ReadTypeRowFromXlsx(xlsx, 1)), "Apply should rewrite all right-side json cells despite stale dimension=A1.");
            var applyResult = JsonSerializer.Deserialize<LifecycleContractResult>(await File.ReadAllTextAsync(applyResultPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            AssertTrue(applyResult != null && applyResult.SyncCacheSummary.CacheStatus == "upToDate", "Apply result should recommend importing Unity assets next.");
            AssertEqual("import-unity", applyResult!.SyncCacheSummary.NextAction, "Apply next action should be import-unity.");
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

static void GateReportEvaluatorMarksValidWaiverAsWaived()
{
    var report = PrGateReportEvaluator.Evaluate(new PrGateReport
    {
        GitHead = "abc",
        Branch = "feature/config",
        Permissions = new GatePermissions { CanReadRegistry = true, CanReadSheets = true },
        MergeReview = new GateReviewState { Status = "" },
        SchemaReview = new GateReviewState { Status = "approved" },
        Waiver = new GateWaiverState
        {
            Approved = true,
            Status = "approved",
            ApprovedByRole = "configOwner",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1).ToString("o"),
            Branch = "feature/config",
            TableId = "__project_pr_gate__",
            RecordId = "rec_waiver",
            Reason = "临时放行人工审查记录。"
        }
    });

    AssertTrue(report.Passed, "valid waiver should make the gate pass.");
    AssertTrue(report.Waived, "report should explicitly mark waived=true.");
    AssertEqual("waived", report.GateState, "gateState should be waived.");
    AssertTrue(report.HumanReadableFailures.Count == 0, "valid waiver should not leave ordinary failure text.");
    AssertTrue(report.WaivedFailures.Any(f => f.Contains("合并审查")), "waived failures should retain what was bypassed for audit.");
    AssertEqual("rec_waiver", report.Waiver.RecordId, "waiver record id should be preserved.");
    AssertEqual("configOwner", report.Waiver.ApprovedByRole, "waiver approver should be preserved.");
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

static LifecycleContractRequest SampleTargetBootstrapRequest(bool dryRun)
{
    return new LifecycleContractRequest
    {
        Operation = "bootstrap-target-branch-from-local-xlsx",
        DryRun = dryRun,
        Locale = "zh-Hans",
        Registry = new RegistryContract { BaseToken = "base_mock" },
        Git = new ContractGitSpec
        {
            Branch = "codex/config-sheet-seed-feishu-main",
            Profile = "codex/config-sheet-seed-feishu-main",
            FeishuBranch = "codex/config-sheet-seed-feishu-main",
            Head = "abc"
        },
        BranchWorkspace = new BranchWorkspaceContract
        {
            RootWikiToken = "wik_root",
            RootWikiTitle = "项目配置表",
            GitBranch = "codex/config-sheet-seed-feishu-main",
            Profile = "codex/config-sheet-seed-feishu-main",
            FeishuBranch = "codex/config-sheet-seed-feishu-main",
            MainGitBranch = "main",
            MainFeishuBranch = "main",
            MainNodeTitle = "main",
            BranchNodeTitleTemplate = "branch-{slug}"
        },
        TargetBranchBootstrap = new TargetBranchBootstrapContract
        {
            TargetGitBranch = "main",
            TargetFeishuProfile = "main",
            TargetBranchWikiNodeTitle = "main",
            SourceMode = "local-xlsx"
        },
        SeedFromLocalXlsx = new SeedFromLocalXlsxContract
        {
            WikiRootToken = "wik_root",
            WikiParentTitle = "项目配置表",
            CacheDirectory = ".config-sheet-forge/cache",
            ExcelCacheDirectory = ".config-sheet-forge/excel-cache",
            Tables =
            {
                new SeedTableContract
                {
                    TableId = "ItemsData",
                    DisplayName = "道具",
                    SourceXlsxPath = "Assets/Config/ItemsData.xlsx",
                    CacheXlsxPath = ".config-sheet-forge/excel-cache/ItemsData.xlsx",
                    SemanticCachePath = ".config-sheet-forge/cache/ItemsData.semantic.json",
                    HashCachePath = ".config-sheet-forge/cache/ItemsData.sha256",
                    ProjectConfigPath = "ProjectSettings/Example.ConfigSheetForge.json",
                    SheetName = "道具",
                    FieldRow = 0,
                    TypeRow = 1,
                    DescriptionRow = 2,
                    DataStartRow = 3
                }
            }
        }
    };
}

static void CreateMinimalXlsx(string path, bool withMergedCells, string dimensionRef = "")
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
    var dimension = string.IsNullOrWhiteSpace(dimensionRef) ? "" : "<dimension ref=\"" + dimensionRef + "\"/>";
    AddZipText(archive, "xl/worksheets/sheet1.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
        """ + dimension + """
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

static void CreateTypeHintXlsx(string path, IReadOnlyList<string> fieldRow, IReadOnlyList<string> typeRow, string dimensionRef = "")
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
          <sheets><sheet name="Data" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """);
    AddZipText(archive, "xl/_rels/workbook.xml.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """);

    var dimension = string.IsNullOrWhiteSpace(dimensionRef) ? "" : "<dimension ref=\"" + dimensionRef + "\"/>";
    AddZipText(archive, "xl/worksheets/sheet1.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
        """ + dimension + """
          <sheetData>
        """ +
        BuildInlineStringRow(1, fieldRow) +
        BuildInlineStringRow(2, typeRow) +
        """
          </sheetData>
        </worksheet>
        """);
}

static string BuildInlineStringRow(int rowIndex, IReadOnlyList<string> values)
{
    var builder = new StringBuilder();
    builder.Append("<row r=\"").Append(rowIndex).Append("\">");
    for (var i = 0; i < values.Count; i++)
    {
        builder.Append("<c r=\"").Append(ToTestA1(i, rowIndex - 1)).Append("\" t=\"inlineStr\"><is><t>")
            .Append(System.Security.SecurityElement.Escape(values[i] ?? ""))
            .Append("</t></is></c>");
    }

    builder.Append("</row>");
    return builder.ToString();
}

static string ToTestA1(int columnIndex, int rowIndex)
{
    var columnNumber = columnIndex + 1;
    var name = "";
    while (columnNumber > 0)
    {
        var modulo = (columnNumber - 1) % 26;
        name = Convert.ToChar('A' + modulo) + name;
        columnNumber = (columnNumber - modulo) / 26;
    }

    return name + (rowIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

static WorkbookDocument SampleWorkbookWithColumns(params (string Key, string ValueKind)[] columns)
{
    var workbook = new WorkbookDocument { ProviderId = "test", SourceId = "test", SourceTitle = "Test" };
    var sheet = new SheetDocument { Id = "sheet1", Name = "TestData" };
    foreach (var column in columns)
    {
        sheet.Columns.Add(new ColumnDefinition
        {
            Key = column.Key,
            DisplayName = column.Key,
            SourceColumn = column.Key,
            ValueKind = column.ValueKind,
            Details = { ["description"] = column.Key + " desc" }
        });
    }

    var row = new RowDocument { StableId = "row1", SourceIndex = 4 };
    foreach (var column in sheet.Columns)
    {
        row.Cells[column.Key] = new CellValue { ValueKind = column.ValueKind, RawText = column.ValueKind == "json" ? "[1,2]" : "1", NormalizedText = column.ValueKind == "json" ? "[1,2]" : "1" };
    }

    sheet.Rows.Add(row);
    workbook.Sheets.Add(sheet);
    return workbook;
}

static (List<string> TypeRow, List<string> Errors) BuildExcelToSoDialectPlanForTest(WorkbookDocument workbook, TableConfig table, string workspaceRoot)
{
    var method = typeof(ConfigSheetForge.Cli.Program).GetMethod("BuildExcelToSoCacheDialectPlan", BindingFlags.NonPublic | BindingFlags.Static);
    AssertTrue(method != null, "BuildExcelToSoCacheDialectPlan should exist.");
    var plan = method!.Invoke(null, new object[] { workbook, table, workspaceRoot });
    AssertTrue(plan != null, "Dialect plan should not be null.");
    var typeRow = ((IEnumerable<string>)plan!.GetType().GetProperty("TypeRow")!.GetValue(plan)!).ToList();
    var errors = ((IEnumerable<string>)plan.GetType().GetProperty("Errors")!.GetValue(plan)!).ToList();
    return (typeRow, errors);
}

static void WriteExcelToSoCacheXlsxForTest(string path, WorkbookDocument workbook, TableConfig table, IList<string> typeRow)
{
    var method = typeof(ConfigSheetForge.Cli.Program).GetMethod("WriteExcelToSoCacheXlsx", BindingFlags.NonPublic | BindingFlags.Static);
    AssertTrue(method != null, "WriteExcelToSoCacheXlsx should exist.");
    method!.Invoke(null, new object[] { path, workbook, table, typeRow });
}

static List<string> ReadTypeRowFromXlsx(string path, int typeRow)
{
    using var archive = ZipFile.OpenRead(path);
    var entry = archive.GetEntry("xl/worksheets/sheet1.xml");
    AssertTrue(entry != null, "Generated xlsx should contain sheet1.xml.");
    using var stream = entry!.Open();
    var document = System.Xml.Linq.XDocument.Load(stream);
    var ns = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
    var row = document.Descendants(ns + "row").FirstOrDefault(r => (string?)r.Attribute("r") == (typeRow + 1).ToString());
    AssertTrue(row != null, "Generated xlsx should contain requested type row.");
    return row!.Elements(ns + "c")
        .Select(c => string.Concat(c.Descendants(ns + "t").Select(t => t.Value)))
        .ToList();
}

static void AddZipText(ZipArchive archive, string path, string text)
{
    var entry = archive.CreateEntry(path);
    using var writer = new StreamWriter(entry.Open());
    writer.Write(text.Trim());
}

static string ToVerbatimMixedPath(string path)
{
    var full = Path.GetFullPath(path);
    if (!OperatingSystem.IsWindows())
    {
        return full;
    }

    return @"\\?\" + full.Replace('\\', '/');
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertNoUtf8Bom(string path, string message)
{
    AssertTrue(File.Exists(path), message + " should exist.");
    var bytes = File.ReadAllBytes(path);
    var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    AssertTrue(!hasBom, message + " must be UTF-8 without BOM.");
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

sealed class SeedRecordingPlatform : ILifecyclePlatform, ISeedFromLocalXlsxPlatform, IBranchWorkspacePlatform, ITargetBranchBootstrapPostflightPlatform
{
    public List<string> Calls { get; } = new();
    public bool PostflightMissingLocator { get; set; }
    private readonly HashSet<string> _knownSheets = new(StringComparer.OrdinalIgnoreCase);

    public Task<RegistrySnapshot> GetRegistrySnapshotAsync(RegistryContract registry, CancellationToken cancellationToken)
    {
        Calls.Add("snapshot");
        return Task.FromResult(new RegistrySnapshot());
    }

    public Task<LifecycleActionResult> EnsureRegistryAsync(RegistryContract registry, RegistryDisplayMapping mapping, CancellationToken cancellationToken)
    {
        Calls.Add("ensure-registry");
        return Task.FromResult(new LifecycleActionResult { Action = "registry.ensure", Status = "done", Message = "mock registry" });
    }

    public Task<SheetCreationResult> CreateOnlineSheetAsync(ContractTableSpec table, CancellationToken cancellationToken)
    {
        Calls.Add("create-sheet");
        return Task.FromResult(new SheetCreationResult { SpreadsheetToken = "sht_mock", SpreadsheetUrl = "https://example.feishu.cn/sheets/sht_mock", SheetId = "sheet_mock", WikiNodeToken = "wik_sheet" });
    }

    public Task<LifecycleActionResult> WriteSheetTemplateAsync(SheetCreationResult sheet, IList<IList<string>> templateRows, CancellationToken cancellationToken)
    {
        Calls.Add("write-template");
        return Task.FromResult(new LifecycleActionResult { Action = "sheet.template.write", Status = "done", Message = "mock template" });
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
        return Task.FromResult(action);
    }

    public Task<LifecycleActionResult> ApplyRegistryMigrationAsync(RegistryContract registry, RegistryMigrationPlan plan, CancellationToken cancellationToken)
    {
        Calls.Add("apply-migration");
        return Task.FromResult(new LifecycleActionResult { Action = "registry.migration.apply", Status = "done", Message = "mock migration" });
    }

    public Task<BranchWorkspaceResolution> EnsureBranchWorkspaceAsync(BranchWorkspaceContract workspace, BranchWorkspaceResolution planned, CancellationToken cancellationToken)
    {
        Calls.Add("branch-workspace");
        planned.WikiNodeToken = string.IsNullOrWhiteSpace(planned.WikiNodeToken) ? "wik_main" : planned.WikiNodeToken;
        planned.WikiNodeUrl = string.IsNullOrWhiteSpace(planned.WikiNodeUrl) ? "https://example.feishu.cn/wiki/wik_main" : planned.WikiNodeUrl;
        planned.Status = "reused";
        return Task.FromResult(planned);
    }

    public Task<LifecycleActionResult> UpsertBranchBindingAsync(RegistryContract registry, BranchWorkspaceResolution resolution, CancellationToken cancellationToken)
    {
        Calls.Add("branch-binding");
        var action = new LifecycleActionResult { Action = "registry.branch_bindings.upsert", Status = "done", Message = "mock branch binding" };
        action.Details["recordId"] = "rec_branch";
        action.Details["gitBranch"] = resolution.GitBranch;
        action.Details["profile"] = resolution.Profile;
        return Task.FromResult(action);
    }

    public Task<SeedLocalWorkbookResult> ReadLocalXlsxAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, CancellationToken cancellationToken)
    {
        Calls.Add("read-local");
        var workbook = MinimalWorkbook(table);
        return Task.FromResult(new SeedLocalWorkbookResult
        {
            Workbook = workbook,
            SourceXlsxPath = table.SourceXlsxPath,
            SemanticHash = SemanticHasher.ComputeHash(workbook)
        });
    }

    public Task<SeedOnlineSheetResult> EnsureOnlineSheetAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, WorkbookDocument localWorkbook, string semanticHash, CancellationToken cancellationToken)
    {
        Calls.Add("ensure-online");
        var profile = !string.IsNullOrWhiteSpace(table.Profile) ? table.Profile : !string.IsNullOrWhiteSpace(table.Branch) ? table.Branch : "main";
        var reused = !_knownSheets.Add(profile + "/" + table.TableId);
        return Task.FromResult(new SeedOnlineSheetResult
        {
            SpreadsheetToken = "sht_" + table.TableId,
            SpreadsheetUrl = "https://example.feishu.cn/sheets/sht_" + table.TableId,
            SheetId = "sheet_" + table.TableId,
            WikiNodeToken = "wik_" + table.TableId,
            Created = !reused,
            Reused = reused,
            UsedRowCount = 2,
            UsedColumnCount = 2,
            ImportMode = "mock"
        });
    }

    public Task<SeedOnlineRoundTripResult> ReadAndExportOnlineSheetAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken)
    {
        Calls.Add("roundtrip");
        var workbook = MinimalWorkbook(table);
        return Task.FromResult(new SeedOnlineRoundTripResult
        {
            OnlineWorkbook = workbook,
            ExportedXlsxWorkbook = workbook,
            ExportedXlsxPath = "Temp/" + table.TableId + ".xlsx"
        });
    }

    public Task<LifecycleActionResult> WriteSeedCacheAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, WorkbookDocument localWorkbook, string semanticHash, string exportedXlsxPath, CancellationToken cancellationToken)
    {
        Calls.Add("cache");
        var action = new LifecycleActionResult { Action = "seed.cache.write", Status = "done", Message = "mock cache" };
        action.Details["xlsxPath"] = table.CacheXlsxPath;
        action.Details["semanticPath"] = table.SemanticCachePath;
        action.Details["shaPath"] = table.HashCachePath;
        action.Details["semanticHash"] = semanticHash;
        return Task.FromResult(action);
    }

    public Task<LifecycleActionResult> UpdateProjectConfigAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken)
    {
        Calls.Add("project-config");
        return Task.FromResult(new LifecycleActionResult { Action = "seed.project_config.update", Status = "done", Message = "mock project config" });
    }

    public Task<LifecycleActionResult> UpsertSeedRegistryRecordAsync(RegistryContract registry, SeedFromLocalXlsxContract seed, SeedTableContract table, SeedOnlineSheetResult sheet, CancellationToken cancellationToken)
    {
        Calls.Add("seed-registry");
        var action = new LifecycleActionResult { Action = "seed.registry.config_sheets.upsert", Status = "done", Message = "mock seed registry" };
        action.Details["recordId"] = "rec_" + table.TableId;
        return Task.FromResult(action);
    }

    public Task<LifecycleActionResult> UpsertSeedSchemaReviewAsync(RegistryContract registry, SeedFromLocalXlsxContract seed, SeedTableContract table, ContractGitSpec git, bool schemaChangeDetected, string reason, CancellationToken cancellationToken)
    {
        Calls.Add("seed-schema");
        var action = new LifecycleActionResult { Action = "seed.registry.schema_reviews.upsert", Status = "done", Message = "mock seed schema" };
        action.Details["schemaReviewId"] = "schema_" + table.TableId;
        return Task.FromResult(action);
    }

    public Task<LifecycleActionResult> UpdateExcelToSoSettingsAsync(SeedFromLocalXlsxContract seed, SeedTableContract table, CancellationToken cancellationToken)
    {
        Calls.Add("excel-to-so");
        return Task.FromResult(new LifecycleActionResult { Action = "seed.unity.excel_to_so.upsert", Status = "done", Message = "mock excel to so" });
    }

    public Task<LifecycleActionResult> ValidateTargetBranchBootstrapPostflightAsync(LifecycleContractRequest request, BranchWorkspaceResolution branchWorkspace, IList<SeedTableLifecycleResult> seedTables, CancellationToken cancellationToken)
    {
        Calls.Add("postflight");
        var action = new LifecycleActionResult
        {
            Action = "target_branch.bootstrap.postflight",
            Status = PostflightMissingLocator ? "failed" : "passed",
            Message = PostflightMissingLocator
                ? "postflight 发现目标分支缺少 ConfigSheets 定位：ItemsData。请确认 registry upsert 成功后重跑。"
                : "postflight 通过：mock 目标分支定位完整。"
        };
        action.Details["postflightPassed"] = (!PostflightMissingLocator).ToString().ToLowerInvariant();
        action.Details["verifiedConfigSheetCount"] = PostflightMissingLocator ? "0" : seedTables.Count.ToString();
        action.Details["missingConfigSheetLocators"] = PostflightMissingLocator ? "ItemsData" : "";
        return Task.FromResult(action);
    }

    private static WorkbookDocument MinimalWorkbook(SeedTableContract table)
    {
        var workbook = new WorkbookDocument { ProviderId = "mock", SourceId = table.SourceXlsxPath, SourceTitle = table.DisplayName };
        var sheet = new SheetDocument { Id = "sheet1", Name = string.IsNullOrWhiteSpace(table.SheetName) ? table.TableId : table.SheetName };
        sheet.Columns.Add(new ColumnDefinition { Key = "id", DisplayName = "ID", ValueKind = "string", Required = true });
        sheet.Columns.Add(new ColumnDefinition { Key = "name", DisplayName = "名称", ValueKind = "string" });
        var row = new RowDocument { StableId = "item_001", SourceIndex = 1 };
        row.Cells["id"] = new CellValue { RawText = "item_001", NormalizedText = "item_001", ValueKind = "string" };
        row.Cells["name"] = new CellValue { RawText = "测试", NormalizedText = "测试", ValueKind = "string" };
        sheet.Rows.Add(row);
        workbook.Sheets.Add(sheet);
        return workbook;
    }
}
