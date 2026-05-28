export type ScenarioId = "environment" | "sync-import" | "pr-merge" | "new-table" | "branch-bootstrap";

export type ViewMode = "planner" | "programmer";

export type ToolStatus = "ok" | "warning" | "error";

export type ToolCheckLike = {
  name: string;
  status: ToolStatus;
  summary?: string;
  detail?: string;
};

export type ProjectSnapshotLike = {
  projectRoot?: string;
  gitBranch?: string;
  feishuProfile?: string;
  prNumber?: string;
  prUrl?: string;
};

export type SyncCacheSummaryLike = {
  cacheStatus?: string;
  tableCount?: number;
  changedTables?: string[];
  missingCacheTables?: string[];
  upToDateTables?: string[];
  blockedTables?: string[];
  triangulationFailedCount?: number;
  willWriteFiles?: boolean;
  noChangeKeepsMtime?: boolean;
};

export type BranchStatusLike = {
  branchBindingStatus?: string;
  tableCountExpected?: number;
  tableCountRegistered?: number;
  missingTables?: string[];
  missingLocators?: string[];
  nextRecommendedAction?: string;
};

export type PrGateReportLike = {
  passed?: boolean;
  gateState?: string;
  waived?: boolean;
  humanReadableFailures?: string[];
  waivedFailures?: string[];
};

export type LifecycleResultLike = {
  operation?: string;
  dryRun?: boolean;
  success?: boolean;
  requestFingerprint?: string;
  humanReadableFailures?: string[];
  syncCacheSummary?: SyncCacheSummaryLike;
  branchStatus?: BranchStatusLike;
  prGateReport?: PrGateReportLike;
};

export type WorkflowState = {
  snapshot?: ProjectSnapshotLike | null;
  checks?: ToolCheckLike[];
  lastScenario?: ScenarioId;
  lastSyncPreview?: LifecycleResultLike | null;
  lastSyncApply?: LifecycleResultLike | null;
  lastComparePreview?: LifecycleResultLike | null;
  lastGateReport?: LifecycleResultLike | PrGateReportLike | null;
  bridgeSessionDir?: string;
  activeOperation?: string;
};

export type ScenarioStep = {
  title: string;
  status: "done" | "active" | "blocked" | "pending";
};

export type ScenarioDefinition = {
  id: ScenarioId;
  title: string;
  shortTitle: string;
  description: string;
  safeNote: string;
};

export type WorkflowDecision = {
  scenarioId: ScenarioId;
  title: string;
  conclusion: string;
  nextStep: string;
  primaryLabel: string;
  primaryOperation: string;
  primaryDisabled: boolean;
  disabledReason: string;
  safety: string;
  programSummary: string;
  steps: ScenarioStep[];
  warnings: string[];
  debugHints: string[];
};

export type FieldType = "string" | "integer" | "number" | "bool" | "date" | "datetime" | "enum" | "json";

export type NewTableFieldDraft = {
  key: string;
  displayName: string;
  type: FieldType;
  description: string;
  enumValues?: string[];
  primary?: boolean;
};

export type NewTableDraft = {
  tableId: string;
  displayName: string;
  ownerRole: string;
  fields: NewTableFieldDraft[];
};

export const scenarios: ScenarioDefinition[] = [
  {
    id: "environment",
    title: "环境/授权",
    shortTitle: "环境",
    description: "先把 Git、GitHub CLI、飞书 CLI 和 bot/user 授权准备好。",
    safeNote: "只检查或安装本机工具，不改项目文件。"
  },
  {
    id: "sync-import",
    title: "同步并导入 Unity",
    shortTitle: "同步",
    description: "从飞书在线表同步到本地 cache，再导入 Unity ScriptableObject。",
    safeNote: "预览只读取；写 cache 只写 .config-sheet-forge；导入只写 Unity asset。"
  },
  {
    id: "pr-merge",
    title: "准备 PR 合并",
    shortTitle: "合并",
    description: "生成合并预览、提交合并审查记录，再运行 PR gate。",
    safeNote: "合并预览不写 main；提交审查只写 MergeReviews。"
  },
  {
    id: "new-table",
    title: "新建配表",
    shortTitle: "新表",
    description: "用结构化表单创建在线表和登记信息。",
    safeNote: "预览不写入；创建在线表需要二次确认。"
  },
  {
    id: "branch-bootstrap",
    title: "从 main/PR base 派生当前分支",
    shortTitle: "派生",
    description: "当前分支还没有在线工作区时，从目标分支复制在线表。",
    safeNote: "预览不写入；apply 默认不写本地 cache、ProjectSettings 或 ExcelToSO。"
  }
];

