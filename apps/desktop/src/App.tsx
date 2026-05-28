import { useCallback, useEffect, useMemo, useState } from "react";
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
  githubRepository: string;
  gitBranchUrl: string;
  prUrl: string;
  prNumber: string;
};

type ToolCheck = {
  name: string;
  status: "ok" | "warning" | "error";
  summary: string;
  detail: string;
  executablePath?: string;
  source?: string;
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
};

type WorkflowStep = {
  title: string;
  detail: string;
  button: string;
  operation: string;
  kind: "safe" | "medium" | "danger";
};

type IdentityMode = "strict-bot" | "user-fallback";

const identityPrefKey = "ConfigSheetForge.Desktop.IdentityMode";

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

function isInteractiveSafeFallbackOperation(operation: string) {
  return operation === "registry-status"
    || operation === "sync-cache-dry-run"
    || operation === "compare-merge";
}

function buildCommandArgs(operation: string, snapshot: ProjectSnapshot | null, identityMode: IdentityMode): string[] {
  const manifestArgs = snapshot?.projectConfigPath ? ["--manifest", snapshot.projectConfigPath] : [];
  const fallbackArgs = identityMode === "user-fallback" && isInteractiveSafeFallbackOperation(operation)
    ? ["--interactive-desktop", "--allow-user-fallback"]
    : [];
  switch (operation) {
    case "registry-status":
      return ["registry-status", ...manifestArgs, "--details", ...fallbackArgs];
    case "sync-cache-dry-run":
      return ["sync-cache", ...manifestArgs, "--dry-run", "--details", ...fallbackArgs];
    case "sync-cache-apply":
      return ["sync-cache", ...manifestArgs, "--yes", "--details"];
    case "repair-cache-dialect":
      return ["repair-cache-dialect", ...manifestArgs, "--dry-run", "--details"];
    case "pr-gate-report":
      return ["apply-contract", "--operation", "pr-gate-report", "--details"];
    case "compare-merge":
      return ["apply-contract", "--operation", "compare-merge", "--dry-run", "--details", ...fallbackArgs];
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
  const [identityMode, setIdentityMode] = useState<IdentityMode>(() => {
    if (typeof localStorage === "undefined") {
      return "strict-bot";
    }

    return localStorage.getItem(identityPrefKey) === "user-fallback" ? "user-fallback" : "strict-bot";
  });
  const [botAppId, setBotAppId] = useState("");
  const [botSecret, setBotSecret] = useState("");

  const runtimeAvailable = isTauriRuntime();
  const elapsed = useMemo(() => {
    if (!operationStartedAt) {
      return "";
    }

    return `${Math.max(0, Math.round((Date.now() - operationStartedAt) / 1000))} 秒`;
  }, [operationStartedAt, activeOperation]);
  const larkCheck = checks.find((check) => check.name === "lark-cli");
  const cliCheck = checks.find((check) => check.name === "Config Sheet Forge CLI");

  const persistIdentityMode = useCallback((mode: IdentityMode) => {
    setIdentityMode(mode);
    localStorage.setItem(identityPrefKey, mode);
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
      const toolChecks = await invoke<ToolCheck[]>("doctor_tools", { projectRoot: result.projectRoot });
      setChecks(toolChecks);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : String(ex));
    } finally {
      setActiveOperation("");
      setOperationStartedAt(null);
    }
  }, [projectRoot, runtimeAvailable]);

  useEffect(() => {
    if (!runtimeAvailable || projectRoot) {
      return;
    }

    invoke<string>("startup_project_root")
      .then((root) => {
        if (root) {
          setProjectRoot(root);
          void discover(root);
        }
      })
      .catch(() => {
        // 启动参数只是便利用途，读取失败时仍允许手动粘贴项目目录。
      });
  }, [projectRoot, runtimeAvailable, discover]);

  const runSetupAction = useCallback(async (action: string) => {
    setError("");
    setActiveOperation(action);
    setOperationStartedAt(Date.now());
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

      const result = await invoke<CliRunResult>("run_setup_action", {
        projectRoot: snapshot?.projectRoot || projectRoot,
        action,
        appId: botAppId,
        secretValue: botSecret
      });
      setLastResult(result);
      if (action === "configure_lark_bot") {
        setBotSecret("");
      }
      await discover(snapshot?.projectRoot || projectRoot);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : String(ex));
    } finally {
      setActiveOperation("");
      setOperationStartedAt(null);
    }
  }, [botAppId, botSecret, discover, projectRoot, runtimeAvailable, snapshot?.projectRoot]);

  const runOperation = useCallback(async (operation: string) => {
    setError("");
    if (operation === "sync-cache-apply") {
      const confirmed = window.confirm("写入本地 cache 会更新 .config-sheet-forge/excel-cache 和 .config-sheet-forge/cache。不会写旧 Excel/，不会写飞书。请确认你已经看过最近一次同步预览。");
      if (!confirmed) {
        return;
      }
    }

    if (identityMode === "user-fallback" && operation === "pr-gate-report") {
      const confirmed = window.confirm("PR hard gate 默认 strict bot。Desktop 不会用用户身份伪装 CI 通过。是否继续按 strict bot 运行 PR 检查？");
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
          commandLine: `config-sheet-forge ${buildCommandArgs(operation, snapshot, identityMode).join(" ")}`,
          exitCode: 0,
          stdout: "Desktop web preview：Tauri 运行时未连接。打包为 Desktop 后会调用内置 sidecar CLI。",
          stderr: ""
        });
        return;
      }

      const result = await invoke<CliRunResult>("run_cli", {
        projectRoot: snapshot?.projectRoot || projectRoot,
        args: buildCommandArgs(operation, snapshot, identityMode)
      });
      setLastResult(result);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : String(ex));
    } finally {
      setActiveOperation("");
      setOperationStartedAt(null);
    }
  }, [identityMode, projectRoot, runtimeAvailable, snapshot]);

  return (
    <main className="app-shell">
      <header className="top-bar">
        <div>
          <p className="eyebrow">Config Sheet Forge Desktop</p>
          <h1>配表 Source of Truth 工作台</h1>
        </div>
        <div className="top-actions">
          <button className="secondary" onClick={() => void runOperation("doctor")}>环境检查</button>
          <button className="primary" onClick={() => void discover()}>识别项目</button>
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
            <p>当前阶段：准备环境 / 启动本机工具。安装、授权、飞书读取和导出可能需要一点时间。</p>
          </div>
          <span>{elapsed}</span>
        </section>
      ) : null}

      <section className="identity-card">
        <div>
          <h2>身份策略</h2>
          <p>交互式 Desktop 可以 bot-first，再经你确认后使用飞书用户身份继续；CI / PR hard gate 默认 strict bot，不会静默 fallback。</p>
        </div>
        <div className="segmented">
          <button className={identityMode === "strict-bot" ? "selected" : ""} onClick={() => persistIdentityMode("strict-bot")}>strict bot</button>
          <button className={identityMode === "user-fallback" ? "selected" : ""} onClick={() => persistIdentityMode("user-fallback")}>允许用户身份预览</button>
        </div>
      </section>

      <section className="status-grid">
        <StatusCard title="CLI" value={cliCheck?.status === "ok" ? "内置可用" : "需要处理"} detail={cliCheck?.detail || "release zip 应包含 sidecar config-sheet-forge CLI。"} />
        <StatusCard title="当前分支" value={snapshot?.gitBranch || "等待识别"} detail="从 git branch 读取。" url={snapshot?.gitBranchUrl} onOpen={openExternal} />
        <StatusCard title="Feishu profile" value={snapshot?.feishuProfile || "等待识别"} detail="按项目 profile 模板推导。" />
        <StatusCard title="在线注册中心" value={snapshot?.registryBaseToken ? "已配置" : "等待检查"} detail={`Base: ${redact(snapshot?.registryBaseToken || "")}`} url={snapshot?.registryBaseUrl} onOpen={openExternal} />
        <StatusCard title="GitHub PR" value={snapshot?.prNumber ? `PR #${snapshot.prNumber}` : "等待识别"} detail={snapshot?.prUrl ? "点击打开当前分支 PR。" : "gh 可用时自动识别。"} url={snapshot?.prUrl} onOpen={openExternal} />
        <StatusCard title="PR gate" value="等待检查" detail="通过后写 Temp/ConfigSheetForge/pr-gate-report.json。" />
      </section>

      <section className="recommendation">
        <div>
          <p className="eyebrow">推荐下一步</p>
          <h2>先完成环境检查，再预览同步计划</h2>
          <p>预览只读取飞书和本地 cache，不写飞书、不写旧 Excel、不改 ProjectSettings。</p>
        </div>
        <button className="primary large" onClick={() => void runOperation("sync-cache-dry-run")}>预览同步计划</button>
      </section>

      <section className="workflow">
        {defaultSteps.map((step) => (
          <article className={`step ${step.kind}`} key={step.title}>
            <h3>{step.title}</h3>
            <p>{step.detail}</p>
            <button onClick={() => void runOperation(step.operation)}>{step.button}</button>
          </article>
        ))}
      </section>

      <section className="tools-panel">
        <div>
          <h2>一键环境 / 授权</h2>
          <p>缺工具时直接点按钮安装。所有安装和授权只改本机环境，不改 ProjectSettings、Packages、旧 Excel/ 或 cache。</p>
        </div>
        <div className="tool-list">
          {checks.length === 0 ? <p className="muted">点击“识别项目”或“环境检查”后显示。</p> : checks.map((check) => (
            <div className={`tool-check ${check.status}`} key={check.name}>
              <div className="tool-check-main">
                <strong>{check.name}：{statusLabel(check.status)}</strong>
                <span>{check.summary}</span>
              </div>
              <small>{check.detail}</small>
              {check.executablePath ? <code>{check.executablePath}</code> : null}
              <div className="tool-actions">
                {check.action ? <button onClick={() => void runSetupAction(check.action || "")}>{check.actionLabel || "处理"}</button> : null}
                {check.name === "gh" && check.status !== "error" ? <button onClick={() => void runSetupAction("gh_auth")}>GitHub 授权</button> : null}
                {check.name === "lark-cli" && check.status !== "error" ? (
                  <>
                    <button onClick={() => void runSetupAction("lark_doctor")}>重新 doctor</button>
                    <button onClick={() => void runSetupAction("lark_auth_user")}>登录飞书用户身份</button>
                  </>
                ) : null}
                {check.url ? <button className="secondary" onClick={() => void openExternal(check.url)}>打开说明</button> : null}
              </div>
            </div>
          ))}
        </div>
      </section>

      <section className="bot-card">
        <div>
          <h2>配置飞书 bot</h2>
          <p>App Secret 只通过 stdin 传给 lark-cli，不写仓库、不进命令行、不显示在日志里。</p>
        </div>
        <div className="bot-form">
          <input value={botAppId} onChange={(event) => setBotAppId(event.target.value)} placeholder="App ID，例如 cli_xxx" />
          <input value={botSecret} onChange={(event) => setBotSecret(event.target.value)} placeholder="App Secret" type="password" />
          <button onClick={() => void runSetupAction("configure_lark_bot")}>配置飞书 bot</button>
        </div>
      </section>

      <section className="operation-grid">
        <button onClick={() => void runOperation("registry-status")}>读取 live registry 状态</button>
        <button onClick={() => void runOperation("sync-cache-dry-run")}>预览同步计划</button>
        <button onClick={() => void runOperation("repair-cache-dialect")}>重写 cache dialect 预览</button>
        <button onClick={() => void runOperation("pr-gate-report")}>运行 PR 检查</button>
      </section>

      <section className="result-panel">
        <div className="result-header">
          <div>
            <h2>最近结果</h2>
            <p>{lastResult ? `ExitCode ${lastResult.exitCode} · ${lastResult.source || "unknown source"}` : "暂无任务结果。"}</p>
          </div>
          <button className="secondary" onClick={() => setLogExpanded((value) => !value)}>
            {logExpanded ? "收起详细日志" : "展开详细日志"}
          </button>
        </div>
        {lastResult ? (
          <>
            <pre className="command">{lastResult.commandLine}</pre>
            {lastResult.executablePath ? <p className="muted">工具路径：{lastResult.executablePath}</p> : null}
            {lastResult.attemptedPaths?.length ? <p className="muted">尝试路径：{lastResult.attemptedPaths.slice(0, 8).join("；")}</p> : null}
            {logExpanded ? <pre className="logs">{`${lastResult.stdout}\n${lastResult.stderr}`.trim()}</pre> : null}
          </>
        ) : null}
      </section>
    </main>
  );
}

function StatusCard(props: { title: string; value: string; detail: string; url?: string; onOpen?: (url?: string) => void }) {
  return (
    <article className="status-card">
      <span>{props.title}</span>
      <strong>{props.value}</strong>
      <p>{props.detail}</p>
      {props.url ? <button className="link-button" onClick={() => props.onOpen?.(props.url)}>打开</button> : null}
    </article>
  );
}
