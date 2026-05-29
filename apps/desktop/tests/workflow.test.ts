import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import {
  decidePrMerge,
  decideSyncImport,
  normalizeSyncCacheResult,
  ordinaryToolText,
  primaryToolAction,
  shouldShowBotSecretForm,
  summarizeLifecycleResult,
  syncResultSummaryLine,
  validateNewTableDraft,
  type LifecycleResultLike,
  type NewTableDraft,
  type ToolCheckLike
} from "../src/workflow";

const tableIds = Array.from({ length: 16 }, (_, index) => `Table${index + 1}`);

function syncPreview(status: string, patch: Partial<LifecycleResultLike["syncCacheSummary"]> = {}): LifecycleResultLike {
  return {
    operation: "sync-cache",
    dryRun: true,
    success: status !== "blocked",
    requestFingerprint: "fp-sync",
    previewFingerprint: "fp-sync",
    syncCacheSummary: {
      cacheStatus: status,
      previewFingerprint: "fp-sync",
      canApplyCache: status === "needsUpdate" || status === "missingCache",
      nextAction: status === "upToDate" ? "import-unity" : status === "blocked" ? "fix-blocker" : "write-cache",
      tableCount: 16,
      changedTables: [],
      missingCacheTables: [],
      upToDateTables: status === "upToDate" ? ["ProjectileData"] : [],
      blockedTables: status === "blocked" ? ["BuffData"] : [],
      triangulationFailedCount: status === "blocked" ? 1 : 0,
      tables: status === "needsUpdate"
        ? [{ tableId: "ProjectileData", displayName: "ProjectileData", cacheStatus: "needsUpdate", needsWriteCache: true }]
        : [],
      ...patch
    }
  };
}

function projectNeedsUpdateFixture(): LifecycleResultLike {
  return {
    schemaVersion: "config-sheet-forge.lifecycle/v1",
    operation: "sync-cache",
    dryRun: true,
    success: true,
    requestFingerprint: "77e4954905e9149690d45fca613a8ee997377b4547cdb0929aee00f195f34d3a",
    previewFingerprint: "77e4954905e9149690d45fca613a8ee997377b4547cdb0929aee00f195f34d3a",
    cacheStatus: null,
    nextAction: "write-cache",
    canApplyCache: true,
    syncCacheSummary: {
      cacheStatus: "needsUpdate",
      canApplyCache: true,
      nextAction: "write-cache",
      previewFingerprint: "77e4954905e9149690d45fca613a8ee997377b4547cdb0929aee00f195f34d3a",
      tableCount: 16,
      changedTables: [...tableIds],
      missingCacheTables: [],
      upToDateTables: [],
      blockedTables: [],
      triangulationFailedCount: 0,
      tables: tableIds.map((tableId) => ({
        tableId,
        displayName: tableId,
        cacheStatus: "needsUpdate",
        onlineSemanticHash: `online-${tableId}`,
        localSemanticHash: `local-${tableId}`,
        needsWriteCache: true,
        blockers: []
      }))
    }
  };
}

