import { useCallback, useMemo, useState } from "react";
import { invoke } from "@tauri-apps/api/core";

type ProjectSnapshot = {
  projectRoot: string;
  unityProject: boolean;
  projectConfigPath: string;
  projectId: string;
  gitBranch: string;
  feishuProfile: string;
  registryBaseToken: string;
  registryBaseUrl: string;
};

type ToolCheck = {
  name: string;
  status: "ok" | "warning" | "error";
  summary: string;
  detail: string;
};

type CliRunResult = {
  commandLine: string;
  exitCode: number;
  stdout: string;
  stderr: string;
};

type WorkflowStep = {
  title: string;
  detail: string;
  button: string;
  operation: string;
  kind: "safe" | "medium" | "danger";
};

const defaultSteps: WorkflowStep[] = [
  {
    title: "同步预览",
    detail: "只读取飞书和本地 cache，不写任何文件。",
    button: "预览同步计划",
    operation: "sync-cache-dry-run",
    kind: "safe"
  },
  {
    title: "写入本地 cache",
    detail: "只写 .config-sheet-forge，不碰旧 Excel/ 和飞书在线表。",
    button: "写入本地 cache",
    operation: "sync-cache-apply",
    kind: "medium"
  },
  {
    title: "导入 Unity asset",
    detail: "调用 ExcelToSO 的 SourceOfTruthCache profile，不改变本地 profile。",
    button: "导入 Unity 配表资产",
    operation: "unity-import",
    kind: "medium"
  },
  {
    title: "合并与 PR gate",
    detail: "先生成合并预览，再提交 MergeReviews，最后运行 PR 检查。",
    button: "生成合并预览",
    operation: "compare-merge",
    kind: "safe"
  }
];

