import { useCallback, useEffect, useMemo, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import {
  decideRecommendedScenario,
  decideWorkflow,
  getScenario,
  humanToolStatus,
  ordinaryToolText,
  primaryToolAction,
  scenarios,
  secondaryToolActions,
  shouldShowBotSecretForm,
  summarizeLifecycleResult,
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
  pid?: number;
};

type StartupContext = {
  projectRoot: string;
  bridgeSessionDir: string;
};

type IdentityMode = "strict-bot" | "user-fallback";

const identityPrefKey = "ConfigSheetForge.Desktop.IdentityMode";
const scenarioPrefKey = "ConfigSheetForge.Desktop.LastScenario";
const viewPrefKey = "ConfigSheetForge.Desktop.ViewMode";
const debugPrefKey = "ConfigSheetForge.Desktop.Debug";

const operationLabels: Record<string, string> = {
  doctor: "环境检查",
  "registry-status": "读取在线注册中心",
  "sync-cache-dry-run": "预览同步",
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
  { value: "integer", label: "整数" },
  { value: "number", label: "小数" },
  { value: "bool", label: "是/否" },
  { value: "date", label: "日期" },
  { value: "datetime", label: "日期时间" },
  { value: "enum", label: "枚举" },
  { value: "json", label: "JSON" }
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

function isStructuredLifecycle(value: unknown): value is LifecycleResultLike {
  return Boolean(value) && typeof value === "object";
}

function parseResultJson(text: string | undefined): LifecycleResultLike | null {
  if (!text?.trim()) {
    return null;
  }

  try {
    const parsed = JSON.parse(text) as unknown;
    return isStructuredLifecycle(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function projectPath(root: string | undefined, ...parts: string[]) {
  const base = (root || "").replace(/[\\/]$/, "");
  return [base, ...parts].filter(Boolean).join("/");
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
  previewResultPath: string
): string[] {
  const manifestArgs = snapshot?.projectConfigPath ? ["--manifest", snapshot.projectConfigPath] : [];
  const fallbackArgs = identityMode === "user-fallback" && isInteractiveSafeFallbackOperation(operation)
    ? ["--interactive-desktop", "--allow-user-fallback"]
    : [];
  const out = (name: string) => ["--out", desktopResultPath(snapshot, projectRoot, name), "--progress", desktopProgressPath(snapshot, projectRoot, name)];

  switch (operation) {
    case "registry-status":
      return ["registry-status", ...manifestArgs, "--details", ...out("registry-status"), ...fallbackArgs];
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
      return ["new-table", ...manifestArgs, "--dry-run", "--details", ...out("new-table")];
    default:
      return ["doctor", "--details", ...out("doctor")];
  }
}

function operationStage(operation: string) {
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

export function App() {
  const [projectRoot, setProjectRoot] = useState("");
  const [startup, setStartup] = useState<StartupContext>({ projectRoot: "", bridgeSessionDir: "" });
  const [snapshot, setSnapshot] = useState<ProjectSnapshot | null>(null);
  const [checks, setChecks] = useState<ToolCheck[]>([]);
  const [activeOperation, setActiveOperation] = useState("");
  const [activeTask, setActiveTask] = useState<TaskSnapshot | null>(null);
  const [operationStartedAt, setOperationStartedAt] = useState<number | null>(null);
  const [lastResult, setLastResult] = useState<CliRunResult | null>(null);
  const [lastResultParsed, setLastResultParsed] = useState<LifecycleResultLike | null>(null);
  const [lastSyncPreview, setLastSyncPreview] = useState<LifecycleResultLike | null>(null);
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
        setChecks(JSON.parse(finalTask.resultJson) as ToolCheck[]);
      } catch {
        setError("工具检查完成，但结果解析失败。请打开 Debug 查看诊断。");
      }
    } else if (finalTask.status === "canceled") {
      setLastResult(taskToCliResult(finalTask));
    } else {
      setError(finalTask.message || "工具检查失败。");
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
      setError(ex instanceof Error ? ex.message : String(ex));
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
      setError(ex instanceof Error ? ex.message : String(ex));
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
      setError(ex instanceof Error ? ex.message : String(ex));
    }
  }, [activeTask, botAppId, botSecret, projectRoot, runtimeAvailable, snapshot?.projectRoot, startToolCheckTask, waitForTask]);

  const applyResult = useCallback((operation: string, result: CliRunResult) => {
    setLastResult(result);
    const parsed = parseResultJson(result.resultJson);
    setLastResultParsed(parsed);
    if (!parsed) {
      return;
    }

    if (operation === "sync-cache-dry-run") {
      setLastSyncPreview(parsed);
      setLastSyncPreviewPath(result.resultPath || desktopResultPath(snapshot, projectRoot, "sync-cache"));
    } else if (operation === "sync-cache-apply") {
      setLastSyncPreview(parsed);
      setLastSyncPreviewPath(result.resultPath || desktopResultPath(snapshot, projectRoot, "sync-cache-apply"));
    } else if (operation === "compare-merge") {
      setLastComparePreview(parsed);
      setLastComparePreviewPath(result.resultPath || desktopResultPath(snapshot, projectRoot, "compare-merge"));
    } else if (operation === "pr-gate-report") {
      setLastGateReport(parsed);
    }
  }, [projectRoot, snapshot]);

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
      if (!lastSyncPreviewPath || !lastSyncPreview?.success) {
        setError("写入本地 cache 前必须先有最近一次同输入 sync-cache dry-run 成功结果。请重新预览同步。");
        return;
      }

      const status = lastSyncPreview.syncCacheSummary?.cacheStatus || "unknown";
      if (status.toLowerCase() === "uptodate") {
        setError("本地 cache 已最新，不需要写入。下一步请导入 Unity 配表资产。");
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
      const args = buildCommandArgs(operation, snapshot, projectRoot, identityMode, previewPath);
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
      const result = taskToCliResult(task);
      applyResult(operation, result);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : String(ex));
    }
  }, [
    applyResult,
    activeTask,
    identityMode,
    lastComparePreview,
    lastComparePreviewPath,
    lastSyncPreview,
    lastSyncPreviewPath,
    projectRoot,
    runtimeAvailable,
    sendUnityBridgeCommand,
    snapshot,
    waitForTask
  ]);

  const primaryDisabled = decision.primaryDisabled
    || (selectedScenario === "new-table" && decision.primaryOperation === "new-table-dry-run" && newTableErrors.length > 0)
    || Boolean(activeOperation);

  return (
    <main className="app-shell">
      <header className="top-bar">
        <div className="brand-block">
          <p className="eyebrow">Config Sheet Forge Desktop</p>
          <h1>配表 Source of Truth 工作台</h1>
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
            <strong>正在运行：{operationLabels[activeOperation] || activeOperation}</strong>
            <p>Task：{activeTask?.taskId || "准备中"}</p>
            <p>当前阶段：{activeTask?.phase || operationStage(activeOperation)}</p>
            {activeTask?.message ? <p>{activeTask.message}</p> : null}
            <p>安全性：{activeOperation.includes("dry-run") || activeOperation.includes("preview") ? "只读预览，不写文件。" : decision.safety}</p>
            {(activeTask?.elapsedMs || 0) > 10000 ? <p>仍在运行，可以取消；取消会终止进程树，不会继续写入本地 cache/飞书。</p> : null}
          </div>
          <div className="running-actions">
            <span>{elapsed}</span>
            {activeTask?.taskId ? <button className="danger-button" onClick={() => void cancelActiveTask()}>取消</button> : null}
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
        <StatusCard title="在线表" value={lastSyncPreview?.branchStatus ? `${lastSyncPreview.branchStatus.tableCountRegistered || 0}/${lastSyncPreview.branchStatus.tableCountExpected || 0} 已登记` : "等待读取"} detail="以 live registry 为准。" />
        <StatusCard title="本地 cache" value={lastSyncPreview?.syncCacheSummary?.cacheStatus || "等待预览"} detail={summarizeLifecycleResult(lastSyncPreview)} />
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
              <button onClick={() => void runOperation("registry-status")}>刷新在线状态</button>
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
            <p>{lastResultParsed ? summarizeLifecycleResult(lastResultParsed) : lastResult ? `ExitCode ${lastResult.exitCode}` : "暂无任务结果。"}</p>
          </div>
          <button className="secondary" onClick={() => setLogExpanded((value) => !value)}>
            {logExpanded ? "收起" : debugEnabled ? "展开 Debug 日志" : "Debug 查看详情"}
          </button>
        </div>
        {debugEnabled && lastResult ? (
          <div className="debug-drawer" hidden={!logExpanded}>
            <pre className="command">{lastResult.commandLine}</pre>
            {lastResult.resultPath ? <p className="muted">result：{lastResult.resultPath}</p> : null}
            {lastResult.executablePath ? <p className="muted">工具路径：{lastResult.executablePath}</p> : null}
            {lastResult.attemptedPaths?.length ? <p className="muted">尝试路径：{lastResult.attemptedPaths.slice(0, 12).join("；")}</p> : null}
            <pre className="logs">{`${lastResult.stdout}\n${lastResult.stderr}`.trim()}</pre>
            {lastResult.resultJson ? <pre className="logs json-log">{lastResult.resultJson}</pre> : null}
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

function NewTableMiniForm(props: { draft: NewTableDraft; onChange: (draft: NewTableDraft) => void; errors: string[] }) {
  const updateField = (index: number, patch: Partial<NewTableDraft["fields"][number]>) => {
    props.onChange({
      ...props.draft,
      fields: props.draft.fields.map((field, i) => i === index ? { ...field, ...patch } : field)
    });
  };

  return (
    <div className="new-table-form">
      <div className="form-grid">
        <label>配表ID<input value={props.draft.tableId} onChange={(event) => props.onChange({ ...props.draft, tableId: event.target.value })} placeholder="例如 SkillExtraData" /></label>
        <label>显示名称<input value={props.draft.displayName} onChange={(event) => props.onChange({ ...props.draft, displayName: event.target.value })} placeholder="例如 技能扩展数据" /></label>
        <label>这张表由谁负责<input value={props.draft.ownerRole} onChange={(event) => props.onChange({ ...props.draft, ownerRole: event.target.value })} placeholder="默认 tableOwner" /></label>
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
            <label className="inline-check"><input type="checkbox" checked={Boolean(field.primary)} onChange={(event) => updateField(index, { primary: event.target.checked })} />唯一ID</label>
          </div>
        ))}
      </div>
      {props.errors.length > 0 ? <div className="warning-list">{props.errors.slice(0, 3).map((error) => <p key={error}>{error}</p>)}</div> : null}
    </div>
  );
}