describe("Desktop workflow state machine", () => {
  it("normalizes sync-cache result from syncCacheSummary when top-level cacheStatus is null", () => {
    const normalized = normalizeSyncCacheResult(projectNeedsUpdateFixture());

    expect(normalized?.cacheStatus).toBe("needsUpdate");
    expect(normalized?.nextAction).toBe("write-cache");
    expect(normalized?.canApplyCache).toBe(true);
    expect(normalized?.changedTables).toHaveLength(16);
    expect(normalized?.tables).toHaveLength(16);
  });

  it("routes needsUpdate fixture to write-cache with a planner summary", () => {
    const fixture = projectNeedsUpdateFixture();
    const decision = decideSyncImport({ lastSyncPreview: fixture });

    expect(decision.primaryOperation).toBe("sync-cache-apply");
    expect(decision.primaryLabel).toBe("写入本地 cache（16 张）");
    expect(decision.primaryLabel).not.toBe("预览同步");
    expect(summarizeLifecycleResult(fixture)).toBe("预览通过，16 张表需要写入 cache，下一步：写入本地 cache。");
    expect(syncResultSummaryLine(fixture)).toContain("下一步：写入本地 cache");
  });

  it("restores write-cache state from an existing desktop sync-cache result file", () => {
    const root = mkdtempSync(join(tmpdir(), "csforge-desktop-restore-"));
    const resultDir = join(root, "Temp", "ConfigSheetForge", "desktop");
    mkdirSync(resultDir, { recursive: true });
    const resultPath = join(resultDir, "sync-cache.result.json");
    writeFileSync(resultPath, JSON.stringify(projectNeedsUpdateFixture(), null, 2), "utf8");

    const restored = JSON.parse(readFileSync(resultPath, "utf8")) as LifecycleResultLike;
    const decision = decideSyncImport({ lastSyncPreview: restored });
    const normalized = normalizeSyncCacheResult(restored);

    expect(decision.conclusion).not.toContain("还没有同步预览");
    expect(decision.primaryOperation).toBe("sync-cache-apply");
    expect(decision.primaryLabel).toContain("写入本地 cache");
    expect(normalized?.changedTables).toHaveLength(16);
  });

  it("planner sync result smoke has no debug noise and shows the next action", () => {
    const summary = syncResultSummaryLine(projectNeedsUpdateFixture());

    expect(summary).toContain("预览通过");
    expect(summary).toContain("16 张表");
    expect(summary).toContain("写入本地 cache");
    for (const leak of ["{", "}", "task-", "PID", "stack trace", "System.IO", "config-sheet-forge sync-cache"]) {
      expect(summary).not.toContain(leak);
    }
  });

  it("routes upToDate sync preview to Unity import instead of writing cache", () => {
    const decision = decideSyncImport({
      lastSyncPreview: syncPreview("upToDate"),
      bridgeSessionDir: "Library/ConfigSheetForge/DesktopBridge/session"
    });

    expect(decision.primaryOperation).toBe("unity-import");
    expect(decision.primaryLabel).toBe("导入 Unity 配表资产");
    expect(decision.programSummary).toContain("SourceOfTruthCache");
  });

  it("requires a same-input preview fingerprint before cache apply", () => {
    const missingFingerprint = syncPreview("needsUpdate", {
      changedTables: ["ProjectileData"]
    });
    missingFingerprint.requestFingerprint = "";
    missingFingerprint.previewFingerprint = "";
    missingFingerprint.syncCacheSummary!.previewFingerprint = "";

    const decision = decideSyncImport({ lastSyncPreview: missingFingerprint });

    expect(decision.primaryOperation).toBe("sync-cache-apply");
    expect(decision.primaryDisabled).toBe(true);
    expect(decision.disabledReason).toContain("fingerprint");
  });

  it("routes successful needsUpdate preview to cache apply even when legacy changedTables is empty", () => {
    const decision = decideSyncImport({ lastSyncPreview: syncPreview("needsUpdate") });

    expect(decision.primaryOperation).toBe("sync-cache-apply");
    expect(decision.primaryLabel).toContain("写入本地 cache");
    expect(decision.primaryDisabled).toBe(false);
  });

  it("blocks cache apply when sync dry-run is blocked", () => {
    const decision = decideSyncImport({ lastSyncPreview: syncPreview("blocked") });

    expect(decision.primaryOperation).toBe("sync-cache-dry-run");
    expect(decision.primaryLabel).toBe("重新预览同步");
    expect(decision.warnings.join(" ")).toContain("BuffData");
  });

  it("moves PR flow from compare preview to review submit and gate", () => {
    const compare = {
      operation: "compare-merge",
      dryRun: true,
      success: true,
      requestFingerprint: "fp-merge"
    };

    const reviewDecision = decidePrMerge({ lastComparePreview: compare });
    expect(reviewDecision.primaryOperation).toBe("submit-merge-review");

    const gateDecision = decidePrMerge({
      lastComparePreview: compare,
      lastGateReport: { prGateReport: { passed: true, gateState: "passed", waived: false } }
    });
    expect(gateDecision.primaryDisabled).toBe(true);
    expect(gateDecision.conclusion).toContain("已通过");
  });
});

describe("new table validation", () => {
  const validDraft: NewTableDraft = {
    tableId: "SkillExtraData",
    displayName: "技能扩展数据",
    ownerRole: "tableOwner",
    fields: [
      { key: "id", displayName: "ID", type: "string", description: "唯一ID", primary: true },
      { key: "rarity", displayName: "稀有度", type: "int", description: "稀有度数值" }
    ]
  };

  it("accepts structured ExcelToSO field rows", () => {
    expect(validateNewTableDraft(validDraft)).toEqual([]);
  });

  it("rejects invalid free-form field keys and unsupported types", () => {
    const errors = validateNewTableDraft({
      ...validDraft,
      fields: [
        { key: "1 bad", displayName: "", type: "json" as any, description: "", primary: false }
      ]
    });

    expect(errors.join(" ")).toContain("字段 key 不合法");
    expect(errors.join(" ")).toContain("ExcelToSO");
    expect(errors.join(" ")).toContain("至少需要一个唯一 ID 字段");
  });
});

describe("tool/auth UX helpers", () => {
  it("does not show a primary GitHub auth button once gh is authenticated", () => {
    const check: ToolCheckLike = {
      name: "gh",
      status: "ok",
      installed: true,
      authenticated: true,
      accountLabel: "today080221",
      nextAction: "none"
    };

    expect(primaryToolAction(check)).toBeNull();
  });

  it("shows GitHub auth when gh is installed but not authenticated", () => {
    const check: ToolCheckLike = {
      name: "gh",
      status: "needsAuth",
      installed: true,
      authenticated: false,
      nextAction: "gh_auth",
      nextActionLabel: "GitHub 授权"
    };

    expect(primaryToolAction(check)?.label).toBe("GitHub 授权");
  });

  it("turns lark-cli JSON doctor output into human text in ordinary view", () => {
    const text = ordinaryToolText("{\"ok\":true,\"identity\":\"bot\"}");

    expect(text).toContain("bot");
    expect(text.startsWith("{")).toBe(false);
  });

  it("hides the bot secret form when bot is already configured", () => {
    const check: ToolCheckLike = {
      name: "lark-cli",
      status: "ok",
      installed: true,
      authenticated: true,
      botConfigured: true
    };

    expect(shouldShowBotSecretForm(check, false)).toBe(false);
    expect(shouldShowBotSecretForm(check, true)).toBe(true);
  });
});
