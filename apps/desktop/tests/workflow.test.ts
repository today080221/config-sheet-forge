import { describe, expect, it } from "vitest";
import {
  decidePrMerge,
  decideSyncImport,
  validateNewTableDraft,
  type LifecycleResultLike,
  type NewTableDraft
} from "../src/workflow";

function syncPreview(status: string, patch: Partial<LifecycleResultLike["syncCacheSummary"]> = {}): LifecycleResultLike {
  return {
    operation: "sync-cache",
    dryRun: true,
    success: status !== "blocked",
    requestFingerprint: "fp-sync",
    syncCacheSummary: {
      cacheStatus: status,
      tableCount: 16,
      changedTables: [],
      missingCacheTables: [],
      upToDateTables: status === "upToDate" ? ["ProjectileData"] : [],
      blockedTables: status === "blocked" ? ["BuffData"] : [],
      triangulationFailedCount: status === "blocked" ? 1 : 0,
      ...patch
    }
  };
}

describe("Desktop workflow state machine", () => {
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

    const decision = decideSyncImport({ lastSyncPreview: missingFingerprint });

    expect(decision.primaryOperation).toBe("sync-cache-apply");
    expect(decision.primaryDisabled).toBe(true);
    expect(decision.disabledReason).toContain("fingerprint");
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
      { key: "rarity", displayName: "稀有度", type: "enum", description: "稀有度枚举", enumValues: ["N", "R"] }
    ]
  };

  it("accepts structured safe field rows and enum values", () => {
    expect(validateNewTableDraft(validDraft)).toEqual([]);
  });

  it("rejects invalid free-form field keys and missing enum values", () => {
    const errors = validateNewTableDraft({
      ...validDraft,
      fields: [
        { key: "1 bad", displayName: "", type: "enum", description: "", enumValues: [], primary: false }
      ]
    });

    expect(errors.join(" ")).toContain("字段 key 不合法");
    expect(errors.join(" ")).toContain("至少需要一个枚举值");
    expect(errors.join(" ")).toContain("至少需要一个唯一 ID 字段");
  });
});