const scenarioById = new Map(scenarios.map((scenario) => [scenario.id, scenario]));

export function getScenario(id: ScenarioId): ScenarioDefinition {
  return scenarioById.get(id) ?? scenarios[0];
}

export function normalizeCacheStatus(value: string | undefined): string {
  return (value || "unknown").trim().toLowerCase();
}

export function hasBlockingToolIssue(checks: ToolCheckLike[] | undefined): boolean {
  return (checks || []).some((check) => check.status === "error" && check.name !== "gh");
}

export function decideRecommendedScenario(state: WorkflowState): ScenarioId {
  if (hasBlockingToolIssue(state.checks)) {
    return "environment";
  }

  const branchStatus = state.lastSyncPreview?.branchStatus ?? state.lastSyncApply?.branchStatus;
  if (branchStatus?.nextRecommendedAction === "bootstrap-current-branch-from-target" ||
      branchStatus?.branchBindingStatus === "missing") {
    return "branch-bootstrap";
  }

  if (state.lastScenario) {
    return state.lastScenario;
  }

  return "sync-import";
}

export function decideWorkflow(scenarioId: ScenarioId, state: WorkflowState): WorkflowDecision {
  switch (scenarioId) {
    case "environment":
      return decideEnvironment(state);
    case "sync-import":
      return decideSyncImport(state);
    case "pr-merge":
      return decidePrMerge(state);
    case "new-table":
      return decideNewTable(state);
    case "branch-bootstrap":
      return decideBranchBootstrap(state);
    default:
      return decideSyncImport(state);
  }
}

