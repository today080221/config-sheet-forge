import { readFileSync } from "node:fs";

const app = readFileSync(new URL("../src/App.tsx", import.meta.url), "utf8");
const css = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");
const tauri = readFileSync(new URL("../src-tauri/tauri.conf.json", import.meta.url), "utf8");

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

for (const viewport of ["1280x720", "1366x768", "1920x1080"]) {
  assert(css.includes("overflow-x: hidden"), `${viewport}: ordinary view must prevent horizontal scroll`);
  assert(css.includes("min-width: 0"), `${viewport}: grid/card items need min-width: 0 overflow guard`);
  assert(css.includes("text-overflow: ellipsis"), `${viewport}: long labels need ellipsis`);
}

for (const marker of ["策划视图", "程序视图", "Debug", "scenario-grid", "stepper", "primaryOperation"]) {
  assert(app.includes(marker), `Desktop scenario UI marker missing: ${marker}`);
}

assert(app.includes("debugEnabled && lastResult"), "Debug drawer must gate command/stdout/result JSON rendering");
assert(app.includes("debugEnabled && check.executablePath"), "Debug off must hide full executable paths");
assert(app.includes("className=\"debug-drawer\""), "Debug drawer markup is missing");
assert(app.includes("lastResult.commandLine"), "Debug mode must expose full command");
assert(app.includes("lastResult.resultJson"), "Debug mode must expose result JSON");
assert(app.includes("start_task"), "Lifecycle actions must start backend tasks instead of blocking run_cli");
assert(app.includes("start_setup_task"), "Setup/auth actions must start backend tasks instead of blocking run_setup_action");
assert(app.includes("start_tool_check_task"), "Tool doctor checks must run as background tasks");
assert(app.includes("cancel_task"), "Running tasks must expose cancellation");
assert(app.includes("取消"), "Running task card must include a cancel button");
assert(!app.includes("invoke<CliRunResult>(\"run_cli\""), "UI must not invoke blocking run_cli");
assert(!app.includes("invoke<CliRunResult>(\"run_setup_action\""), "UI must not invoke blocking run_setup_action");
assert(app.includes("shouldShowBotSecretForm(larkCheck, showBotConfigure)"), "Bot secret form must be hidden when bot is already configured");
assert(app.includes("ToolActions"), "Tool/auth cards must use the shared action model");
assert(app.includes("primaryToolAction"), "Tool/auth cards must distinguish primary auth/install actions from secondary actions");

for (const rawLeak of ["完整命令", "raw JSON", "stdout 第一行"]) {
  assert(!app.includes(rawLeak), `ordinary view leaked debug wording: ${rawLeak}`);
}

assert(tauri.includes("icons/icon.ico"), "Tauri Windows icon resource must be configured");
assert(tauri.includes("frontendDist"), "Tauri production frontendDist must stay configured");

console.log("Desktop UI smoke passed.");