function isTauriRuntime() {
  return typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
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

function statusLabel(status: ToolCheck["status"]) {
  if (status === "ok") {
    return "可用";
  }

  if (status === "warning") {
    return "需要处理";
  }

  return "不可用";
}

function buildCommandArgs(operation: string, snapshot: ProjectSnapshot | null): string[] {
  const manifestArgs = snapshot?.projectConfigPath ? ["--manifest", snapshot.projectConfigPath] : [];
  switch (operation) {
    case "registry-status":
      return ["registry-status", ...manifestArgs, "--details"];
    case "sync-cache-dry-run":
      return ["sync-cache", ...manifestArgs, "--dry-run", "--details"];
    case "sync-cache-apply":
      return ["sync-cache", ...manifestArgs, "--yes", "--details"];
    case "repair-cache-dialect":
      return ["repair-cache-dialect", ...manifestArgs, "--dry-run", "--details"];
    case "pr-gate-report":
      return ["apply-contract", "--operation", "pr-gate-report", "--details"];
    case "compare-merge":
      return ["apply-contract", "--operation", "compare-merge", "--dry-run", "--details"];
    case "unity-import":
      return ["doctor", "--details"];
    default:
      return ["doctor", "--details"];
  }
}

export function App() {
  const [projectRoot, setProjectRoot] = useState("");
  const [snapshot, setSnapshot] = useState<ProjectSnapshot | null>(null);
  const [checks, setChecks] = useState<ToolCheck[]>([]);
  const [activeOperation, setActiveOperation] = useState("");
  const [operationStartedAt, setOperationStartedAt] = useState<number | null>(null);
  const [lastResult, setLastResult] = useState<CliRunResult | null>(null);
  const [logExpanded, setLogExpanded] = useState(false);
  const [error, setError] = useState("");

  const runtimeAvailable = isTauriRuntime();
  const elapsed = useMemo(() => {
    if (!operationStartedAt) {
      return "";
    }

    return `${Math.max(0, Math.round((Date.now() - operationStartedAt) / 1000))} 秒`;
  }, [operationStartedAt, activeOperation]);

  const discover = useCallback(async () => {
    setError("");
    setActiveOperation("识别项目");
    setOperationStartedAt(Date.now());
    try {
      if (!runtimeAvailable) {
        setSnapshot({
          projectRoot: projectRoot || "K:/Unity/Project",
          unityProject: true,
          projectConfigPath: "ProjectSettings/Project.ConfigSheetForge.json",
          projectId: "demo",
          gitBranch: "当前分支",
          feishuProfile: "当前分支",
          registryBaseToken: "",
          registryBaseUrl: ""
        });
        return;
      }

      const result = await invoke<ProjectSnapshot>("discover_project", { projectRoot });
      setSnapshot(result);
      const toolChecks = await invoke<ToolCheck[]>("doctor_tools", { projectRoot: result.projectRoot });
      setChecks(toolChecks);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : String(ex));
    } finally {
      setActiveOperation("");
      setOperationStartedAt(null);
    }
  }, [projectRoot, runtimeAvailable]);

  const runOperation = useCallback(async (operation: string) => {
    setError("");
    if (operation === "sync-cache-apply") {
      const confirmed = window.confirm("写入本地 cache 会更新 .config-sheet-forge/excel-cache 和 .config-sheet-forge/cache。不会写旧 Excel/，不会写飞书。请确认你已经看过最近一次同步预览。");
      if (!confirmed) {
        return;
      }
    }

    if (operation === "unity-import") {
      setError("导入 Unity 配表资产必须由 Unity bridge 调用 ExcelToSO ImportByProfile(SourceOfTruthCache)。请回 Unity 点击“导入 Unity 配表资产”。");
      return;
    }

    setActiveOperation(operation);
    setOperationStartedAt(Date.now());
    setLastResult(null);
    try {
      if (!runtimeAvailable) {
        setLastResult({
          commandLine: `config-sheet-forge ${buildCommandArgs(operation, snapshot).join(" ")}`,
          exitCode: 0,
          stdout: "Desktop web preview：Tauri 运行时未连接。打包为 Desktop 后会调用本机 CLI。",
          stderr: ""
        });
        return;
      }

      const result = await invoke<CliRunResult>("run_cli", {
        projectRoot: snapshot?.projectRoot || projectRoot,
        args: buildCommandArgs(operation, snapshot)
      });
      setLastResult(result);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : String(ex));
    } finally {
      setActiveOperation("");
      setOperationStartedAt(null);
    }
  }, [projectRoot, runtimeAvailable, snapshot]);

  return (
    <main className="app-shell">
      <header className="top-bar">
        <div>
          <p className="eyebrow">Config Sheet Forge Desktop</p>
          <h1>配表 Source of Truth 工作台</h1>
        </div>
        <div className="top-actions">
          <button className="secondary" onClick={() => runOperation("doctor")}>环境检查</button>
          <button className="primary" onClick={discover}>识别项目</button>
        </div>
      </header>

      <section className="project-picker">
        <label>
          Unity 项目目录
          <input
            value={projectRoot}
            onChange={(event) => setProjectRoot(event.target.value)}
            placeholder="选择或粘贴 Unity project root，例如 K:/Unity/MyGame"
          />
        </label>
        <p>Desktop 会读取 ProjectSettings/*ConfigSheetForge*.json。长任务都在后台进程运行，不阻塞 Unity。</p>
      </section>

      {error ? <section className="error-card">{error}</section> : null}

      {activeOperation ? (
        <section className="running-card">
          <div>
            <strong>正在运行：{activeOperation}</strong>
            <p>当前阶段：准备环境 / 启动 Config Sheet Forge CLI。网络读取和导出可能需要几分钟。</p>
          </div>
          <span>{elapsed}</span>
        </section>
      ) : null}

      <section className="status-grid">
        <StatusCard title="当前分支" value={snapshot?.gitBranch || "等待识别"} detail="从 git branch 读取。" />
        <StatusCard title="Feishu profile" value={snapshot?.feishuProfile || "等待识别"} detail="按项目 profile 模板推导。" />
        <StatusCard title="在线注册中心" value={snapshot?.registryBaseToken ? "已配置" : "等待检查"} detail={`Base: ${redact(snapshot?.registryBaseToken || "")}`} />
        <StatusCard title="本地 cache" value="等待 sync-status" detail="Desktop 会以 live registry 为准，不要求 ProjectSettings 保存 Sheet token。" />
        <StatusCard title="PR gate" value="等待检查" detail="通过后写 Temp/ConfigSheetForge/pr-gate-report.json。" />
      </section>

      <section className="recommendation">
        <div>
          <p className="eyebrow">推荐下一步</p>
          <h2>先预览同步计划</h2>
          <p>预览只读取飞书和本地 cache，不写飞书、不写旧 Excel、不改 ProjectSettings。</p>
        </div>
        <button className="primary large" onClick={() => runOperation("sync-cache-dry-run")}>预览同步计划</button>
      </section>

      <section className="workflow">
        {defaultSteps.map((step) => (
          <article className={`step ${step.kind}`} key={step.title}>
            <h3>{step.title}</h3>
            <p>{step.detail}</p>
            <button onClick={() => runOperation(step.operation)}>{step.button}</button>
          </article>
        ))}
      </section>

      <section className="tools-panel">
        <div>
          <h2>环境检查</h2>
          <p>lark-cli 用于飞书，gh 用于自动识别 GitHub PR。缺 gh 不影响同步，只需要手动选择目标分支。</p>
        </div>
        <div className="tool-list">
          {checks.length === 0 ? <p className="muted">点击“识别项目”或“环境检查”后显示。</p> : checks.map((check) => (
            <div className={`tool-check ${check.status}`} key={check.name}>
              <strong>{check.name}：{statusLabel(check.status)}</strong>
              <span>{check.summary}</span>
              <small>{check.detail}</small>
            </div>
          ))}
        </div>
      </section>

      <section className="operation-grid">
        <button onClick={() => runOperation("registry-status")}>读取 live registry 状态</button>
        <button onClick={() => runOperation("sync-cache-dry-run")}>预览同步计划</button>
        <button onClick={() => runOperation("repair-cache-dialect")}>重写 cache dialect 预览</button>
        <button onClick={() => runOperation("pr-gate-report")}>运行 PR 检查</button>
      </section>

      <section className="result-panel">
        <div className="result-header">
          <div>
            <h2>最近结果</h2>
            <p>{lastResult ? `ExitCode ${lastResult.exitCode}` : "暂无任务结果。"}</p>
          </div>
          <button className="secondary" onClick={() => setLogExpanded((value) => !value)}>
            {logExpanded ? "收起详细日志" : "展开详细日志"}
          </button>
        </div>
        {lastResult ? (
          <>
            <pre className="command">{lastResult.commandLine}</pre>
            {logExpanded ? <pre className="logs">{`${lastResult.stdout}\n${lastResult.stderr}`.trim()}</pre> : null}
          </>
        ) : null}
      </section>
    </main>
  );
}

function StatusCard(props: { title: string; value: string; detail: string }) {
  return (
    <article className="status-card">
      <span>{props.title}</span>
      <strong>{props.value}</strong>
      <p>{props.detail}</p>
    </article>
  );
}