export function decideSyncImport(state: WorkflowState): WorkflowDecision {
  const preview = state.lastSyncPreview;
  const summary = preview?.syncCacheSummary;
  const status = normalizeCacheStatus(summary?.cacheStatus);
  const blockedTables = summary?.blockedTables ?? [];
  const changedCount = (summary?.changedTables?.length ?? 0) + (summary?.missingCacheTables?.length ?? 0);
  const tableCount = summary?.tableCount ?? 0;
  const base = baseDecision("sync-import", state);
  const hasPreview = Boolean(preview);

  if (state.activeOperation) {
    return {
      ...base,
      conclusion: "后台任务正在运行",
      nextStep: "等待当前任务完成，或到 Debug 查看日志。",
      primaryLabel: "正在运行...",
      primaryOperation: "",
      primaryDisabled: true,
      disabledReason: "后台任务运行中，完成后会自动恢复。",
      steps: syncSteps("active-preview")
    };
  }

  if (!hasPreview) {
    return {
      ...base,
      conclusion: "还没有同步预览",
      nextStep: "先预览同步计划，确认在线表和本地 cache 的差异。",
      primaryLabel: "预览同步",
      primaryOperation: "sync-cache-dry-run",
      primaryDisabled: false,
      safety: "只读取飞书和本地 cache，不写飞书、不写本地文件。",
      programSummary: "调用 sync-cache dry-run，输出到 Temp/ConfigSheetForge/desktop/sync-cache.result.json。",
      steps: syncSteps("preview")
    };
  }

  if (!preview?.success || status === "blocked" || blockedTables.length > 0 || (summary?.triangulationFailedCount ?? 0) > 0) {
    return {
      ...base,
      conclusion: "同步预检被阻断",
      nextStep: "先修复在线读取、范围、schema 或三方一致性问题。",
      primaryLabel: "重新预览同步",
      primaryOperation: "sync-cache-dry-run",
      primaryDisabled: false,
      safety: "仍然只读，不会写 cache。",
      programSummary: "blocked 时不会允许 sync-cache apply。请在 Debug 查看 attemptedRange/finalRange 和 result JSON。",
      steps: syncSteps("blocked"),
      warnings: [
        blockedTables.length > 0 ? `阻断表：${blockedTables.join("、")}` : "同步结果失败，但未返回具体阻断表。",
        ...(preview?.humanReadableFailures ?? [])
      ],
      debugHints: ["blockedTables", "attemptedRange", "finalRange", "syncCacheSummary"]
    };
  }

  if (status === "uptodate") {
    return {
      ...base,
      conclusion: tableCount > 0 ? `${tableCount}/${tableCount} 已同步，cache 已最新` : "本地 cache 已最新",
      nextStep: state.bridgeSessionDir ? "下一步导入 Unity 配表资产。" : "请回 Unity 导入配表资产。",
      primaryLabel: state.bridgeSessionDir ? "导入 Unity 配表资产" : "回 Unity 导入资产",
      primaryOperation: "unity-import",
      primaryDisabled: !state.bridgeSessionDir,
      disabledReason: state.bridgeSessionDir ? "" : "当前不是从 Unity bridge 启动，Desktop 无法直接调用 Unity Editor。",
      safety: "只调用 Unity bridge 导入 asset，不写飞书、不写旧 Excel/。",
      programSummary: "使用 ExcelToSO ImportByProfile(SourceOfTruthCache)。",
      steps: syncSteps("import")
    };
  }

  if (status === "needsupdate" || status === "missingcache") {
    const count = Math.max(1, changedCount);
    return {
      ...base,
      conclusion: status === "missingcache" ? "本地 cache 缺失" : "本地 cache 需要更新",
      nextStep: "写入本地 cache 后再导入 Unity。",
      primaryLabel: `写入本地 cache（${count} 张）`,
      primaryOperation: "sync-cache-apply",
      primaryDisabled: !preview?.requestFingerprint,
      disabledReason: preview?.requestFingerprint ? "" : "缺少同步预览 fingerprint，请重新生成预览。",
      safety: "只写 .config-sheet-forge/excel-cache 和 .config-sheet-forge/cache，不写旧 Excel/ 或飞书。",
      programSummary: "调用 sync-cache --yes --preview-result <dry-run result>，CLI 会校验同输入预览。",
      steps: syncSteps("apply")
    };
  }

  return {
    ...base,
    conclusion: "同步状态未知",
    nextStep: "重新预览同步计划，拿到明确 cacheStatus。",
    primaryLabel: "预览同步",
    primaryOperation: "sync-cache-dry-run",
    primaryDisabled: false,
    safety: "只读预览，不写文件。",
    programSummary: "cacheStatus unknown 时不会推荐写 cache。",
    steps: syncSteps("preview")
  };
}

export function decidePrMerge(state: WorkflowState): WorkflowDecision {
  const base = baseDecision("pr-merge", state);
  const gate = extractPrGate(state.lastGateReport);

  if (gate?.passed || gate?.gateState === "passed") {
    return {
      ...base,
      conclusion: "PR gate 已通过",
      nextStep: "可以回到 GitHub 合并 PR。",
      primaryLabel: "完成",
      primaryOperation: "",
      primaryDisabled: true,
      disabledReason: "当前流程已经完成。",
      safety: "不会再执行写入。",
      programSummary: "gateState=passed。",
      steps: prSteps("gate")
    };
  }

  if (!state.lastComparePreview?.success) {
    return {
      ...base,
      conclusion: "还没有合并预览",
      nextStep: "先生成合并预览，确认 source/target 在线表差异。",
      primaryLabel: "生成合并预览",
      primaryOperation: "compare-merge",
      primaryDisabled: false,
      safety: "只生成预览，不写 main、不写 cache。",
      programSummary: "调用 compare-merge dry-run，结果包含 mergeReportPath 和 requestFingerprint。",
      steps: prSteps("compare")
    };
  }

  if (state.lastComparePreview.success && !gate?.passed) {
    return {
      ...base,
      conclusion: "合并预览已生成",
      nextStep: "提交合并审查记录，然后运行 PR gate。",
      primaryLabel: "提交合并审查记录",
      primaryOperation: "submit-merge-review",
      primaryDisabled: !state.lastComparePreview.requestFingerprint,
      disabledReason: state.lastComparePreview.requestFingerprint ? "" : "合并预览缺少 requestFingerprint，请重新生成。",
      safety: "只写 Base MergeReviews，不写 main、不写 cache、不改 ProjectSettings。",
      programSummary: "提交前会带 compare preview result / requestFingerprint。",
      steps: prSteps("review")
    };
  }

  return {
    ...base,
    conclusion: "等待 PR 检查",
    nextStep: "运行 PR gate。",
    primaryLabel: "运行 PR 检查",
    primaryOperation: "pr-gate-report",
    primaryDisabled: false,
    safety: "PR hard gate 使用 strict bot，不允许静默 user fallback。",
    programSummary: "输出 Temp/ConfigSheetForge/pr-gate-report.json。",
    steps: prSteps("gate")
  };
}

