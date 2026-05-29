import { useCallback, useEffect, useMemo, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import {
  decideRecommendedScenario,
  decideWorkflow,
  desktopResultNameForOperation,
  getScenario,
  humanToolStatus,
  normalizeSyncCacheResult,
  ordinaryToolText,
  parseLifecycleResultJson,
  primaryToolAction,
  scenarios,
  secondaryToolActions,
  shouldShowBotSecretForm,
  shouldReadDesktopResultAfterTask,
  summarizeLifecycleResult,
  syncPreviewFingerprint,
  syncWritableTableCount,
  validateNewTableDraft,
  type FieldType,
  type LifecycleResultLike,
  type NewTableDraft,
  type ScenarioId,
  type ToolCheckLike,
  type ViewMode
} from "./workflow";

type ProjectSnapshot = {
  projectRoot: string;
  unityProject: boolean;
  projectConfigPath: string;
  unityPackageVersion: string;
  projectId: string;
  gitBranch: string;
  feishuProfile: string;
  registryBaseToken: string;
  registryBaseUrl: string;
  githubRepository: string;
  gitBranchUrl: string;
  prUrl: string;
  prNumber: string;
};

type ToolCheck = ToolCheckLike & {
  detail: string;
  action?: string;
  actionLabel?: string;
  url?: string;
};

type CliRunResult = {
  commandLine: string;
  exitCode: number;
  stdout: string;
  stderr: string;
  executablePath?: string;
  source?: string;
  attemptedPaths?: string[];
  resultPath?: string;
  resultJson?: string;
};

type TaskSnapshot = CliRunResult & {
  taskId: string;
  operation: string;
  status: "running" | "canceling" | "succeeded" | "failed" | "canceled";
  phase: string;
  message: string;
  elapsedMs: number;
  progressPath?: string;
  progressLog?: string;
  pid?: number;
  tableId?: string;
  current?: number;
  total?: number;
};

type StartupContext = {
  projectRoot: string;
  bridgeSessionDir: string;
  desktopVersion: string;
  sidecarCliVersion: string;
};

type IdentityMode = "strict-bot" | "user-fallback";

const identityPrefKey = "ConfigSheetForge.Desktop.IdentityMode";
const scenarioPrefKey = "ConfigSheetForge.Desktop.LastScenario";
const viewPrefKey = "ConfigSheetForge.Desktop.ViewMode";
const debugPrefKey = "ConfigSheetForge.Desktop.Debug";

const operationLabels: Record<string, string> = {
  doctor: "环境检查",
  "registry-status": "读取在线注册中心",
  "sync-status": "快速状态检查",
  "sync-cache-dry-run": "完整同步预览",
  "sync-cache-apply": "写入本地 cache",
  "repair-cache-dialect": "修复 cache 类型行",
  "compare-merge": "生成合并预览",
  "submit-merge-review": "提交合并审查记录",
  "pr-gate-report": "运行 PR 检查",
  "bootstrap-current-branch-from-target-dry-run": "预览派生当前分支",
  "new-table-dry-run": "预览新建配表",
  "unity-import": "导入 Unity 配表资产"
};

const fieldTypes: Array<{ value: FieldType; label: string }> = [
  { value: "string", label: "文本" },
  { value: "int", label: "整数" },
  { value: "float", label: "小数" },
  { value: "bool", label: "是/否" },
  { value: "string[]", label: "文本列表" },
  { value: "int[]", label: "整数列表" },
  { value: "float[]", label: "小数列表" },
  { value: "bool[]", label: "是/否列表" }
];

function isTauriRuntime() {
  return typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
}

function readStoredScenario(): ScenarioId {
  const value = typeof localStorage === "undefined" ? "" : localStorage.getItem(scenarioPrefKey) || "";
  return scenarios.some((scenario) => scenario.id === value) ? value as ScenarioId : "sync-import";
}

function readStoredViewMode(): ViewMode {
  return typeof localStorage !== "undefined" && localStorage.getItem(viewPrefKey) === "programmer"
    ? "programmer"
    : "planner";
}

function redact(value: string) {
  if (!value) {
    return "未配置";
  }

  if (value.length <= 8) {
    return "已配置";
  }

  return `${value.slice(0, 4)}...${value.slice(-4)}`;
}

function isInteractiveSafeFallbackOperation(operation: string) {
  return operation === "registry-status"
    || operation === "sync-cache-dry-run"
    || operation === "compare-merge";
}

function parseResultJson(text: string | undefined): LifecycleResultLike | null {
  return parseLifecycleResultJson(text);
}

function projectPath(root: string | undefined, ...parts: string[]) {
  const base = stripVerbatimPathPrefix(root || "").replace(/[\\/]$/, "");
  const separator = base.includes("\\") || /^[A-Za-z]:/.test(base) ? "\\" : "/";
  return [base, ...parts].filter(Boolean).join(separator);
}

function stripVerbatimPathPrefix(path: string) {
  if (path.startsWith("\\\\?\\UNC\\")) {
    return `\\\\${path.slice("\\\\?\\UNC\\".length)}`;
  }

  if (path.startsWith("\\\\?\\")) {
    return path.slice("\\\\?\\".length);
  }

  return path;
}

function desktopResultPath(snapshot: ProjectSnapshot | null, projectRoot: string, name: string) {
  return projectPath(snapshot?.projectRoot || projectRoot, "Temp", "ConfigSheetForge", "desktop", `${name}.result.json`);
}

function desktopProgressPath(snapshot: ProjectSnapshot | null, projectRoot: string, name: string) {
  return projectPath(snapshot?.projectRoot || projectRoot, "Temp", "ConfigSheetForge", "desktop", `${name}.progress.ndjson`);
}

function buildCommandArgs(
  operation: string,
  snapshot: ProjectSnapshot | null,
  projectRoot: string,
  identityMode: IdentityMode,
  previewResultPath: string,
  newTable?: NewTableDraft
): string[] {
  const manifestArgs = snapshot?.projectConfigPath ? ["--manifest", snapshot.projectConfigPath] : [];
  const fallbackArgs = identityMode === "user-fallback" && isInteractiveSafeFallbackOperation(operation)
    ? ["--interactive-desktop", "--allow-user-fallback"]
    : [];
  const out = (name: string) => ["--out", desktopResultPath(snapshot, projectRoot, name), "--progress", desktopProgressPath(snapshot, projectRoot, name)];

  switch (operation) {
    case "registry-status":
      return ["registry-status", ...manifestArgs, "--details", ...out("registry-status"), ...fallbackArgs];
    case "sync-status":
      return ["sync-status", ...manifestArgs, "--details", ...out("sync-status"), ...fallbackArgs];
    case "sync-cache-dry-run":
      return ["sync-cache", ...manifestArgs, "--dry-run", "--details", ...out("sync-cache"), ...fallbackArgs];
    case "sync-cache-apply":
      return ["sync-cache", ...manifestArgs, "--yes", "--details", "--preview-result", previewResultPath, ...out("sync-cache-apply")];
    case "repair-cache-dialect":
      return ["repair-cache-dialect", ...manifestArgs, "--dry-run", "--details", ...out("repair-cache-dialect")];
    case "compare-merge":
      return ["apply-contract", "--operation", "compare-merge", "--dry-run", "--details", ...out("compare-merge"), ...fallbackArgs];
    case "submit-merge-review":
      return ["submit-merge-review", "--preview-result", previewResultPath, "--details", ...out("submit-merge-review")];
    case "pr-gate-report":
      return ["apply-contract", "--operation", "pr-gate-report", "--details", ...out("pr-gate-report")];
    case "bootstrap-current-branch-from-target-dry-run":
      return ["bootstrap-current-branch-from-target", ...manifestArgs, "--dry-run", "--details", ...out("bootstrap-current-branch-from-target"), ...fallbackArgs];
    case "new-table-dry-run":
      return [
        "new-table",
        ...manifestArgs,
        "--dry-run",
        "--details",
        "--id",
        newTable?.tableId || "",
        "--name",
        newTable?.displayName || "",
        "--owner-role",
        newTable?.ownerRole || "tableOwner",
        "--fields-json",
        JSON.stringify(newTable?.fields || []),
        ...out("new-table")
      ];
    default:
      return ["doctor", "--details", ...out("doctor")];
  }
}

function operationStage(operation: string) {
  if (operation === "sync-status" || operation === "registry-status") {
    return "快速读取 registry / 检查本地 cache，不导出 xlsx";
  }

  if (operation.includes("sync-cache")) {
    return "读取注册中心 / 读取在线表 / 导出 xlsx / 三方一致性检查";
  }

  if (operation.includes("merge")) {
    return "解析 PR 上下文 / 准备语义输入 / 生成报告";
  }

  if (operation.includes("bootstrap")) {
    return "定位目标分支 / 规划在线表派生 / 生成写入清单";
  }

  return "准备环境 / 启动本机工具";
}

function isTerminalTask(status: TaskSnapshot["status"]) {
  return status === "succeeded" || status === "failed" || status === "canceled";
}

function taskToCliResult(task: TaskSnapshot): CliRunResult {
  return {
    commandLine: task.commandLine,
    exitCode: task.exitCode,
    stdout: task.stdout,
    stderr: task.stderr,
    executablePath: task.executablePath,
    source: task.source,
    attemptedPaths: task.attemptedPaths,
    resultPath: task.resultPath,
    resultJson: task.resultJson
  };
}

function delay(ms: number) {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}

function taskHumanTitle(operation: string) {
  if (operation === "sync-cache-dry-run") {
    return "正在预览同步，不会写入文件";
  }

  if (operation === "sync-status" || operation === "registry-status") {
    return "正在快速检查状态，不导出 xlsx";
  }

  return `正在运行：${operationLabels[operation] || operation}`;
}

function taskHumanProgress(task: TaskSnapshot | null, operation: string) {
  if (task?.tableId && task.current && task.total) {
    return `当前：第 ${task.current}/${task.total} 张 ${task.tableId}，${task.phase || operationStage(operation)}`;
  }

  if (task?.message) {
    return task.message;
  }

  return `当前：${task?.phase || operationStage(operation)}`;
}

function taskEtaText(task: TaskSnapshot | null) {
  if (!task?.current || !task.total || !task.elapsedMs || task.current <= 0 || task.total <= task.current) {
    return "";
  }

  const average = task.elapsedMs / task.current;
  const remainingMs = Math.max(0, Math.round(average * (task.total - task.current)));
  return `预计还需约 ${Math.max(1, Math.round(remainingMs / 1000))} 秒`;
}

function taskSafetyText(operation: string, fallback: string) {
  if (operation === "sync-cache-dry-run") {
    return "完整同步预览会读取在线表并临时导出 xlsx，可能需要几分钟；不会写本地 cache、飞书或 ProjectSettings。";
  }

  if (operation === "sync-status" || operation === "registry-status") {
    return "快速状态检查只读取 registry 和本地 cache/hash/mtime，不导出 16 张 xlsx。";
  }

  if (operation.includes("dry-run") || operation.includes("preview")) {
    return "只读预览，不写文件。";
  }

  return fallback;
}

function cancelHintText(operation: string) {
  if (operation === "sync-cache-dry-run" || operation.includes("dry-run") || operation.includes("preview") || operation === "sync-status" || operation === "registry-status") {
    return "取消只终止本次预览，不会写 cache，也不会改飞书。";
  }

  return "可以取消后台任务；已经完成的本地写入不会自动回滚。";
}

function cancelButtonLabel(operation: string) {
  if (operation === "sync-cache-dry-run" || operation.includes("dry-run") || operation.includes("preview") || operation === "sync-status" || operation === "registry-status") {
    return "取消本次预览（不写 cache/飞书）";
  }

  return "取消后台任务";
}

function ordinaryErrorText(value: unknown) {
  const text = value instanceof Error ? value.message : String(value ?? "");
  if (text.includes("System.IO.IOException") || text.includes("at ConfigSheetForge") || text.includes("SafeFileHandle")) {
    return "生成结果文件失败：路径格式不合法。请升级 Desktop/CLI 或重试。Debug 里可以查看完整堆栈。";
  }

  return text;
}

export function App() {
  const [projectRoot, setProjectRoot] = useState("");
  const [startup, setStartup] = useState<StartupContext>({ projectRoot: "", bridgeSessionDir: "", desktopVersion: "", sidecarCliVersion: "" });
  const [snapshot, setSnapshot] = useState<ProjectSnapshot | null>(null);
  const [checks, setChecks] = useState<ToolCheck[]>([]);
  const [activeOperation, setActiveOperation] = useState("");
  const [activeTask, setActiveTask] = useState<TaskSnapshot | null>(null);
  const [operationStartedAt, setOperationStartedAt] = useState<number | null>(null);
  const [lastResult, setLastResult] = useState<CliRunResult | null>(null);
  const [lastResultParsed, setLastResultParsed] = useState<LifecycleResultLike | null>(null);
  const [lastSyncPreview, setLastSyncPreview] = useState<LifecycleResultLike | null>(null);
  const [lastQuickStatus, setLastQuickStatus] = useState<LifecycleResultLike | null>(null);
  const [lastSyncPreviewPath, setLastSyncPreviewPath] = useState("");
  const [lastComparePreview, setLastComparePreview] = useState<LifecycleResultLike | null>(null);
  const [lastComparePreviewPath, setLastComparePreviewPath] = useState("");
  const [lastGateReport, setLastGateReport] = useState<LifecycleResultLike | null>(null);
  const [logExpanded, setLogExpanded] = useState(false);
  const [moreExpanded, setMoreExpanded] = useState(false);
  const [error, setError] = useState("");
  const [selectedScenario, setSelectedScenario] = useState<ScenarioId>(readStoredScenario);
  const [viewMode, setViewMode] = useState<ViewMode>(readStoredViewMode);
  const [debugEnabled, setDebugEnabled] = useState(() => typeof localStorage !== "undefined" && localStorage.getItem(debugPrefKey) === "1");
  const [identityMode, setIdentityMode] = useState<IdentityMode>(() => {
    if (typeof localStorage === "undefined") {
      return "strict-bot";
    }

    return localStorage.getItem(identityPrefKey) === "user-fallback" ? "user-fallback" : "strict-bot";
  });
  const [botAppId, setBotAppId] = useState("");
  const [botSecret, setBotSecret] = useState("");
  const [showBotConfigure, setShowBotConfigure] = useState(false);
  const [newTable, setNewTable] = useState<NewTableDraft>({
    tableId: "",
    displayName: "",
    ownerRole: "tableOwner",
    fields: [
      { key: "id", displayName: "ID", type: "string", description: "唯一ID", primary: true },
      { key: "name", displayName: "名称", type: "string", description: "显示名称" }
    ]
  });

  const runtimeAvailable = isTauriRuntime();
  const elapsed = useMemo(() => {
    if (activeTask?.elapsedMs) {
      return `${Math.max(0, Math.round(activeTask.elapsedMs / 1000))} 秒`;
    }

    if (!operationStartedAt) {
      return "";
    }

    return `${Math.max(0, Math.round((Date.now() - operationStartedAt) / 1000))} 秒`;
  }, [activeTask?.elapsedMs, operationStartedAt, activeOperation]);
  const larkCheck = checks.find((check) => check.name === "lark-cli");
  const cliCheck = checks.find((check) => check.name === "Config Sheet Forge CLI");
  const recommendedScenario = useMemo(
    () => decideRecommendedScenario({
      snapshot,
      checks,
      lastScenario: selectedScenario,
      lastSyncPreview,
      lastComparePreview,
      lastGateReport,
      bridgeSessionDir: startup.bridgeSessionDir,
      activeOperation
    }),
    [activeOperation, checks, lastComparePreview, lastGateReport, lastSyncPreview, selectedScenario, snapshot, startup.bridgeSessionDir]
  );
  const decision = useMemo(
    () => decideWorkflow(selectedScenario, {
      snapshot,
      checks,
      lastScenario: selectedScenario,
      lastSyncPreview,
      lastComparePreview,
      lastGateReport,
      bridgeSessionDir: startup.bridgeSessionDir,
      activeOperation
    }),
    [activeOperation, checks, lastComparePreview, lastGateReport, lastSyncPreview, selectedScenario, snapshot, startup.bridgeSessionDir]
  );
  const newTableErrors = validateNewTableDraft(newTable);

  const persistIdentityMode = useCallback((mode: IdentityMode) => {
    setIdentityMode(mode);
    localStorage.setItem(identityPrefKey, mode);
  }, []);

  const updateScenario = useCallback((scenario: ScenarioId) => {
    setSelectedScenario(scenario);
    localStorage.setItem(scenarioPrefKey, scenario);
  }, []);

  const updateViewMode = useCallback((mode: ViewMode) => {
    setViewMode(mode);
    localStorage.setItem(viewPrefKey, mode);
  }, []);

  const updateDebug = useCallback((enabled: boolean) => {
    setDebugEnabled(enabled);
    localStorage.setItem(debugPrefKey, enabled ? "1" : "0");
  }, []);

  const openExternal = useCallback(async (url?: string) => {
    if (!url) {
      return;
    }

    if (!runtimeAvailable) {
      window.open(url, "_blank", "noopener,noreferrer");
      return;
    }

    await invoke("open_external_url", { url });
  }, [runtimeAvailable]);

  const waitForTask = useCallback(async (initial: TaskSnapshot): Promise<TaskSnapshot> => {
    let current = initial;
    setActiveTask(current);
    setActiveOperation(current.operation);
    setOperationStartedAt(Date.now());
    while (!isTerminalTask(current.status)) {
      await delay(500);
      current = await invoke<TaskSnapshot>("get_task", { taskId: current.taskId });
      setActiveTask(current);
    }
    setActiveTask(null);
    setActiveOperation("");
    setOperationStartedAt(null);
    return current;
  }, []);

  const startToolCheckTask = useCallback(async (root: string) => {
    if (!runtimeAvailable) {
      return;
    }

    const initial = await invoke<TaskSnapshot>("start_tool_check_task", { projectRoot: root });
    const finalTask = await waitForTask(initial);
    if (finalTask.status === "succeeded" && finalTask.resultJson) {
      try {
        setChecks(JSON.parse(finalTask.resultJson.replace(/^\uFEFF/, "")) as ToolCheck[]);
      } catch {
        setError("工具检查完成，但结果解析失败。请打开 Debug 查看诊断。");
      }
    } else if (finalTask.status === "canceled") {
      setLastResult(taskToCliResult(finalTask));
    } else {
      setError(ordinaryErrorText(finalTask.message || "工具检查失败。"));
      setLastResult(taskToCliResult(finalTask));
    }
  }, [runtimeAvailable, waitForTask]);

  const cancelActiveTask = useCallback(async () => {
    if (!activeTask?.taskId || !runtimeAvailable) {
      return;
    }

    try {
      const canceled = await invoke<TaskSnapshot>("cancel_task", { taskId: activeTask.taskId });
      setActiveTask(canceled);
    } catch (ex) {
      setError(ordinaryErrorText(ex));
    }
  }, [activeTask?.taskId, runtimeAvailable]);

  const discover = useCallback(async (overrideRoot?: string) => {
    const targetRoot = typeof overrideRoot === "string" ? overrideRoot : projectRoot;
    setError("");
    setActiveOperation("识别项目");
    setOperationStartedAt(Date.now());
    try {
      if (!runtimeAvailable) {
        setSnapshot({
          projectRoot: targetRoot || "K:/Unity/Project",
          unityProject: true,
          projectConfigPath: "ProjectSettings/Project.ConfigSheetForge.json",
          unityPackageVersion: "web-preview",
          projectId: "demo",
          gitBranch: "当前分支",
          feishuProfile: "当前分支",
          registryBaseToken: "",
          registryBaseUrl: "",
          githubRepository: "",
          gitBranchUrl: "",
          prUrl: "",
          prNumber: ""
        });
        return;
      }

      const result = await invoke<ProjectSnapshot>("discover_project", { projectRoot: targetRoot });
      setSnapshot(result);
      void startToolCheckTask(result.projectRoot);
    } catch (ex) {
      setError(ordinaryErrorText(ex));
    } finally {
      setActiveOperation("");
      setOperationStartedAt(null);
    }
  }, [projectRoot, runtimeAvailable, startToolCheckTask]);

  useEffect(() => {
    if (!runtimeAvailable || projectRoot) {
      return;
    }

    invoke<StartupContext>("startup_context")
      .then((context) => {
        setStartup(context);
        if (context.projectRoot) {
          setProjectRoot(context.projectRoot);
          void discover(context.projectRoot);
        }
      })
      .catch(() => {
        // 启动参数只是便利用途，读取失败时仍允许手动粘贴项目目录。
      });
  }, [projectRoot, runtimeAvailable, discover]);

  const runSetupAction = useCallback(async (action: string) => {
    setError("");
    if (activeTask && !isTerminalTask(activeTask.status)) {
      setError("后台任务正在运行，完成或取消后再启动新的安装/授权动作。");
      return;
    }

    setLastResult(null);
    try {
      if (!runtimeAvailable) {
        setLastResult({
          commandLine: `setup ${action}`,
          exitCode: 0,
          stdout: "Desktop web preview：Tauri 运行时未连接。",
          stderr: ""
        });
        return;
      }

      const initial = await invoke<TaskSnapshot>("start_setup_task", {
        projectRoot: snapshot?.projectRoot || projectRoot,
        action,
        appId: botAppId,
        secretValue: botSecret
      });
      const task = await waitForTask(initial);
      const result = taskToCliResult(task);
      setLastResult(result);
      if (action === "configure_lark_bot") {
        setBotSecret("");
      }
      void startToolCheckTask(snapshot?.projectRoot || projectRoot);
    } catch (ex) {
      setError(ordinaryErrorText(ex));
    }
  }, [activeTask, botAppId, botSecret, projectRoot, runtimeAvailable, snapshot?.projectRoot, startToolCheckTask, waitForTask]);

  const readResultAfterTaskCompletion = useCallback(async (operation: string, result: CliRunResult): Promise<CliRunResult> => {
    if (!runtimeAvailable || !shouldReadDesktopResultAfterTask(operation, result.exitCode, result.resultJson)) {
      return result;
    }

    const name = desktopResultNameForOperation(operation);
    try {
      const cached = await invoke<CliRunResult>("read_desktop_result", {
        projectRoot: snapshot?.projectRoot || projectRoot,
        name
      });
      if (cached.resultJson?.trim()) {
        return {
          ...result,
          resultPath: cached.resultPath || result.resultPath,
          resultJson: cached.resultJson
        };
      }

      setError(`任务已完成，但没有读取到 result JSON：${cached.resultPath || result.resultPath || name}。请打开 Debug 查看日志，或重新运行预览。`);
    } catch (ex) {
      setError(`任务已完成，但读取 result 文件失败：${ordinaryErrorText(ex)}。请打开 Debug 查看日志，或重新运行预览。`);
    }

    return result;
  }, [projectRoot, runtimeAvailable, snapshot?.projectRoot]);

  const applyResult = useCallback((operation: string, result: CliRunResult) => {
    setLastResult(result);
    const parsed = parseResultJson(result.resultJson);
    setLastResultParsed(parsed);
    if (!parsed) {
      if (result.exitCode === 0 && desktopResultNameForOperation(operation)) {
        setError(`任务已完成，但结果文件没有成功解析：${result.resultPath || desktopResultNameForOperation(operation)}。请打开 Debug 查看日志，或重新运行预览。`);
      }
      return;
    }

    const syncResult = normalizeSyncCacheResult(parsed);
    if (operation === "sync-status" || operation === "registry-status") {
      setLastQuickStatus(parsed);
    } else if (operation === "sync-cache-dry-run" || (parsed.operation === "sync-cache" && syncResult?.dryRun)) {
      if (!syncResult) {
        setError("同步预览完成，但 result 不是有效的 sync-cache 结果。请打开 Debug 查看 result JSON。");
        return;
      }

      setLastSyncPreview(parsed);
      setLastSyncPreviewPath(result.resultPath || desktopResultPath(snapshot, projectRoot, "sync-cache"));
    } else if (operation === "sync-cache-apply" || (parsed.operation === "sync-cache" && !syncResult?.dryRun)) {
      setLastSyncPreview(parsed);
      setLastSyncPreviewPath(result.resultPath || desktopResultPath(snapshot, projectRoot, "sync-cache-apply"));
    } else if (operation === "compare-merge") {
      setLastComparePreview(parsed);
      setLastComparePreviewPath(result.resultPath || desktopResultPath(snapshot, projectRoot, "compare-merge"));
    } else if (operation === "pr-gate-report") {
      setLastGateReport(parsed);
    }
  }, [projectRoot, snapshot]);

  useEffect(() => {
    if (!runtimeAvailable || !snapshot?.projectRoot) {
      return;
    }

    let canceled = false;
    const restore = async () => {
      const entries: Array<[string, string]> = [
        ["sync-cache", "sync-cache-dry-run"],
        ["sync-cache-apply", "sync-cache-apply"],
        ["compare-merge", "compare-merge"],
        ["pr-gate-report", "pr-gate-report"]
      ];

      for (const [name, operation] of entries) {
        try {
          const result = await invoke<CliRunResult>("read_desktop_result", {
            projectRoot: snapshot.projectRoot,
            name
          });
          if (!canceled && result.resultJson?.trim()) {
            applyResult(operation, result);
          }
        } catch {
          // 旧项目没有 result 文件是正常情况，静默跳过即可。
        }
      }
    };

    void restore();
    return () => {
      canceled = true;
    };
  }, [applyResult, runtimeAvailable, snapshot?.projectRoot]);

  const sendUnityBridgeCommand = useCallback(async (operation: string) => {
    if (!startup.bridgeSessionDir) {
      setError("当前 Desktop 不是从 Unity bridge 启动。请回 Unity 点击“导入 Unity 配表资产”。");
      return;
    }

    if (!runtimeAvailable) {
      setLastResult({
        commandLine: `unity-bridge ${operation}`,
        exitCode: 0,
        stdout: "Desktop web preview：Tauri 运行时未连接。",
        stderr: ""
      });
      return;
    }

    const path = await invoke<string>("write_bridge_command", {
      bridgeSessionDir: startup.bridgeSessionDir,
      operation,
      payloadJson: JSON.stringify({
        projectRoot: snapshot?.projectRoot || projectRoot,
        requestedAt: new Date().toISOString()
      })
    });
    setLastResult({
      commandLine: `unity-bridge ${operation}`,
      exitCode: 0,
      stdout: `已发送给 Unity bridge：${path}`,
      stderr: "",
      resultPath: path
    });
  }, [projectRoot, runtimeAvailable, snapshot?.projectRoot, startup.bridgeSessionDir]);

  const handleToolAction = useCallback(async (action: string) => {
    if (!action || action === "none") {
      return;
    }

    if (action === "refresh_pr" || action === "refresh_tools") {
      await discover(snapshot?.projectRoot || projectRoot);
      return;
    }

    if (action === "configure_lark_bot") {
      setShowBotConfigure(true);
      updateScenario("environment");
      return;
    }

    await runSetupAction(action);
  }, [discover, projectRoot, runSetupAction, snapshot?.projectRoot, updateScenario]);

  const runOperation = useCallback(async (operation: string) => {
    setError("");
    if (!operation) {
      return;
    }

    if (activeTask && !isTerminalTask(activeTask.status)) {
      setError("后台任务正在运行，可以继续查看页面或点击取消；完成后再启动新的任务。");
      return;
    }

    if (operation === "unity-import") {
      await sendUnityBridgeCommand("import-assets");
      return;
    }

    if (operation === "sync-cache-apply") {
      const normalizedPreview = normalizeSyncCacheResult(lastSyncPreview);
      if (!lastSyncPreviewPath || !normalizedPreview?.success) {
        setError("写入本地 cache 前必须先有最近一次同输入 sync-cache dry-run 成功结果。请重新预览同步。");
        return;
      }

      if (normalizedPreview.cacheStatus === "upToDate") {
        setError("本地 cache 已最新，不需要写入。下一步请导入 Unity 配表资产。");
        return;
      }

      if (!normalizedPreview.canApplyCache) {
        setError("最近一次同步预览没有允许写 cache。请重新生成完整同步预览。");
        return;
      }

      if (!syncPreviewFingerprint(lastSyncPreview)) {
        setError("最近一次同步预览缺少 fingerprint，不能写入本地 cache。请重新预览同步。");
        return;
      }

      const confirmed = window.confirm("写入本地 cache 会更新 .config-sheet-forge/excel-cache 和 .config-sheet-forge/cache。\n\n不会写旧 Excel/，不会写飞书，不改 ProjectSettings。\n\nDesktop 会把最近一次 dry-run result 交给 CLI 校验，同输入通过后才写入。");
      if (!confirmed) {
        return;
      }
    }

    if (operation === "submit-merge-review" && (!lastComparePreviewPath || !lastComparePreview?.success)) {
      setError("提交合并审查前必须先生成合并预览。");
      return;
    }

    if (identityMode === "user-fallback" && operation === "pr-gate-report") {
      const confirmed = window.confirm("PR hard gate 默认 strict bot。Desktop 不会用用户身份伪装 CI 通过。是否继续按 strict bot 运行 PR 检查？");
      if (!confirmed) {
        return;
      }
    }

    setLastResult(null);
    try {
      const previewPath = operation === "submit-merge-review" ? lastComparePreviewPath : lastSyncPreviewPath;
      const args = buildCommandArgs(operation, snapshot, projectRoot, identityMode, previewPath, newTable);
      if (!runtimeAvailable) {
        const mock = {
          commandLine: `config-sheet-forge ${args.join(" ")}`,
          exitCode: 0,
          stdout: "Desktop web preview：Tauri 运行时未连接。打包为 Desktop 后会调用内置 sidecar CLI。",
          stderr: "",
          resultPath: args[args.indexOf("--out") + 1] || ""
        };
        applyResult(operation, mock);
        return;
      }

      const initial = await invoke<TaskSnapshot>("start_task", {
        projectRoot: snapshot?.projectRoot || projectRoot,
        operation,
        args
      });
      const task = await waitForTask(initial);
      let result = taskToCliResult(task);
      if (task.status === "canceled") {
        setLastResult(result);
        setLastResultParsed(null);
        return;
      }

      result = await readResultAfterTaskCompletion(operation, result);
      applyResult(operation, result);
    } catch (ex) {
      setError(ordinaryErrorText(ex));
    }
  }, [
    applyResult,
    activeTask,
    identityMode,
    lastComparePreview,
    lastComparePreviewPath,
    lastSyncPreview,
    lastSyncPreviewPath,
    newTable,
    projectRoot,
    readResultAfterTaskCompletion,
    runtimeAvailable,
    sendUnityBridgeCommand,
    snapshot,
    waitForTask
  ]);

  const primaryDisabled = decision.primaryDisabled
    || (selectedScenario === "new-table" && decision.primaryOperation === "new-table-dry-run" && newTableErrors.length > 0)
    || Boolean(activeOperation);
  const statusSource = lastSyncPreview || lastQuickStatus;
  const statusSourceSync = normalizeSyncCacheResult(statusSource);
  const desktopVersion = startup.desktopVersion || "开发预览";
  const unityVersion = snapshot?.unityPackageVersion || "未识别";
  const cliVersion = startup.sidecarCliVersion || (cliCheck?.source?.includes("sidecar") ? desktopVersion : cliCheck?.source || "未识别");
  const visibleResult = activeTask ? taskToCliResult(activeTask) : lastResult;
  const visibleParsed = activeTask ? parseResultJson(activeTask.resultJson) : lastResultParsed;
  const resultNext = resultNextAction(visibleParsed, startup.bridgeSessionDir);

  return (
    <main className="app-shell">
      <header className="top-bar">
        <div className="brand-block">
          <p className="eyebrow">Config Sheet Forge Desktop</p>
          <h1>配表 Source of Truth 工作台</h1>
          <p className="version-strip">Desktop v{desktopVersion} · UPM {unityVersion} · CLI {cliVersion}</p>
          <p>{snapshot?.projectId || "未识别项目"} · {snapshot?.gitBranch || "等待分支"} · {getScenario(selectedScenario).shortTitle}</p>
        </div>
        <div className="top-actions">
          <div className="view-switch" role="tablist" aria-label="视图模式">
            <button className={viewMode === "planner" ? "selected" : ""} onClick={() => updateViewMode("planner")}>策划视图</button>
            <button className={viewMode === "programmer" ? "selected" : ""} onClick={() => updateViewMode("programmer")}>程序视图</button>
          </div>
          <label className="debug-toggle">
            <input type="checkbox" checked={debugEnabled} onChange={(event) => updateDebug(event.target.checked)} />
            <span>Debug</span>
          </label>
        </div>
      </header>

      <section className="project-picker compact-card">
        <label>
          Unity 项目目录
          <input
            value={projectRoot}
            onChange={(event) => setProjectRoot(event.target.value)}
            placeholder="选择或粘贴 Unity project root，例如 K:/Unity/MyGame"
          />
        </label>
        <button className="secondary" onClick={() => void discover()}>识别项目</button>
      </section>

      {error ? <section className="error-card">{error}</section> : null}

      {activeOperation ? (
        <section className="running-card">
          <div>
            <strong>{taskHumanTitle(activeOperation)}</strong>
            <p>{taskHumanProgress(activeTask, activeOperation)}</p>
            {taskEtaText(activeTask) ? <p>{taskEtaText(activeTask)}</p> : null}
            <p>安全性：{taskSafetyText(activeOperation, decision.safety)}</p>
            {debugEnabled && activeTask?.taskId ? <p className="debug-meta">taskId：{activeTask.taskId}{activeTask.pid ? ` · PID ${activeTask.pid}` : ""}</p> : null}
            {(activeTask?.elapsedMs || 0) > 10000 ? <p>仍在运行，可以取消；{cancelHintText(activeOperation)}</p> : null}
          </div>
          <div className="running-actions">
            <span>已用时 {elapsed || "0 秒"}</span>
            {activeTask?.taskId ? <button className="danger-button" onClick={() => void cancelActiveTask()}>{cancelButtonLabel(activeOperation)}</button> : null}
          </div>
        </section>
      ) : null}

      <section className="scenario-home">
        <div className="section-title">
          <div>
            <p className="eyebrow">智能场景</p>
            <h2>你现在要做什么？</h2>
          </div>
          <p>系统推荐：{getScenario(recommendedScenario).title}</p>
        </div>
        <div className="scenario-grid">
          {scenarios.map((scenario) => (
            <button
              key={scenario.id}
              className={`scenario-card ${selectedScenario === scenario.id ? "selected" : ""} ${recommendedScenario === scenario.id ? "recommended" : ""}`}
              onClick={() => updateScenario(scenario.id)}
            >
              <span>{recommendedScenario === scenario.id ? "推荐" : scenario.shortTitle}</span>
              <strong>{scenario.title}</strong>
              <small>{scenario.description}</small>
            </button>
          ))}
        </div>
      </section>

      <section className="status-strip">
        <StatusCard title="在线表" value={statusSource?.branchStatus ? `${statusSource.branchStatus.tableCountRegistered || 0}/${statusSource.branchStatus.tableCountExpected || 0} 已登记` : "等待读取"} detail="以 live registry 为准。" />
        <StatusCard title="本地 cache" value={statusSourceSync?.cacheStatus || "等待预览"} detail={summarizeLifecycleResult(statusSource)} />
        <StatusCard title="PR gate" value={lastGateReport?.prGateReport?.gateState || "等待检查"} detail={summarizeLifecycleResult(lastGateReport)} url={snapshot?.prUrl} onOpen={openExternal} />
        <StatusCard title="飞书注册中心" value={snapshot?.registryBaseToken ? "已配置" : "等待识别"} detail={`Base: ${redact(snapshot?.registryBaseToken || "")}`} url={snapshot?.registryBaseUrl} onOpen={openExternal} />
      </section>

      <section className="wizard-layout">
        <aside className="stepper-panel">
          <h2>{decision.title}</h2>
          <ol className="stepper">
            {decision.steps.map((step) => (
              <li className={step.status} key={step.title}>
                <span />
                <p>{step.title}</p>
              </li>
            ))}
          </ol>
        </aside>

        <section className="primary-panel">
          <p className="eyebrow">当前结论</p>
          <h2>{decision.conclusion}</h2>
          <p className="next-step">{decision.nextStep}</p>
          {decision.warnings.length > 0 ? (
            <div className="warning-list">
              {decision.warnings.map((warning) => <p key={warning}>{warning}</p>)}
            </div>
          ) : null}
          {selectedScenario === "new-table" ? (
            <NewTableMiniForm draft={newTable} onChange={setNewTable} errors={newTableErrors} />
          ) : null}
          <div className="primary-row">
            <button
              className="primary large"
              disabled={primaryDisabled}
              title={primaryDisabled ? decision.disabledReason || "后台任务运行中，完成后可继续。" : ""}
              onClick={() => void runOperation(decision.primaryOperation)}
            >
              {activeOperation ? "后台任务运行中..." : decision.primaryLabel}
            </button>
            {primaryDisabled && (decision.disabledReason || newTableErrors[0]) ? <small>{decision.disabledReason || newTableErrors[0]}</small> : null}
          </div>
        </section>

        <aside className="safety-panel">
          <h3>为什么安全</h3>
          <p>{decision.safety}</p>
          {viewMode === "programmer" ? (
            <>
              <h3>程序视图</h3>
              <p>{decision.programSummary}</p>
              <p>身份策略：{identityMode === "strict-bot" ? "strict bot；PR gate 不允许 user fallback。" : "交互预览可确认后 user fallback；PR gate 仍 strict bot。"}</p>
            </>
          ) : null}
          <details className="more-actions" open={moreExpanded} onToggle={(event) => setMoreExpanded(event.currentTarget.open)}>
            <summary>更多操作</summary>
            <div className="secondary-actions">
              <button onClick={() => void runOperation("sync-status")}>快速状态检查（不导出 xlsx）</button>
              <button onClick={() => void runOperation("repair-cache-dialect")}>预览修复 cache 类型行</button>
              <button onClick={() => void runOperation("pr-gate-report")}>运行 PR 检查</button>
              <button onClick={() => void runOperation("doctor")}>环境检查</button>
            </div>
          </details>
        </aside>
      </section>

      <section className="identity-card">
        <div>
          <h2>身份策略</h2>
          <p>交互式 Desktop 可以 bot-first，再经你确认后使用飞书用户身份继续；CI / PR hard gate 默认 strict bot。</p>
        </div>
        <div className="segmented">
          <button className={identityMode === "strict-bot" ? "selected" : ""} onClick={() => persistIdentityMode("strict-bot")}>strict bot</button>
          <button className={identityMode === "user-fallback" ? "selected" : ""} onClick={() => persistIdentityMode("user-fallback")}>允许用户身份预览</button>
        </div>
      </section>

      {selectedScenario === "environment" || viewMode === "programmer" ? (
        <section className="tools-panel">
          <div>
            <h2>一键环境 / 授权</h2>
            <p>缺工具时直接点按钮安装。所有安装和授权只改本机环境，不改 ProjectSettings、Packages、旧 Excel/ 或 cache。</p>
          </div>
          <div className="tool-list">
            {checks.length === 0 ? <p className="muted">点击“识别项目”或“开始检查”后显示。</p> : checks.map((check) => (
              <div className={`tool-check ${check.status}`} key={check.name}>
                <div className="tool-check-main">
                  <strong>{check.name}：{humanToolStatus(check)}</strong>
                  <span>{ordinaryToolText(check.summary)}</span>
                </div>
                {check.accountLabel ? <small>当前账号：{check.accountLabel}</small> : null}
                {check.scopesOk === false ? <small className="tool-warning">权限/scope 还不满足当前项目。</small> : null}
                <small>{ordinaryToolText(check.detail)}</small>
                {debugEnabled && check.executablePath ? <code title={check.executablePath}>{check.executablePath}</code> : null}
                <ToolActions check={check} debugEnabled={debugEnabled} onAction={handleToolAction} onOpen={openExternal} />
              </div>
            ))}
          </div>
        </section>
      ) : null}

      {selectedScenario === "environment" ? (
        <section className="bot-card">
          <div>
            <h2>配置飞书 bot</h2>
            {larkCheck?.botConfigured && !showBotConfigure ? (
              <p>bot 已配置：{larkCheck.botLabel || larkCheck.accountLabel || "已配置"}。App Secret 输入框已收起，需要变更时再重新配置。</p>
            ) : (
              <p>App Secret 只通过 stdin 传给 lark-cli，不写仓库、不进命令行、不显示在日志里。</p>
            )}
          </div>
          {shouldShowBotSecretForm(larkCheck, showBotConfigure) ? (
            <div className="bot-form">
              <input value={botAppId} onChange={(event) => setBotAppId(event.target.value)} placeholder="App ID，例如 cli_xxx" />
              <input value={botSecret} onChange={(event) => setBotSecret(event.target.value)} placeholder="App Secret" type="password" />
              <button onClick={() => void runSetupAction("configure_lark_bot")}>配置飞书 bot</button>
            </div>
          ) : (
            <div className="tool-actions">
              <button className="secondary" onClick={() => setShowBotConfigure(true)}>重新配置 bot</button>
              <button className="secondary" onClick={() => void runSetupAction("lark_doctor")}>重新 doctor</button>
            </div>
          )}
        </section>
      ) : null}

      <section className={`result-dock ${debugEnabled ? "debug-open" : ""}`}>
        <div className="result-header">
          <div>
            <h2>最近结果</h2>
            <p>{activeTask ? taskHumanProgress(activeTask, activeTask.operation) : visibleParsed ? summarizeLifecycleResult(visibleParsed) : visibleResult ? `ExitCode ${visibleResult.exitCode}` : "暂无任务结果。"}</p>
          </div>
          {resultNext ? <button className="primary compact" onClick={() => void runOperation(resultNext.operation)}>{resultNext.label}</button> : null}
          <button className="secondary" onClick={() => setLogExpanded((value) => !value)}>
            {logExpanded ? "收起" : "展开详情"}
          </button>
        </div>
        {logExpanded && visibleParsed ? (
          <div className="result-details">
            <p>{summarizeLifecycleResult(visibleParsed)}</p>
            {normalizeSyncCacheResult(visibleParsed) ? <SyncTableSummary result={visibleParsed} /> : null}
            {visibleResult?.resultPath && viewMode === "programmer" ? <p className="muted">result：{visibleResult.resultPath}</p> : null}
          </div>
        ) : null}
        {debugEnabled && visibleResult ? (
          <div className="debug-drawer" hidden={!logExpanded}>
            <p className="muted">Debug 已开启：这里会显示 CLI 命令、路径、stdout/stderr、progress ndjson 和 result JSON。</p>
            <pre className="command">{visibleResult.commandLine}</pre>
            {activeTask?.taskId ? <p className="muted">task：{activeTask.taskId}{activeTask.pid ? ` · PID ${activeTask.pid}` : ""}</p> : null}
            {visibleResult.resultPath ? <p className="muted">result：{visibleResult.resultPath}</p> : null}
            {activeTask?.progressPath ? <p className="muted">progress：{activeTask.progressPath}</p> : null}
            {visibleResult.executablePath ? <p className="muted">工具路径：{visibleResult.executablePath}</p> : null}
            {visibleResult.attemptedPaths?.length ? <p className="muted">尝试路径：{visibleResult.attemptedPaths.slice(0, 12).join("；")}</p> : null}
            {activeTask?.progressLog ? <pre className="logs json-log">{activeTask.progressLog}</pre> : null}
            <pre className="logs">{`${visibleResult.stdout}\n${visibleResult.stderr}`.trim()}</pre>
            {visibleResult.resultJson ? <pre className="logs json-log">{visibleResult.resultJson}</pre> : null}
          </div>
        ) : null}
      </section>
    </main>
  );
}

function ToolActions(props: {
  check: ToolCheck;
  debugEnabled: boolean;
  onAction: (action: string) => void | Promise<void>;
  onOpen: (url?: string) => void | Promise<void>;
}) {
  const primary = primaryToolAction(props.check);
  const secondary = secondaryToolActions(props.check);
  return (
    <div className="tool-actions">
      {primary ? <button onClick={() => void props.onAction(primary.action)}>{primary.label}</button> : null}
      {secondary.length > 0 || props.check.url ? (
        <details className="tool-more">
          <summary>更多操作</summary>
          <div className="tool-more-actions">
            {secondary.map((action) => (
              <button
                className={action.kind === "danger" ? "danger-button" : "secondary"}
                key={`${props.check.name}-${action.action}-${action.label}`}
                onClick={() => void props.onAction(action.action)}
              >
                {action.label}
              </button>
            ))}
            {props.check.url ? <button className="secondary" onClick={() => void props.onOpen(props.check.url)}>打开说明</button> : null}
          </div>
        </details>
      ) : null}
      {!primary && props.check.status === "ok" ? <span className="ready-pill">能用</span> : null}
    </div>
  );
}

function StatusCard(props: { title: string; value: string; detail: string; url?: string; onOpen?: (url?: string) => void }) {
  return (
    <article className="status-card">
      <span>{props.title}</span>
      <strong title={props.value}>{props.value}</strong>
      <p title={props.detail}>{props.detail}</p>
      {props.url ? <button className="link-button" onClick={() => props.onOpen?.(props.url)}>打开</button> : null}
    </article>
  );
}

function resultNextAction(result: LifecycleResultLike | null, bridgeSessionDir: string): { operation: string; label: string } | null {
  const normalized = normalizeSyncCacheResult(result);
  if (!normalized) {
    return null;
  }

  if (result?.operation === "sync-status" || result?.operation === "registry-status") {
    return { operation: "sync-cache-dry-run", label: "完整同步预览" };
  }

  if (normalized.nextAction === "write-cache" && normalized.canApplyCache) {
    const count = Math.max(1, syncWritableTableCount(normalized));
    return { operation: "sync-cache-apply", label: `写入本地 cache（${count} 张）` };
  }

  if (normalized.nextAction === "import-unity" || normalized.cacheStatus === "upToDate") {
    return bridgeSessionDir
      ? { operation: "unity-import", label: "导入 Unity asset" }
      : null;
  }

  return null;
}

function SyncTableSummary(props: { result: LifecycleResultLike }) {
  const summary = normalizeSyncCacheResult(props.result);
  if (!summary) {
    return null;
  }

  const tables = summary.tables || [];
  const changed = syncWritableTableCount(summary);
  return (
    <div className="sync-table-summary">
      <p>{summary.tableCount || tables.length || 0} 张表，{changed} 张需要更新 / {(summary.upToDateTables || []).length} 张已最新 / {(summary.blockedTables || []).length} 张阻断。</p>
      {tables.length > 0 ? (
        <div className="mini-table">
          {tables.slice(0, 24).map((table) => (
            <div className={`mini-row ${table.cacheStatus || "unknown"}`} key={table.tableId || table.displayName}>
              <strong>{table.displayName || table.tableId}</strong>
              <span>{humanCacheStatus(table.cacheStatus)}</span>
              {table.blockers?.length ? <small>{table.blockers[0]}</small> : null}
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function humanCacheStatus(status?: string) {
  switch ((status || "").toLowerCase()) {
    case "uptodate":
      return "已最新";
    case "needsupdate":
      return "需写 cache";
    case "missingcache":
      return "缺 cache";
    case "blocked":
      return "阻断";
    default:
      return "待确认";
  }
}

function NewTableMiniForm(props: { draft: NewTableDraft; onChange: (draft: NewTableDraft) => void; errors: string[] }) {
  const updateField = (index: number, patch: Partial<NewTableDraft["fields"][number]>) => {
    props.onChange({
      ...props.draft,
      fields: props.draft.fields.map((field, i) => i === index ? { ...field, ...patch } : field)
    });
  };
  const moveField = (index: number, delta: number) => {
    const next = [...props.draft.fields];
    const target = index + delta;
    if (target < 0 || target >= next.length) {
      return;
    }

    const [field] = next.splice(index, 1);
    next.splice(target, 0, field);
    props.onChange({ ...props.draft, fields: next });
  };
  const addField = () => props.onChange({
    ...props.draft,
    fields: [...props.draft.fields, { key: "", displayName: "", type: "string", description: "", required: false }]
  });
  const duplicateField = (index: number) => {
    const source = props.draft.fields[index];
    const copy = { ...source, key: `${source.key || "field"}Copy`, displayName: `${source.displayName || "字段"} 副本`, primary: false };
    const next = [...props.draft.fields];
    next.splice(index + 1, 0, copy);
    props.onChange({ ...props.draft, fields: next });
  };
  const deleteField = (index: number) => {
    props.onChange({ ...props.draft, fields: props.draft.fields.filter((_, i) => i !== index) });
  };

  return (
    <div className="new-table-form">
      <div className="form-grid">
        <label>配表ID<input value={props.draft.tableId} onChange={(event) => props.onChange({ ...props.draft, tableId: event.target.value })} placeholder="例如 SkillExtraData" /></label>
        <label>显示名称<input value={props.draft.displayName} onChange={(event) => props.onChange({ ...props.draft, displayName: event.target.value })} placeholder="例如 技能扩展数据" /></label>
        <label>这张表由谁负责
          <select value={props.draft.ownerRole} onChange={(event) => props.onChange({ ...props.draft, ownerRole: event.target.value })}>
            <option value="currentUser">当前创建人</option>
            <option value="tableOwner">表负责人</option>
            <option value="configOwner">配置负责人</option>
            <option value="later">稍后在注册中心补</option>
          </select>
        </label>
      </div>
      <div className="role-hint">
        <strong>负责人说明</strong>
        <span>表负责人负责这张表的日常维护；配置负责人负责最终流程、临时放行和 PR gate 例外。角色列表后续会优先从在线注册中心读取。</span>
      </div>
      <p className="muted">字段类型只能从 ExcelToSO 支持列表选择，避免生成 Unity 无法导入的 cache。</p>
      <div className="field-toolbar">
        <button className="secondary" onClick={addField}>添加字段</button>
        <button className="secondary" onClick={() => props.onChange({
          ...props.draft,
          fields: [
            { key: "id", displayName: "ID", type: "string", description: "唯一 ID", primary: true, required: true },
            { key: "name", displayName: "名称", type: "string", description: "显示名称", required: true }
          ]
        })}>重置默认字段</button>
      </div>
      <div className="field-table">
        {props.draft.fields.map((field, index) => (
          <div className="field-row" key={`${field.key}-${index}`}>
            <input value={field.key} onChange={(event) => updateField(index, { key: event.target.value })} placeholder="字段 key" />
            <input value={field.displayName} onChange={(event) => updateField(index, { displayName: event.target.value })} placeholder="中文名" />
            <select value={field.type} onChange={(event) => updateField(index, { type: event.target.value as FieldType })}>
              {fieldTypes.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}
            </select>
            <input value={field.description} onChange={(event) => updateField(index, { description: event.target.value })} placeholder="说明" />
            <input value={field.defaultValue || ""} onChange={(event) => updateField(index, { defaultValue: event.target.value })} placeholder="默认值，可空" />
            <label className="inline-check"><input type="checkbox" checked={Boolean(field.primary)} onChange={(event) => updateField(index, { primary: event.target.checked })} />唯一ID</label>
            <label className="inline-check"><input type="checkbox" checked={Boolean(field.required)} onChange={(event) => updateField(index, { required: event.target.checked })} />必填</label>
            <div className="field-actions">
              <button className="icon-button" title="上移字段" disabled={index === 0} onClick={() => moveField(index, -1)}>↑</button>
              <button className="icon-button" title="下移字段" disabled={index + 1 >= props.draft.fields.length} onClick={() => moveField(index, 1)}>↓</button>
              <button className="icon-button" title="复制字段" onClick={() => duplicateField(index)}>⧉</button>
              <button className="icon-button danger-button" title="删除字段" disabled={props.draft.fields.length <= 1} onClick={() => deleteField(index)}>×</button>
            </div>
          </div>
        ))}
      </div>
      {props.errors.length > 0 ? <div className="warning-list">{props.errors.slice(0, 3).map((error) => <p key={error}>{error}</p>)}</div> : null}
    </div>
  );
}
