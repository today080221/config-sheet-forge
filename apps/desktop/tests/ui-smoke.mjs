import { readFileSync } from "node:fs";

const app = readFileSync(new URL("../src/App.tsx", import.meta.url), "utf8");
const workflow = readFileSync(new URL("../src/workflow.ts", import.meta.url), "utf8");
const css = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");
const tauri = readFileSync(new URL("../src-tauri/tauri.conf.json", import.meta.url), "utf8");

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

for (const viewport of ["1280x720", "1366x768", "1600x900", "1920x1080", "125% zoom"]) {
  assert(css.includes("overflow-x: hidden"), `${viewport}: ordinary view must prevent horizontal scroll`);
  assert(css.includes("min-width: 0"), `${viewport}: grid/card items need min-width: 0 overflow guard`);
  assert(css.includes("text-overflow: ellipsis"), `${viewport}: long labels need ellipsis`);
}

for (const marker of ["策划视图", "程序视图", "Debug", "scenario-grid", "stepper", "primaryOperation"]) {
  assert(app.includes(marker), `Desktop scenario UI marker missing: ${marker}`);
}

assert(app.includes("debugEnabled && visibleResult"), "Debug drawer must gate command/stdout/result JSON rendering");
assert(app.includes("debugEnabled && check.executablePath"), "Debug off must hide full executable paths");
assert(app.includes("className=\"debug-drawer\""), "Debug drawer markup is missing");
assert(app.includes("visibleResult.commandLine"), "Debug mode must expose full command");
assert(app.includes("visibleResult.resultJson"), "Debug mode must expose result JSON");
assert(app.includes("activeTask?.progressLog"), "Debug mode must tail progress ndjson while a task is running");
assert(app.includes("read_desktop_result"), "Desktop must restore workflow state from previous result files");
assert(app.includes("read_bridge_response"), "Desktop must poll Unity bridge responses instead of fire-and-forget commands");
assert(app.includes("scan_bridge_processed_results"), "Desktop must recover bridge processed results that were completed before polling");
assert(app.includes("project-lifecycle"), "PR gate must use the project adapter lifecycle pipeline");
assert(!app.includes('"apply-contract", "--operation", "pr-gate-report"'), "PR gate must not call apply-contract --operation directly");
assert(app.includes("正在请求 Unity 导入"), "Desktop must show a human readable Unity import running state");
assert(app.includes("unity-import-assets"), "Desktop must understand direct Unity import results");
assert(app.includes("buildProjectState"), "Desktop must render status cards from one normalized project state");
assert(app.includes("mode-card"), "Standalone mode must show a clear non-bridge explanation");
assert(workflow.includes("getVersionStatus"), "Desktop must block mismatched Desktop/UPM/CLI versions");
assert(workflow.includes("Unity 导入项"), "Unity import summaries must distinguish import items from online tables");
assert(app.includes("readResultAfterTaskCompletion"), "Desktop must consume result JSON immediately after a background task completes");
assert(app.includes("shouldReadDesktopResultAfterTask"), "Desktop must reread --out result files when TaskSnapshot misses resultJson");
assert(app.includes("normalizeSyncCacheResult"), "Desktop must normalize sync-cache results before driving workflow state");
assert(app.includes("parseLifecycleResultJson"), "Desktop must parse lifecycle JSON through the BOM-safe parser");
assert(workflow.includes("syncResultSummaryLine"), "Planner result summary must come from normalized sync-cache result");
assert(workflow.includes("normalizeSyncCacheResult"), "Workflow state must expose one sync-cache normalize layer");
assert(workflow.includes("stripJsonBom"), "Workflow JSON parsing must strip UTF-8 BOM from old result files");
assert(workflow.includes("unityImportSummary"), "Workflow summaries must include direct Unity import results");
assert(app.includes("SyncTableSummary"), "Result details must show structured table summaries instead of raw JSON");
assert(app.includes("resultNextAction"), "Result panel must offer the next workflow action directly");
assert(app.includes("start_task"), "Lifecycle actions must start backend tasks instead of blocking run_cli");
assert(app.includes("start_setup_task"), "Setup/auth actions must start backend tasks instead of blocking run_setup_action");
assert(app.includes("start_tool_check_task"), "Tool doctor checks must run as background tasks");
assert(app.includes("cancel_task"), "Running tasks must expose cancellation");
assert(app.includes("取消本次预览（不写 cache/飞书）"), "Running task card must explain cancellation safety");
assert(app.includes("version-strip"), "Desktop must show Desktop/UPM/CLI version strip");
assert(app.includes("Desktop v{desktopVersion}"), "Desktop version must be visible in ordinary view");
assert(app.includes("UPM {unityVersion}"), "Unity package version must be visible in ordinary view");
assert(app.includes("CLI {cliVersion}"), "CLI sidecar version/source must be visible in ordinary view");
assert(app.includes("快速状态检查（不导出 xlsx）"), "Home refresh must be a quick status check, not a full xlsx export");
assert(app.includes("完整同步预览会读取在线表并临时导出 xlsx"), "Full sync preview must explain why it can take minutes");
assert(app.includes("repair-cache-dialect-apply"), "Desktop must expose an apply path for offline cache dialect repair");
assert(workflow.includes("dialectOutdated"), "Workflow must distinguish importability/dialect repair from semantic cache freshness");
assert(workflow.includes("修复 cache 类型行"), "Planner view must recommend cache dialect repair in human wording");
assert(!app.includes("<p>Task："), "Ordinary running card must not show internal task ids");
assert(!app.includes("invoke<CliRunResult>(\"run_cli\""), "UI must not invoke blocking run_cli");
assert(!app.includes("invoke<CliRunResult>(\"run_setup_action\""), "UI must not invoke blocking run_setup_action");
assert(app.includes("shouldShowBotSecretForm(larkCheck, showBotConfigure)"), "Bot secret form must be hidden when bot is already configured");
assert(app.includes("ToolActions"), "Tool/auth cards must use the shared action model");
assert(app.includes("primaryToolAction"), "Tool/auth cards must distinguish primary auth/install actions from secondary actions");
assert(app.includes("添加字段"), "New-table editor must let users add fields");
assert(app.includes("复制字段"), "New-table editor must let users duplicate fields");
assert(app.includes("ExcelToSO 支持列表"), "New-table validation must use ExcelToSO dialect field types");

for (const stableLayoutMarker of [
  "scrollbar-gutter: stable",
  "grid-auto-rows: 146px",
  "height: 146px",
  "grid-auto-rows: 128px",
  "height: 128px",
  "white-space: normal",
  "max-height: 360px"
]) {
  assert(css.includes(stableLayoutMarker), `Stable scene switching CSS marker missing: ${stableLayoutMarker}`);
}

for (const rawLeak of ["完整命令", "raw JSON", "stdout 第一行"]) {
  assert(!app.includes(rawLeak), `ordinary view leaked debug wording: ${rawLeak}`);
}

assert(tauri.includes("icons/icon.ico"), "Tauri Windows icon resource must be configured");
assert(tauri.includes("frontendDist"), "Tauri production frontendDist must stay configured");

console.log("Desktop UI smoke passed.");