export function decideEnvironment(state: WorkflowState): WorkflowDecision {
  const base = baseDecision("environment", state);
  const issues = (state.checks || []).filter((check) => check.status !== "ok");
  return {
    ...base,
    conclusion: issues.length === 0 && (state.checks?.length ?? 0) > 0 ? "本机环境可用" : "需要检查本机环境",
    nextStep: issues.length > 0 ? `先处理：${issues.map((issue) => issue.name).join("、")}` : "可以开始同步预览。",
    primaryLabel: "开始检查",
    primaryOperation: "doctor",
    primaryDisabled: false,
    safety: "只检查本机工具和授权状态。",
    programSummary: "检查 sidecar CLI、git、gh、lark-cli 和飞书直连环境。",
    steps: [
      { title: "识别项目", status: state.snapshot?.projectRoot ? "done" : "active" },
      { title: "检查工具", status: issues.length > 0 ? "blocked" : "done" },
      { title: "确认授权", status: issues.length > 0 ? "pending" : "done" }
    ],
    warnings: issues.map((issue) => `${issue.name}: ${issue.summary || issue.detail || "需要处理"}`)
  };
}

export function decideBranchBootstrap(state: WorkflowState): WorkflowDecision {
  const base = baseDecision("branch-bootstrap", state);
  return {
    ...base,
    conclusion: "当前分支可能还没有在线工作区",
    nextStep: "从 main 或 PR base 派生当前分支在线表。",
    primaryLabel: "预览派生当前分支",
    primaryOperation: "bootstrap-current-branch-from-target-dry-run",
    primaryDisabled: false,
    safety: "预览不写飞书；apply 默认不写本地 cache、ProjectSettings 或 ExcelToSO。",
    programSummary: "调用 bootstrap-current-branch-from-target dry-run，以 PR base/main 为 target。",
    steps: [
      { title: "定位目标分支", status: "done" },
      { title: "预览派生", status: "active" },
      { title: "确认写飞书工作区", status: "pending" },
      { title: "重新同步", status: "pending" }
    ]
  };
}

export function decideNewTable(state: WorkflowState): WorkflowDecision {
  const base = baseDecision("new-table", state);
  return {
    ...base,
    conclusion: "准备新建在线配表",
    nextStep: "填写配表 ID、中文名、负责人和字段，然后生成预览。",
    primaryLabel: "预览新建配表",
    primaryOperation: "new-table-dry-run",
    primaryDisabled: false,
    safety: "预览只生成计划，不创建在线表。",
    programSummary: "字段类型使用下拉，enum 需要至少一个枚举值。",
    steps: [
      { title: "填写表信息", status: "active" },
      { title: "预览创建", status: "pending" },
      { title: "负责人确认", status: "pending" },
      { title: "创建在线表", status: "pending" }
    ]
  };
}

export function validateNewTableDraft(draft: NewTableDraft): string[] {
  const errors: string[] = [];
  if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(draft.tableId || "")) {
    errors.push("配表ID 只能使用英文、数字、下划线，且不能以数字开头。");
  }

  if (!(draft.displayName || "").trim()) {
    errors.push("请填写给策划看的中文显示名称。");
  }

  const keys = new Set<string>();
  let hasPrimary = false;
  for (const field of draft.fields || []) {
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(field.key || "")) {
      errors.push(`字段 key 不合法：${field.key || "空"}`);
    }

    const normalizedKey = (field.key || "").toLowerCase();
    if (normalizedKey && keys.has(normalizedKey)) {
      errors.push(`字段 key 重复：${field.key}`);
    }
    keys.add(normalizedKey);

    if (!(field.displayName || "").trim()) {
      errors.push(`字段 ${field.key || "未命名"} 缺少中文名。`);
    }

    if (!(field.description || "").trim()) {
      errors.push(`字段 ${field.key || "未命名"} 缺少说明。`);
    }

    if (!["string", "integer", "number", "bool", "date", "datetime", "enum", "json"].includes(field.type)) {
      errors.push(`字段 ${field.key || "未命名"} 类型不在安全类型列表里。`);
    }

    if (field.type === "enum" && (field.enumValues || []).filter((value) => value.trim()).length === 0) {
      errors.push(`枚举字段 ${field.key || "未命名"} 至少需要一个枚举值。`);
    }

    if (field.primary) {
      hasPrimary = true;
    }
  }

  if (!hasPrimary) {
    errors.push("至少需要一个唯一 ID 字段。");
  }

  return Array.from(new Set(errors));
}

export function summarizeLifecycleResult(result: LifecycleResultLike | PrGateReportLike | null | undefined): string {
  if (!result) {
    return "暂无结果。";
  }

  const lifecycle = result as LifecycleResultLike;
  const gate = extractPrGate(result);
  if (gate) {
    if (gate.passed || gate.gateState === "passed") {
      return "PR gate 已通过。";
    }

    if (gate.waived || gate.gateState === "waived") {
      return "PR gate 已由负责人临时放行。";
    }
  }

  if (lifecycle.operation === "sync-cache" || lifecycle.syncCacheSummary) {
    const summary = lifecycle.syncCacheSummary;
    const status = normalizeCacheStatus(summary?.cacheStatus);
    if (status === "uptodate") {
      return "同步预览通过，本地 cache 已最新。";
    }

    if (status === "needsupdate") {
      return `同步预览通过，${(summary?.changedTables || []).length || "若干"} 张表需要写入 cache。`;
    }

    if (status === "missingcache") {
      return "同步预览通过，但本地 cache 缺失。";
    }

    if (status === "blocked") {
      return `同步预检被阻断：${(summary?.blockedTables || []).join("、") || "请查看结果详情"}`;
    }
  }

  if (lifecycle.success === false) {
    return (lifecycle.humanReadableFailures || []).slice(0, 1)[0] || "任务失败，请查看详情。";
  }

  if (lifecycle.success === true) {
    return "任务完成。";
  }

  return "已收到结果。";
}

function baseDecision(scenarioId: ScenarioId, state: WorkflowState): WorkflowDecision {
  const scenario = getScenario(scenarioId);
  return {
    scenarioId,
    title: scenario.title,
    conclusion: scenario.description,
    nextStep: "",
    primaryLabel: "下一步",
    primaryOperation: "",
    primaryDisabled: Boolean(state.activeOperation),
    disabledReason: "",
    safety: scenario.safeNote,
    programSummary: "",
    steps: [],
    warnings: [],
    debugHints: []
  };
}

function syncSteps(active: "preview" | "active-preview" | "blocked" | "apply" | "import"): ScenarioStep[] {
  return [
    { title: "预览同步", status: active === "preview" || active === "active-preview" ? "active" : "done" },
    { title: "写入 cache", status: active === "blocked" ? "blocked" : active === "apply" ? "active" : active === "import" ? "done" : "pending" },
    { title: "导入 Unity", status: active === "import" ? "active" : "pending" },
    { title: "PR 检查", status: "pending" }
  ];
}

function prSteps(active: "compare" | "review" | "gate"): ScenarioStep[] {
  return [
    { title: "合并预览", status: active === "compare" ? "active" : "done" },
    { title: "提交审查", status: active === "review" ? "active" : active === "gate" ? "done" : "pending" },
    { title: "PR 检查", status: active === "gate" ? "active" : "pending" }
  ];
}

function extractPrGate(value: LifecycleResultLike | PrGateReportLike | null | undefined): PrGateReportLike | null {
  if (!value) {
    return null;
  }

  const lifecycle = value as LifecycleResultLike;
  return lifecycle.prGateReport ?? (value as PrGateReportLike);
}
