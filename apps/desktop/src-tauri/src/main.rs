use serde::Serialize;
use serde_json::Value;
use std::collections::HashMap;
use std::env;
use std::fs;
use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use std::thread;
use std::time::{Duration, Instant};

static TASKS: OnceLock<Mutex<HashMap<String, Arc<Mutex<TaskState>>>>> = OnceLock::new();
static NEXT_TASK_ID: AtomicU64 = AtomicU64::new(1);

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ProjectSnapshot {
    project_root: String,
    unity_project: bool,
    project_config_path: String,
    unity_package_version: String,
    project_id: String,
    git_branch: String,
    feishu_profile: String,
    registry_base_token: String,
    registry_base_url: String,
    github_repository: String,
    git_branch_url: String,
    pr_url: String,
    pr_number: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct StartupContext {
    project_root: String,
    bridge_session_dir: String,
    desktop_version: String,
    sidecar_cli_version: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ToolAction {
    action: String,
    label: String,
    kind: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ToolCheck {
    name: String,
    status: String,
    summary: String,
    detail: String,
    installed: bool,
    executable_path: String,
    source: String,
    authenticated: bool,
    account_label: String,
    scopes_ok: bool,
    next_action: String,
    next_action_label: String,
    secondary_actions: Vec<ToolAction>,
    bot_configured: bool,
    bot_label: String,
    user_authenticated: bool,
    user_label: String,
    action: String,
    action_label: String,
    url: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CliRunResult {
    command_line: String,
    exit_code: i32,
    stdout: String,
    stderr: String,
    executable_path: String,
    source: String,
    attempted_paths: Vec<String>,
    result_path: String,
    result_json: String,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct TaskSnapshot {
    task_id: String,
    operation: String,
    status: String,
    phase: String,
    message: String,
    elapsed_ms: u128,
    command_line: String,
    exit_code: i32,
    stdout: String,
    stderr: String,
    progress_log: String,
    result_path: String,
    result_json: String,
    executable_path: String,
    source: String,
    attempted_paths: Vec<String>,
    progress_path: String,
    pid: u32,
    table_id: String,
    current: i64,
    total: i64,
}

struct TaskState {
    snapshot: TaskSnapshot,
    started_at: Instant,
    finished_at: Option<Instant>,
    cancel_requested: bool,
}

#[derive(Clone)]
struct TaskSpec {
    operation: String,
    root: PathBuf,
    tool: ResolvedTool,
    args: Vec<String>,
    lark_cli: Option<ResolvedTool>,
    stdin: Option<String>,
    result_path: String,
    progress_path: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ReleaseSmokeReport {
    version: &'static str,
    release_build: bool,
    dev_server_url_embedded: bool,
    frontend_markers_embedded: bool,
    sidecar_cli_found: bool,
    sidecar_cli_smoke_ok: bool,
}

#[derive(Clone, Debug)]
struct ResolvedTool {
    program: String,
    prefix_args: Vec<String>,
    display_path: String,
    source: String,
    attempted_paths: Vec<String>,
}

impl ResolvedTool {
    fn command_args(&self, args: &[&str]) -> Vec<String> {
        let mut all = self.prefix_args.clone();
        all.extend(args.iter().map(|arg| (*arg).to_string()));
        all
    }
}

#[tauri::command]
fn startup_project_root() -> String {
    env::args().nth(1).unwrap_or_default()
}

#[tauri::command]
fn startup_context() -> StartupContext {
    let args: Vec<String> = env::args().collect();
    let project_root = args
        .iter()
        .skip(1)
        .find(|arg| !arg.starts_with("--"))
        .cloned()
        .unwrap_or_default();
    let bridge_session_dir = args
        .windows(2)
        .find(|pair| pair[0] == "--bridge-session")
        .map(|pair| pair[1].clone())
        .unwrap_or_default();
    StartupContext {
        project_root: normalize_path_string_for_cli(&project_root),
        bridge_session_dir: normalize_path_string_for_cli(&bridge_session_dir),
        desktop_version: env!("CARGO_PKG_VERSION").to_string(),
        sidecar_cli_version: resolve_sidecar_cli()
            .map(|_| env!("CARGO_PKG_VERSION").to_string())
            .unwrap_or_default(),
    }
}

#[tauri::command]
fn discover_project(project_root: String) -> Result<ProjectSnapshot, String> {
    let root = resolve_project_root(project_root)?;
    let project_settings = root.join("ProjectSettings");
    let unity_project = project_settings.exists() && root.join("Assets").exists();
    let config_path = find_project_config(&root).ok_or_else(|| {
        format!(
            "没有找到 ProjectSettings/*ConfigSheetForge*.json：{}",
            root.display()
        )
    })?;

    let config = read_json(&config_path).unwrap_or(Value::Null);
    let git_branch = read_git_branch_from_files(&root).unwrap_or_else(|| "unknown".to_string());
    let feishu_profile = find_string_deep(&config, &["feishuProfile", "profile", "currentProfile"])
        .filter(|v| !v.is_empty())
        .unwrap_or_else(|| git_branch.clone());
    let github_repository = find_string_deep(&config, &["githubRepository", "repository"])
        .or_else(|| github_repo_from_git_config(&root))
        .unwrap_or_default();

    Ok(ProjectSnapshot {
        project_root: root.to_string_lossy().to_string(),
        unity_project,
        project_config_path: config_path.to_string_lossy().to_string(),
        unity_package_version: read_unity_package_version(&root),
        project_id: find_string_deep(&config, &["projectId", "id", "name"]).unwrap_or_default(),
        git_branch_url: if github_repository.is_empty() || git_branch == "unknown" {
            String::new()
        } else {
            format!(
                "https://github.com/{}/tree/{}",
                github_repository, git_branch
            )
        },
        git_branch,
        feishu_profile,
        registry_base_token: find_string_deep(&config, &["registryBaseToken", "baseToken"])
            .unwrap_or_default(),
        registry_base_url: find_string_deep(&config, &["registryBaseUrl", "baseUrl"])
            .unwrap_or_default(),
        github_repository,
        pr_url: String::new(),
        pr_number: String::new(),
    })
}

fn doctor_tools_snapshot(project_root: String) -> Vec<ToolCheck> {
    let root = resolve_project_root(project_root)
        .unwrap_or_else(|_| env::current_dir().unwrap_or_else(|_| PathBuf::from(".")));
    let config = read_desktop_project_config(&root);
    let mut checks = Vec::new();

    checks.push(tool_check_resolved(
        &root,
        "Config Sheet Forge CLI",
        resolve_config_sheet_forge_cli(&root, &config),
        &["help"],
        "Desktop 会优先调用 release zip 内置 sidecar CLI；找不到时才尝试环境变量、源码 checkout 和 PATH。",
        "重新安装 Desktop",
        "reinstall_desktop",
        "",
    ));

    checks.push(tool_check_resolved(
        &root,
        "git",
        resolve_path_tool("git", &[".exe", ".cmd", ".bat", ""]),
        &["--version"],
        "git 必需，用于识别当前分支和 merge-base。",
        "安装 Git",
        "install_git",
        "https://git-scm.com/download/win",
    ));

    checks.push(lark_tool_check(&root, &config));

    checks.push(github_tool_check(&root));

    checks.push(ToolCheck {
        name: "飞书直连".to_string(),
        status: "ok".to_string(),
        summary: "Desktop 子进程会设置 LARK_CLI_NO_PROXY=1".to_string(),
        detail: "飞书导出和 Base 读取默认不走代理；如果公司网络需要特殊代理，请在程序视图里检查本机环境。".to_string(),
        installed: true,
        executable_path: String::new(),
        source: "desktop-env".to_string(),
        authenticated: true,
        account_label: String::new(),
        scopes_ok: true,
        next_action: "none".to_string(),
        next_action_label: String::new(),
        secondary_actions: Vec::new(),
        bot_configured: true,
        bot_label: String::new(),
        user_authenticated: false,
        user_label: String::new(),
        action: String::new(),
        action_label: String::new(),
        url: String::new(),
    });

    checks
}

#[tauri::command]
fn start_tool_check_task(project_root: String) -> Result<TaskSnapshot, String> {
    let root = resolve_project_root(project_root)?;
    let (task_id, task_state, initial) = create_task_state(
        "tool-check".to_string(),
        "Desktop 内部任务：检查工具安装、授权和账号状态".to_string(),
        String::new(),
        "desktop-internal".to_string(),
        Vec::new(),
        String::new(),
        String::new(),
    );

    thread::spawn({
        let task_state = task_state.clone();
        move || {
            update_task(&task_state, |state| {
                state.snapshot.phase = "检查本机工具".to_string();
                state.snapshot.message =
                    "正在检查 Config Sheet Forge CLI、git、gh、lark-cli 和飞书直连。".to_string();
            });

            let checks = doctor_tools_snapshot(root.to_string_lossy().to_string());
            let result_json =
                serde_json::to_string_pretty(&checks).unwrap_or_else(|_| "[]".to_string());
            update_task(&task_state, |state| {
                if state.cancel_requested {
                    state.snapshot.status = "canceled".to_string();
                    state.snapshot.message = "已取消工具检查。".to_string();
                    state.snapshot.exit_code = -1;
                } else {
                    state.snapshot.status = "succeeded".to_string();
                    state.snapshot.phase = "工具检查完成".to_string();
                    state.snapshot.message = "工具安装、授权和账号状态已刷新。".to_string();
                    state.snapshot.exit_code = 0;
                    state.snapshot.result_json = result_json;
                }
                state.finished_at = Some(Instant::now());
            });
        }
    });

    remember_task(task_id, task_state);
    Ok(initial)
}

#[tauri::command]
fn start_task(
    project_root: String,
    operation: String,
    args: Vec<String>,
) -> Result<TaskSnapshot, String> {
    let root = resolve_project_root(project_root)?;
    let spec = build_cli_task_spec(root, operation, args)?;
    start_process_task(spec)
}

#[tauri::command]
fn write_bridge_command(
    bridge_session_dir: String,
    operation: String,
    payload_json: String,
) -> Result<String, String> {
    let session = PathBuf::from(bridge_session_dir.trim());
    if session.as_os_str().is_empty() {
        return Err("Desktop 不是从 Unity bridge 启动，不能直接发送 Unity 命令。".to_string());
    }

    let commands = session.join("commands");
    fs::create_dir_all(&commands).map_err(|e| format!("无法创建 Unity bridge 命令目录：{}", e))?;
    let timestamp = chrono_like_timestamp();
    let path = commands.join(format!(
        "{}-{}.json",
        timestamp,
        sanitize_file_name(&operation)
    ));
    let payload: Value = serde_json::from_str(&payload_json).unwrap_or(Value::Null);
    let document = serde_json::json!({
        "operation": operation,
        "createdAt": timestamp,
        "payload": payload
    });
    fs::write(
        &path,
        serde_json::to_string_pretty(&document).unwrap_or_else(|_| "{}".to_string()),
    )
    .map_err(|e| format!("写入 Unity bridge 命令失败：{}", e))?;
    Ok(path.to_string_lossy().to_string())
}

#[tauri::command]
fn start_setup_task(
    project_root: String,
    action: String,
    app_id: String,
    secret_value: String,
) -> Result<TaskSnapshot, String> {
    let root = resolve_project_root(project_root)?;
    let config = read_desktop_project_config(&root);
    let spec = build_setup_task_spec(root, &config, action, app_id, secret_value)?;
    start_process_task(spec)
}

#[tauri::command]
fn get_task(task_id: String) -> Result<TaskSnapshot, String> {
    let state = find_task(&task_id)?;
    Ok(snapshot_task(&state))
}

#[tauri::command]
fn cancel_task(task_id: String) -> Result<TaskSnapshot, String> {
    let state = find_task(&task_id)?;
    let pid = {
        let mut guard = state.lock().map_err(|_| "任务状态锁已损坏。".to_string())?;
        guard.cancel_requested = true;
        guard.snapshot.status = "canceling".to_string();
        guard.snapshot.phase = "正在取消".to_string();
        guard.snapshot.message = "正在终止后台进程树；取消后不会写本地 cache 或飞书。".to_string();
        guard.snapshot.pid
    };

    if pid > 0 {
        kill_process_tree(pid);
    }

    Ok(snapshot_task(&state))
}

#[tauri::command]
fn read_desktop_result(project_root: String, name: String) -> Result<CliRunResult, String> {
    let root = resolve_project_root(project_root)?;
    let safe_name: String = name
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || *c == '-' || *c == '_')
        .collect();
    if safe_name.is_empty() {
        return Err("result 名称不合法。".to_string());
    }

    let result_path = root
        .join("Temp")
        .join("ConfigSheetForge")
        .join("desktop")
        .join(format!("{}.result.json", safe_name));
    let result_json = fs::read_to_string(&result_path).unwrap_or_default();
    Ok(CliRunResult {
        command_line: String::new(),
        exit_code: if result_json.is_empty() { -1 } else { 0 },
        stdout: String::new(),
        stderr: String::new(),
        executable_path: String::new(),
        source: "desktop-result-cache".to_string(),
        attempted_paths: Vec::new(),
        result_path: normalize_path_string_for_cli(&result_path.to_string_lossy()),
        result_json,
    })
}

#[tauri::command]
fn open_external_url(url: String) -> Result<(), String> {
    let trimmed = url.trim();
    if !(trimmed.starts_with("https://") || trimmed.starts_with("http://")) {
        return Err("只能打开 http/https 链接。".to_string());
    }

    #[cfg(target_os = "windows")]
    let mut command = {
        let mut cmd = Command::new("cmd");
        cmd.args(["/C", "start", "", trimmed]);
        cmd
    };

    #[cfg(target_os = "macos")]
    let mut command = {
        let mut cmd = Command::new("open");
        cmd.arg(trimmed);
        cmd
    };

    #[cfg(all(unix, not(target_os = "macos")))]
    let mut command = {
        let mut cmd = Command::new("xdg-open");
        cmd.arg(trimmed);
        cmd
    };

    command
        .spawn()
        .map_err(|e| format!("无法打开链接：{}", e))?;
    Ok(())
}

fn main() {
    let args: Vec<String> = env::args().collect();
    if args.iter().any(|arg| arg == "--smoke-release") {
        std::process::exit(run_release_smoke(
            args.iter().any(|arg| arg == "--expect-sidecar"),
        ));
    }

    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            startup_project_root,
            startup_context,
            discover_project,
            start_tool_check_task,
            start_task,
            start_setup_task,
            get_task,
            cancel_task,
            read_desktop_result,
            write_bridge_command,
            open_external_url
        ])
        .run(tauri::generate_context!())
        .expect("error while running Config Sheet Forge desktop");
}

fn run_release_smoke(expect_sidecar: bool) -> i32 {
    let exe_text = env::current_exe()
        .ok()
        .and_then(|path| fs::read(path).ok())
        .map(|bytes| String::from_utf8_lossy(&bytes).into_owned())
        .unwrap_or_default();
    let dev_server_url_embedded = contains_desktop_dev_url(&exe_text);
    let frontend_markers_embedded = exe_text.contains("index.html")
        || exe_text.contains("tauri://localhost")
        || exe_text.contains("配表 Source of Truth 工作台");
    let sidecar = resolve_sidecar_cli();
    let sidecar_cli_found = sidecar.is_some();
    let sidecar_cli_smoke_ok = sidecar
        .as_ref()
        .and_then(|tool| {
            run_capture_resolved(
                &env::current_dir().unwrap_or_else(|_| PathBuf::from(".")),
                tool,
                &["help"],
                None,
                None,
            )
            .ok()
        })
        .map(|result| result.exit_code == 0)
        .unwrap_or(false);
    let report = ReleaseSmokeReport {
        version: env!("CARGO_PKG_VERSION"),
        release_build: !cfg!(debug_assertions),
        dev_server_url_embedded,
        frontend_markers_embedded,
        sidecar_cli_found,
        sidecar_cli_smoke_ok,
    };
    println!(
        "{}",
        serde_json::to_string(&report).unwrap_or_else(|_| "{}".to_string())
    );

    if report.release_build
        && report.frontend_markers_embedded
        && !report.dev_server_url_embedded
        && (!expect_sidecar || (report.sidecar_cli_found && report.sidecar_cli_smoke_ok))
    {
        0
    } else {
        2
    }
}

fn resolve_config_sheet_forge_cli(root: &Path, config: &Value) -> Option<ResolvedTool> {
    if let Some(sidecar) = resolve_sidecar_cli() {
        return Some(sidecar);
    }

    if let Ok(value) = env::var("CONFIG_SHEET_FORGE_CLI") {
        if let Some(tool) = resolve_explicit_tool(&value, "env:CONFIG_SHEET_FORGE_CLI") {
            return Some(tool);
        }
    }

    let source_root = env::var("CONFIG_SHEET_FORGE_ROOT").unwrap_or_default();
    let source_relative = find_string_deep(
        config,
        &["sourceCliProjectRelativePath", "cliProjectRelativePath"],
    )
    .unwrap_or_else(|| "src/cli/ConfigSheetForge.Cli".to_string());
    if !source_root.trim().is_empty() {
        let project = Path::new(source_root.trim()).join(source_relative);
        if project.exists() {
            return Some(ResolvedTool {
                program: "dotnet".to_string(),
                prefix_args: vec![
                    "run".to_string(),
                    "--project".to_string(),
                    project.to_string_lossy().to_string(),
                    "--".to_string(),
                ],
                display_path: format!("dotnet run --project {}", project.display()),
                source: "CONFIG_SHEET_FORGE_ROOT source checkout".to_string(),
                attempted_paths: vec![project.to_string_lossy().to_string()],
            });
        }
    }

    let mut attempted = Vec::new();
    let mut path_tool = resolve_path_tool_with_attempts(
        "config-sheet-forge",
        &[".exe", ".cmd", ".bat", ""],
        &mut attempted,
    );
    if let Some(tool) = path_tool.as_mut() {
        tool.attempted_paths.extend(attempted);
        return path_tool;
    }

    let source_project = root.join("src/cli/ConfigSheetForge.Cli");
    if source_project.exists() {
        return Some(ResolvedTool {
            program: "dotnet".to_string(),
            prefix_args: vec![
                "run".to_string(),
                "--project".to_string(),
                source_project.to_string_lossy().to_string(),
                "--".to_string(),
            ],
            display_path: format!("dotnet run --project {}", source_project.display()),
            source: "project-local source checkout".to_string(),
            attempted_paths: vec![source_project.to_string_lossy().to_string()],
        });
    }

    None
}

fn resolve_sidecar_cli() -> Option<ResolvedTool> {
    let exe_dir = env::current_exe()
        .ok()
        .and_then(|path| path.parent().map(|p| p.to_path_buf()))?;
    let candidates = [
        exe_dir.join("cli").join("config-sheet-forge.exe"),
        exe_dir.join("cli").join("config-sheet-forge"),
        exe_dir.join("config-sheet-forge.exe"),
        exe_dir.join("config-sheet-forge"),
    ];
    for candidate in candidates {
        if candidate.exists() {
            return Some(tool_from_path(candidate, "Desktop sidecar CLI"));
        }
    }

    None
}

fn resolve_lark_cli(root: &Path, config: &Value) -> Option<ResolvedTool> {
    let local_config =
        read_json(&root.join(".config-sheet-forge").join("config.json")).unwrap_or(Value::Null);
    let mut attempted = Vec::new();
    for (source, value) in [
        (
            "env:CONFIG_SHEET_FORGE_LARK_CLI",
            env::var("CONFIG_SHEET_FORGE_LARK_CLI").unwrap_or_default(),
        ),
        (
            "env:LARK_CLI_PATH",
            env::var("LARK_CLI_PATH").unwrap_or_default(),
        ),
        (
            "project:toolkit.larkCliPath",
            find_string_deep(config, &["larkCliPath", "larkCliExecutable"]).unwrap_or_default(),
        ),
        (
            "local:.config-sheet-forge/config.json",
            find_string_deep(&local_config, &["larkCliPath", "larkCliExecutable"])
                .unwrap_or_default(),
        ),
    ] {
        if let Some(tool) = resolve_explicit_tool_with_attempts(&value, source, &mut attempted) {
            return Some(tool);
        }
    }

    if cfg!(windows) {
        if let Ok(appdata) = env::var("APPDATA") {
            for name in ["lark-cli.ps1", "lark-cli.cmd"] {
                let candidate = Path::new(&appdata).join("npm").join(name);
                attempted.push(candidate.to_string_lossy().to_string());
                if candidate.exists() {
                    return Some(tool_from_path(candidate, "%APPDATA%/npm"));
                }
            }
        }
    }

    resolve_path_tool_with_attempts(
        "lark-cli",
        &[".ps1", ".cmd", ".exe", ".bat", ""],
        &mut attempted,
    )
}

fn resolve_path_tool(name: &str, extensions: &[&str]) -> Option<ResolvedTool> {
    let mut attempted = Vec::new();
    resolve_path_tool_with_attempts(name, extensions, &mut attempted)
}

fn resolve_path_tool_with_attempts(
    name: &str,
    extensions: &[&str],
    attempted: &mut Vec<String>,
) -> Option<ResolvedTool> {
    let path_var = env::var_os("PATH").unwrap_or_default();
    let mut search_paths: Vec<PathBuf> = env::split_paths(&path_var).collect();
    if cfg!(windows) {
        if let Ok(appdata) = env::var("APPDATA") {
            let npm = Path::new(&appdata).join("npm");
            if npm.exists() && !search_paths.iter().any(|path| path == &npm) {
                search_paths.insert(0, npm);
            }
        }
    }

    for dir in search_paths {
        for extension in extensions {
            let candidate = dir.join(format!("{}{}", name, extension));
            attempted.push(candidate.to_string_lossy().to_string());
            if candidate.exists() {
                return Some(tool_from_path(candidate, "PATH"));
            }
        }
    }

    None
}

fn resolve_explicit_tool(value: &str, source: &str) -> Option<ResolvedTool> {
    let mut attempted = Vec::new();
    resolve_explicit_tool_with_attempts(value, source, &mut attempted)
}

fn resolve_explicit_tool_with_attempts(
    value: &str,
    source: &str,
    attempted: &mut Vec<String>,
) -> Option<ResolvedTool> {
    let trimmed = value.trim().trim_matches('"');
    if trimmed.is_empty() {
        return None;
    }

    let looks_like_path = trimmed.contains('\\')
        || trimmed.contains('/')
        || trimmed.ends_with(".exe")
        || trimmed.ends_with(".cmd")
        || trimmed.ends_with(".bat")
        || trimmed.ends_with(".ps1");
    if !looks_like_path {
        return None;
    }

    let path = PathBuf::from(trimmed);
    attempted.push(path.to_string_lossy().to_string());
    if path.exists() {
        return Some(tool_from_path(path, source));
    }

    None
}

fn tool_from_path(path: PathBuf, source: &str) -> ResolvedTool {
    let extension = path
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or("")
        .to_lowercase();
    let display_path = path.to_string_lossy().to_string();
    if cfg!(windows) && extension == "ps1" {
        return ResolvedTool {
            program: "powershell.exe".to_string(),
            prefix_args: vec![
                "-NoProfile".to_string(),
                "-ExecutionPolicy".to_string(),
                "Bypass".to_string(),
                "-File".to_string(),
                display_path.clone(),
            ],
            display_path,
            source: source.to_string(),
            attempted_paths: Vec::new(),
        };
    }

    if cfg!(windows) && (extension == "cmd" || extension == "bat") {
        return ResolvedTool {
            program: "cmd.exe".to_string(),
            prefix_args: vec!["/C".to_string(), display_path.clone()],
            display_path,
            source: source.to_string(),
            attempted_paths: Vec::new(),
        };
    }

    ResolvedTool {
        program: display_path.clone(),
        prefix_args: Vec::new(),
        display_path,
        source: source.to_string(),
        attempted_paths: Vec::new(),
    }
}

fn tool_check_resolved(
    root: &Path,
    name: &str,
    resolved: Option<ResolvedTool>,
    args: &[&str],
    help: &str,
    action_label: &str,
    action: &str,
    url: &str,
) -> ToolCheck {
    match resolved {
        Some(tool) => match run_capture_resolved(root, &tool, args, None, None) {
            Ok(result) if result.exit_code == 0 => ToolCheck {
                name: name.to_string(),
                status: "ok".to_string(),
                summary: first_line(&result.stdout).unwrap_or_else(|| "可用".to_string()),
                detail: format!(
                    "{} 来源：{}。路径：{}",
                    help, tool.source, tool.display_path
                ),
                installed: true,
                executable_path: tool.display_path,
                source: tool.source,
                authenticated: true,
                account_label: String::new(),
                scopes_ok: true,
                next_action: "none".to_string(),
                next_action_label: String::new(),
                secondary_actions: Vec::new(),
                bot_configured: true,
                bot_label: String::new(),
                user_authenticated: false,
                user_label: String::new(),
                action: String::new(),
                action_label: String::new(),
                url: url.to_string(),
            },
            Ok(result) => ToolCheck {
                name: name.to_string(),
                status: "warning".to_string(),
                summary: first_line(&result.stderr)
                    .or_else(|| first_line(&result.stdout))
                    .unwrap_or_else(|| "命令返回非 0。".to_string()),
                detail: format!(
                    "{} 已识别路径：{}。请按提示修复授权或配置。",
                    help, tool.display_path
                ),
                installed: true,
                executable_path: tool.display_path,
                source: tool.source,
                authenticated: false,
                account_label: String::new(),
                scopes_ok: false,
                next_action: action.to_string(),
                next_action_label: action_label.to_string(),
                secondary_actions: docs_action(url),
                bot_configured: false,
                bot_label: String::new(),
                user_authenticated: false,
                user_label: String::new(),
                action: action.to_string(),
                action_label: action_label.to_string(),
                url: url.to_string(),
            },
            Err(err) => ToolCheck {
                name: name.to_string(),
                status: "warning".to_string(),
                summary: "已找到但无法运行".to_string(),
                detail: format!("{} 路径：{}。原因：{}", help, tool.display_path, err),
                installed: true,
                executable_path: tool.display_path,
                source: tool.source,
                authenticated: false,
                account_label: String::new(),
                scopes_ok: false,
                next_action: action.to_string(),
                next_action_label: action_label.to_string(),
                secondary_actions: docs_action(url),
                bot_configured: false,
                bot_label: String::new(),
                user_authenticated: false,
                user_label: String::new(),
                action: action.to_string(),
                action_label: action_label.to_string(),
                url: url.to_string(),
            },
        },
        None => ToolCheck {
            name: name.to_string(),
            status: "needsInstall".to_string(),
            summary: format!("未找到 {}", name),
            detail: help.to_string(),
            installed: false,
            executable_path: String::new(),
            source: "not-found".to_string(),
            authenticated: false,
            account_label: String::new(),
            scopes_ok: false,
            next_action: action.to_string(),
            next_action_label: action_label.to_string(),
            secondary_actions: docs_action(url),
            bot_configured: false,
            bot_label: String::new(),
            user_authenticated: false,
            user_label: String::new(),
            action: action.to_string(),
            action_label: action_label.to_string(),
            url: url.to_string(),
        },
    }
}

fn github_tool_check(root: &Path) -> ToolCheck {
    let help = "GitHub CLI 推荐安装，用于自动识别当前 PR 和目标分支；缺失时仍可手动选择目标分支。";
    let Some(tool) = resolve_path_tool("gh", &[".exe", ".cmd", ".bat", ""]) else {
        return missing_tool_check(
            "gh",
            help,
            "install_gh",
            "安装 GitHub CLI",
            "https://cli.github.com/",
        );
    };

    let auth = run_capture_resolved(root, &tool, &["auth", "status"], None, None);
    let Ok(auth_result) = auth else {
        return installed_tool_issue(
            "gh",
            &tool,
            "error",
            "gh 已安装但无法运行 auth status。",
            help,
            "gh_auth",
            "GitHub 授权",
            "https://cli.github.com/",
        );
    };

    if auth_result.exit_code != 0 {
        return ToolCheck {
            name: "gh".to_string(),
            status: "needsAuth".to_string(),
            summary: "gh 已安装，但还没有完成 GitHub 授权。".to_string(),
            detail: "PR base 自动识别需要 gh 登录。点击 GitHub 授权后按浏览器提示完成。"
                .to_string(),
            installed: true,
            executable_path: tool.display_path.clone(),
            source: tool.source.clone(),
            authenticated: false,
            account_label: String::new(),
            scopes_ok: false,
            next_action: "gh_auth".to_string(),
            next_action_label: "GitHub 授权".to_string(),
            secondary_actions: docs_action("https://cli.github.com/"),
            bot_configured: false,
            bot_label: String::new(),
            user_authenticated: false,
            user_label: String::new(),
            action: "gh_auth".to_string(),
            action_label: "GitHub 授权".to_string(),
            url: "https://cli.github.com/".to_string(),
        };
    }

    let user =
        github_user_label(root, &tool, &auth_result).unwrap_or_else(|| "GitHub 用户".to_string());
    let pr = run_capture_resolved(
        root,
        &tool,
        &["pr", "view", "--json", "number,url"],
        None,
        None,
    )
    .ok();
    let pr_found = pr.as_ref().map(|r| r.exit_code == 0).unwrap_or(false);
    let (summary, detail, next_action, next_label) = if pr_found {
        (
            "已授权，并能识别当前分支 PR。".to_string(),
            format!("已授权为 {}。PR 自动识别可用。", user),
            "none".to_string(),
            String::new(),
        )
    } else {
        ("已授权，但未找到当前分支 PR。".to_string(), format!("已授权为 {}。如果这个分支应该有 PR，请刷新 PR；没有 PR 时可在合并场景手动选择目标分支。", user), "refresh_pr".to_string(), "刷新 PR".to_string())
    };

    ToolCheck {
        name: "gh".to_string(),
        status: "ok".to_string(),
        summary,
        detail,
        installed: true,
        executable_path: tool.display_path,
        source: tool.source,
        authenticated: true,
        account_label: user,
        scopes_ok: true,
        next_action,
        next_action_label: next_label,
        secondary_actions: vec![
            action_item("gh_auth", "重新授权 / 切换账号", "secondary"),
            action_item("gh_logout", "取消授权", "danger"),
            action_item("refresh_pr", "刷新 PR", "secondary"),
        ],
        bot_configured: false,
        bot_label: String::new(),
        user_authenticated: true,
        user_label: String::new(),
        action: String::new(),
        action_label: String::new(),
        url: "https://cli.github.com/".to_string(),
    }
}

fn lark_tool_check(root: &Path, config: &Value) -> ToolCheck {
    let help = "lark-cli 必需，用 bot/user 身份读取飞书 Base 和在线 Sheet。Desktop 子进程默认设置 LARK_CLI_NO_PROXY=1，飞书直连。";
    let Some(tool) = resolve_lark_cli(root, config) else {
        return missing_tool_check(
            "lark-cli",
            help,
            "install_lark_cli",
            "安装 lark-cli",
            "https://github.com/larksuite/cli",
        );
    };

    let doctor = run_capture_resolved(root, &tool, &["doctor"], None, None);
    let Ok(result) = doctor else {
        return installed_tool_issue(
            "lark-cli",
            &tool,
            "error",
            "lark-cli 已安装但 doctor 无法启动。",
            help,
            "lark_doctor",
            "重新 doctor",
            "https://github.com/larksuite/cli",
        );
    };

    let combined = (result.stdout.clone() + "\n" + &result.stderr)
        .trim()
        .to_string();
    let bot_label = find_lark_bot_label(&combined).unwrap_or_default();
    let user_label = find_lark_user_label(&combined).unwrap_or_default();
    let bot_configured = !bot_label.is_empty()
        || (result.exit_code == 0
            && !contains_any(
                &combined,
                &["not configured", "config init", "app secret", "app id"],
            ));
    let user_authenticated = !user_label.is_empty();
    let scopes_ok = result.exit_code == 0
        && !contains_any(
            &combined,
            &[
                "missing_scope",
                "missing scope",
                "scope",
                "权限不足",
                "insufficient",
            ],
        );

    if result.exit_code != 0 {
        let (status, summary, action, label) = if contains_any(
            &combined,
            &["missing_scope", "missing scope", "insufficient", "权限不足"],
        ) {
            (
                "needsScope",
                "lark-cli 可用，但权限/scope 不足。",
                "lark_doctor",
                "查看缺失权限 / 重新 doctor",
            )
        } else if contains_any(
            &combined,
            &[
                "not configured",
                "config init",
                "app secret",
                "app id",
                "未配置",
            ],
        ) {
            (
                "needsAuth",
                "lark-cli 已安装，但 bot 还没有配置。",
                "configure_lark_bot",
                "配置飞书 bot",
            )
        } else {
            (
                "error",
                "lark-cli doctor 未通过。",
                "lark_doctor",
                "重新 doctor",
            )
        };

        return ToolCheck {
            name: "lark-cli".to_string(),
            status: status.to_string(),
            summary: summary.to_string(),
            detail: humanize_lark_doctor_detail(&combined),
            installed: true,
            executable_path: tool.display_path,
            source: tool.source,
            authenticated: false,
            account_label: first_non_empty_rust(&[bot_label.clone(), user_label.clone()]),
            scopes_ok,
            next_action: action.to_string(),
            next_action_label: label.to_string(),
            secondary_actions: vec![
                action_item("lark_doctor", "重新 doctor", "secondary"),
                action_item("lark_auth_user", "登录飞书用户身份", "secondary"),
            ],
            bot_configured,
            bot_label,
            user_authenticated,
            user_label,
            action: action.to_string(),
            action_label: label.to_string(),
            url: "https://github.com/larksuite/cli".to_string(),
        };
    }

    let account_label = if bot_label.is_empty() && user_label.is_empty() {
        "bot/user 状态已通过 doctor".to_string()
    } else {
        [bot_label.clone(), user_label.clone()]
            .into_iter()
            .filter(|v| !v.is_empty())
            .collect::<Vec<_>>()
            .join(" / ")
    };

    let mut secondary = vec![action_item("lark_doctor", "重新 doctor", "secondary")];
    if user_authenticated {
        secondary.push(action_item(
            "lark_auth_user",
            "重新登录 / 切换飞书用户",
            "secondary",
        ));
    } else {
        secondary.push(action_item(
            "lark_auth_user",
            "登录飞书用户身份",
            "secondary",
        ));
    }
    secondary.push(action_item(
        "configure_lark_bot",
        "重新配置 bot",
        "secondary",
    ));

    ToolCheck {
        name: "lark-cli".to_string(),
        status: "ok".to_string(),
        summary: "lark-cli 可用，飞书直连和 bot doctor 已通过。".to_string(),
        detail: if user_authenticated {
            "交互式预览可经确认使用用户身份；PR hard gate 仍 strict bot，不能靠 user 登录通过 CI。"
                .to_string()
        } else {
            "bot 可用。若要在交互式预览中允许 user fallback，可在身份策略里开启后再登录飞书用户身份。".to_string()
        },
        installed: true,
        executable_path: tool.display_path,
        source: tool.source,
        authenticated: true,
        account_label,
        scopes_ok,
        next_action: "none".to_string(),
        next_action_label: String::new(),
        secondary_actions: secondary,
        bot_configured,
        bot_label,
        user_authenticated,
        user_label,
        action: String::new(),
        action_label: String::new(),
        url: "https://github.com/larksuite/cli".to_string(),
    }
}

fn action_item(action: &str, label: &str, kind: &str) -> ToolAction {
    ToolAction {
        action: action.to_string(),
        label: label.to_string(),
        kind: kind.to_string(),
    }
}

fn docs_action(_url: &str) -> Vec<ToolAction> {
    // Project docs are rendered as normal links by the frontend. Keeping this
    // empty avoids showing a button that needs hidden payload data.
    Vec::new()
}

fn missing_tool_check(
    name: &str,
    help: &str,
    action: &str,
    action_label: &str,
    url: &str,
) -> ToolCheck {
    ToolCheck {
        name: name.to_string(),
        status: "needsInstall".to_string(),
        summary: format!("未安装 {}", name),
        detail: help.to_string(),
        installed: false,
        executable_path: String::new(),
        source: "not-found".to_string(),
        authenticated: false,
        account_label: String::new(),
        scopes_ok: false,
        next_action: action.to_string(),
        next_action_label: action_label.to_string(),
        secondary_actions: docs_action(url),
        bot_configured: false,
        bot_label: String::new(),
        user_authenticated: false,
        user_label: String::new(),
        action: action.to_string(),
        action_label: action_label.to_string(),
        url: url.to_string(),
    }
}

fn installed_tool_issue(
    name: &str,
    tool: &ResolvedTool,
    status: &str,
    summary: &str,
    detail: &str,
    action: &str,
    action_label: &str,
    url: &str,
) -> ToolCheck {
    ToolCheck {
        name: name.to_string(),
        status: status.to_string(),
        summary: summary.to_string(),
        detail: detail.to_string(),
        installed: true,
        executable_path: tool.display_path.clone(),
        source: tool.source.clone(),
        authenticated: false,
        account_label: String::new(),
        scopes_ok: false,
        next_action: action.to_string(),
        next_action_label: action_label.to_string(),
        secondary_actions: docs_action(url),
        bot_configured: false,
        bot_label: String::new(),
        user_authenticated: false,
        user_label: String::new(),
        action: action.to_string(),
        action_label: action_label.to_string(),
        url: url.to_string(),
    }
}

fn github_user_label(
    root: &Path,
    tool: &ResolvedTool,
    auth_result: &CliRunResult,
) -> Option<String> {
    run_capture_resolved(root, tool, &["api", "user", "--jq", ".login"], None, None)
        .ok()
        .filter(|result| result.exit_code == 0)
        .and_then(|result| first_line(&result.stdout))
        .filter(|value| !value.is_empty())
        .or_else(|| {
            parse_github_user_from_text(&(auth_result.stdout.clone() + "\n" + &auth_result.stderr))
        })
}

fn parse_github_user_from_text(text: &str) -> Option<String> {
    for line in text.lines().map(|line| line.trim()) {
        if let Some(after) = line.split_once("account ") {
            let candidate = after
                .1
                .split_whitespace()
                .next()
                .unwrap_or("")
                .trim_matches(|ch: char| ch == ':' || ch == ',' || ch == '.' || ch == '"');
            if !candidate.is_empty() {
                return Some(candidate.to_string());
            }
        }

        if line.starts_with("- Logged in to ") && line.contains(" as ") {
            if let Some(after) = line.split(" as ").nth(1) {
                let candidate = after
                    .split_whitespace()
                    .next()
                    .unwrap_or("")
                    .trim_matches(|ch: char| ch == ':' || ch == ',' || ch == '.' || ch == '"');
                if !candidate.is_empty() {
                    return Some(candidate.to_string());
                }
            }
        }
    }

    None
}

fn find_lark_bot_label(text: &str) -> Option<String> {
    let value = parse_first_json_value(text);
    for key in [
        "appId",
        "app_id",
        "appID",
        "clientId",
        "client_id",
        "botAppId",
    ] {
        if let Some(found) = value
            .as_ref()
            .and_then(|json| find_string_deep(json, &[key]))
        {
            if !found.trim().is_empty() {
                return Some(format!("bot {}", redact_identifier(&found)));
            }
        }
    }

    for word in text.split(|ch: char| ch.is_whitespace() || ch == ',' || ch == '"' || ch == '\'') {
        let trimmed =
            word.trim_matches(|ch: char| ch == ':' || ch == ';' || ch == ')' || ch == '(');
        if trimmed.starts_with("cli_") || trimmed.starts_with("cli-") {
            return Some(format!("bot {}", redact_identifier(trimmed)));
        }
    }

    None
}

fn find_lark_user_label(text: &str) -> Option<String> {
    let value = parse_first_json_value(text);
    for key in [
        "userName",
        "username",
        "name",
        "displayName",
        "userOpenId",
        "user_open_id",
        "openId",
        "open_id",
        "userId",
        "user_id",
    ] {
        if let Some(found) = value
            .as_ref()
            .and_then(|json| find_string_deep(json, &[key]))
        {
            if !found.trim().is_empty() {
                if found.starts_with("ou_") || found.starts_with("cli_") {
                    return Some(format!("用户 {}", redact_identifier(&found)));
                }
                return Some(found);
            }
        }
    }

    for line in text.lines().map(|line| line.trim()) {
        let lower = line.to_lowercase();
        if (lower.contains("user") || line.contains("用户"))
            && (line.contains("ou_") || line.contains('@'))
        {
            return Some(shorten_line(line, 64));
        }
    }

    None
}

fn contains_any(text: &str, needles: &[&str]) -> bool {
    let lower = text.to_lowercase();
    needles
        .iter()
        .any(|needle| lower.contains(&needle.to_lowercase()))
}

fn humanize_lark_doctor_detail(text: &str) -> String {
    let trimmed = text.trim();
    if trimmed.is_empty() {
        return "lark-cli 没有返回诊断详情，请在 Debug 查看完整日志。".to_string();
    }

    if parse_first_json_value(trimmed).is_some()
        || trimmed.starts_with('{')
        || trimmed.starts_with('[')
    {
        if contains_any(trimmed, &["missing_scope", "missing scope", "scope"]) {
            return "doctor 返回结构化诊断：飞书 scope 或权限不足，请查看缺失权限后重新授权。"
                .to_string();
        }
        if contains_any(
            trimmed,
            &[
                "not configured",
                "config init",
                "app secret",
                "app id",
                "未配置",
            ],
        ) {
            return "doctor 返回结构化诊断：飞书 bot 尚未配置，请配置 App ID 和 App Secret。"
                .to_string();
        }
        return "doctor 返回结构化诊断，普通视图已折叠原始 JSON；需要完整内容请打开 Debug。"
            .to_string();
    }

    shorten_line(first_line(trimmed).as_deref().unwrap_or(trimmed), 160)
}

fn parse_first_json_value(text: &str) -> Option<Value> {
    if let Ok(value) = serde_json::from_str::<Value>(text.trim()) {
        return Some(value);
    }

    let chars: Vec<char> = text.chars().collect();
    for (index, ch) in chars.iter().enumerate() {
        if *ch != '{' && *ch != '[' {
            continue;
        }

        let open = *ch;
        let close = if open == '{' { '}' } else { ']' };
        let mut depth = 0usize;
        let mut in_string = false;
        let mut escape = false;
        for (end, current) in chars.iter().enumerate().skip(index) {
            if escape {
                escape = false;
                continue;
            }

            if *current == '\\' && in_string {
                escape = true;
                continue;
            }

            if *current == '"' {
                in_string = !in_string;
                continue;
            }

            if in_string {
                continue;
            }

            if *current == open {
                depth += 1;
            } else if *current == close {
                depth = depth.saturating_sub(1);
                if depth == 0 {
                    let candidate: String = chars[index..=end].iter().collect();
                    if let Ok(value) = serde_json::from_str::<Value>(&candidate) {
                        return Some(value);
                    }
                    break;
                }
            }
        }
    }

    None
}

fn first_non_empty_rust(values: &[String]) -> String {
    values
        .iter()
        .map(|value| value.trim())
        .find(|value| !value.is_empty())
        .unwrap_or("")
        .to_string()
}

fn redact_identifier(value: &str) -> String {
    let trimmed = value.trim();
    let chars: Vec<char> = trimmed.chars().collect();
    if chars.len() <= 10 {
        return trimmed.to_string();
    }

    let start: String = chars.iter().take(6).collect();
    let end: String = chars
        .iter()
        .rev()
        .take(4)
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .collect();
    format!("{}...{}", start, end)
}

fn shorten_line(value: &str, max_chars: usize) -> String {
    let trimmed = value.trim();
    if trimmed.chars().count() <= max_chars {
        return trimmed.to_string();
    }

    let mut output: String = trimmed.chars().take(max_chars.saturating_sub(1)).collect();
    output.push('…');
    output
}

fn build_cli_task_spec(
    root: PathBuf,
    operation: String,
    args: Vec<String>,
) -> Result<TaskSpec, String> {
    let config = read_desktop_project_config(&root);
    let tool = resolve_config_sheet_forge_cli(&root, &config)
        .ok_or_else(|| missing_cli_message(&root, &config))?;
    let lark_cli = resolve_lark_cli(&root, &config);
    let args = normalize_cli_path_args(&root, &args);
    let result_path = find_result_path(
        &root,
        &tool.command_args(&args.iter().map(|s| s.as_str()).collect::<Vec<_>>()),
    )
    .unwrap_or_default();
    let progress_path = find_named_arg_path(&root, &args, "--progress").unwrap_or_default();
    Ok(TaskSpec {
        operation,
        root,
        tool,
        args,
        lark_cli,
        stdin: None,
        result_path,
        progress_path,
    })
}

fn build_setup_task_spec(
    root: PathBuf,
    config: &Value,
    action: String,
    app_id: String,
    secret_value: String,
) -> Result<TaskSpec, String> {
    let (tool, args, stdin) = match action.as_str() {
        "install_git" => (
            resolve_path_tool("winget", &[".exe", ".cmd", ".bat", ""]).ok_or_else(|| {
                "没有找到 winget。请手动安装 Git，或把安装器路径加入 PATH。".to_string()
            })?,
            vec!["install", "--id", "Git.Git", "-e", "--source", "winget"]
                .into_iter()
                .map(|s| s.to_string())
                .collect(),
            None,
        ),
        "install_gh" => (
            resolve_path_tool("winget", &[".exe", ".cmd", ".bat", ""]).ok_or_else(|| {
                "没有找到 winget。请手动安装 GitHub CLI，或把安装器路径加入 PATH。".to_string()
            })?,
            vec!["install", "--id", "GitHub.cli", "-e", "--source", "winget"]
                .into_iter()
                .map(|s| s.to_string())
                .collect(),
            None,
        ),
        "gh_auth" => (
            resolve_path_tool("gh", &[".exe", ".cmd", ".bat", ""])
                .ok_or_else(|| "没有找到 gh。请先点击“安装 GitHub CLI”。".to_string())?,
            vec!["auth", "login"]
                .into_iter()
                .map(|s| s.to_string())
                .collect(),
            None,
        ),
        "gh_logout" => (
            resolve_path_tool("gh", &[".exe", ".cmd", ".bat", ""])
                .ok_or_else(|| "没有找到 gh。请先点击“安装 GitHub CLI”。".to_string())?,
            vec!["auth", "logout", "-h", "github.com"]
                .into_iter()
                .map(|s| s.to_string())
                .collect(),
            None,
        ),
        "install_lark_cli" => (
            resolve_path_tool("npm", &[".cmd", ".exe", ".bat", ""]).ok_or_else(|| {
                "没有找到 npm。请先安装 Node.js LTS，然后重试安装 lark-cli。".to_string()
            })?,
            vec!["install", "-g", "@larksuite/cli"]
                .into_iter()
                .map(|s| s.to_string())
                .collect(),
            None,
        ),
        "lark_auth_user" => (
            resolve_lark_cli(&root, config)
                .ok_or_else(|| "没有找到 lark-cli。请先点击“安装 lark-cli”。".to_string())?,
            vec!["auth", "login", "--recommend"]
                .into_iter()
                .map(|s| s.to_string())
                .collect(),
            None,
        ),
        "lark_doctor" => (
            resolve_lark_cli(&root, config)
                .ok_or_else(|| "没有找到 lark-cli。请先点击“安装 lark-cli”。".to_string())?,
            vec!["doctor"].into_iter().map(|s| s.to_string()).collect(),
            None,
        ),
        "configure_lark_bot" => {
            if app_id.trim().is_empty() || secret_value.is_empty() {
                return Err("请先填写飞书 App ID 和 App Secret。Secret 只会通过 stdin 传给 lark-cli，不写入仓库或日志。".to_string());
            }

            (
                resolve_lark_cli(&root, config)
                    .ok_or_else(|| "没有找到 lark-cli。请先点击“安装 lark-cli”。".to_string())?,
                vec![
                    "config",
                    "init",
                    "--app-id",
                    app_id.trim(),
                    "--app-secret-stdin",
                    "--brand",
                    "feishu",
                ]
                .into_iter()
                .map(|s| s.to_string())
                .collect(),
                Some(format!("{}\n", secret_value)),
            )
        }
        _ => return Err("未知安装/授权动作：".to_string() + &action),
    };

    Ok(TaskSpec {
        operation: action,
        root,
        tool,
        args,
        lark_cli: None,
        stdin,
        result_path: String::new(),
        progress_path: String::new(),
    })
}

fn start_process_task(spec: TaskSpec) -> Result<TaskSnapshot, String> {
    let all_args = spec
        .tool
        .command_args(&spec.args.iter().map(|s| s.as_str()).collect::<Vec<_>>());
    let command_line = build_display_command(&spec.tool, &all_args);
    let (task_id, task_state, initial) = create_task_state(
        spec.operation.clone(),
        command_line,
        spec.tool.display_path.clone(),
        spec.tool.source.clone(),
        spec.tool.attempted_paths.clone(),
        spec.result_path.clone(),
        spec.progress_path.clone(),
    );

    thread::spawn({
        let task_state = task_state.clone();
        move || run_process_task_thread(task_state, spec)
    });

    remember_task(task_id, task_state);
    Ok(initial)
}

fn run_process_task_thread(task_state: Arc<Mutex<TaskState>>, spec: TaskSpec) {
    update_task(&task_state, |state| {
        state.snapshot.phase = "正在启动本机工具".to_string();
        state.snapshot.message = format!("正在执行：{}", state.snapshot.operation);
    });

    if !spec.progress_path.is_empty() {
        write_task_progress_event(
            &spec.progress_path,
            &spec.operation,
            &infer_machine_phase_from_args(&spec.args),
            "",
            0,
            0,
            &initial_progress_message(&spec.operation, &spec.args),
            "info",
        );
        let progress_state = task_state.clone();
        let progress_path = spec.progress_path.clone();
        thread::spawn(move || tail_progress_file(progress_state, progress_path));
    }

    let mut command = Command::new(&spec.tool.program);
    let all_args = spec
        .tool
        .command_args(&spec.args.iter().map(|s| s.as_str()).collect::<Vec<_>>());
    command.args(&all_args).current_dir(&spec.root);
    configure_process_environment(&mut command, spec.lark_cli.as_ref());
    if spec.stdin.is_some() {
        command.stdin(Stdio::piped());
    }
    command.stdout(Stdio::piped()).stderr(Stdio::piped());

    let mut child = match command.spawn() {
        Ok(child) => child,
        Err(err) => {
            update_task(&task_state, |state| {
                state.snapshot.status = "failed".to_string();
                state.snapshot.phase = "启动失败".to_string();
                state.snapshot.message = format!("无法启动 {}：{}", spec.tool.display_path, err);
                state.snapshot.exit_code = -1;
                state.finished_at = Some(Instant::now());
            });
            return;
        }
    };

    let pid = child.id();
    update_task(&task_state, |state| {
        state.snapshot.pid = pid;
        state.snapshot.phase = infer_phase_from_args(&spec.args);
        state.snapshot.message = "进程已启动，可以切换页面或随时取消。".to_string();
    });

    if let Some(input) = spec.stdin.as_deref() {
        if let Some(mut pipe) = child.stdin.take() {
            if let Err(err) = pipe.write_all(input.as_bytes()) {
                update_task(&task_state, |state| {
                    append_limited(
                        &mut state.snapshot.stderr,
                        &format!("写入 stdin 失败：{}\n", err),
                    );
                });
            }
        }
    }

    if let Some(stdout) = child.stdout.take() {
        spawn_pipe_reader(task_state.clone(), stdout, false);
    }
    if let Some(stderr) = child.stderr.take() {
        spawn_pipe_reader(task_state.clone(), stderr, true);
    }

    let wait_result = child.wait();
    let exit_code = wait_result
        .as_ref()
        .ok()
        .and_then(|status| status.code())
        .unwrap_or(-1);
    let result_json = if spec.result_path.is_empty() {
        String::new()
    } else {
        fs::read_to_string(&spec.result_path).unwrap_or_default()
    };

    update_task(&task_state, |state| {
        state.snapshot.exit_code = exit_code;
        state.snapshot.result_json = result_json;
        state.finished_at = Some(Instant::now());
        if state.cancel_requested {
            state.snapshot.status = "canceled".to_string();
            state.snapshot.phase = "已取消".to_string();
            state.snapshot.message =
                "已取消，本次没有继续写入本地 cache、飞书或 ProjectSettings。".to_string();
        } else if wait_result.is_err() {
            state.snapshot.status = "failed".to_string();
            state.snapshot.phase = "等待进程失败".to_string();
            state.snapshot.message = wait_result
                .err()
                .map(|e| e.to_string())
                .unwrap_or_else(|| "等待进程失败。".to_string());
        } else if exit_code == 0 {
            state.snapshot.status = "succeeded".to_string();
            state.snapshot.phase = "完成".to_string();
            state.snapshot.message = "任务完成。".to_string();
        } else {
            state.snapshot.status = "failed".to_string();
            state.snapshot.phase = "失败".to_string();
            state.snapshot.message = format!(
                "任务失败，退出码 {}。请看结果摘要或 Debug 日志。",
                exit_code
            );
        }
    });
}

fn spawn_pipe_reader<R>(task_state: Arc<Mutex<TaskState>>, pipe: R, stderr: bool)
where
    R: std::io::Read + Send + 'static,
{
    thread::spawn(move || {
        let reader = BufReader::new(pipe);
        for line in reader.lines() {
            let Ok(line) = line else {
                break;
            };
            update_task(&task_state, |state| {
                if stderr {
                    append_limited(&mut state.snapshot.stderr, &(line.clone() + "\n"));
                } else {
                    append_limited(&mut state.snapshot.stdout, &(line.clone() + "\n"));
                }
                if state.snapshot.progress_path.is_empty() {
                    state.snapshot.message = shorten_line(&line, 160);
                }
            });
        }
    });
}

fn tail_progress_file(task_state: Arc<Mutex<TaskState>>, progress_path: String) {
    let path = PathBuf::from(progress_path);
    let mut offset = 0usize;
    loop {
        let status = task_state
            .lock()
            .ok()
            .map(|state| state.snapshot.status.clone())
            .unwrap_or_else(|| "failed".to_string());
        if !matches!(status.as_str(), "running" | "canceling") {
            break;
        }

        if let Ok(text) = fs::read_to_string(&path) {
            if text.len() > offset {
                for line in text[offset..].lines() {
                    apply_progress_line(&task_state, line);
                }
                offset = text.len();
            }
        }
        thread::sleep(Duration::from_millis(250));
    }
}

fn apply_progress_line(task_state: &Arc<Mutex<TaskState>>, line: &str) {
    let trimmed = line.trim();
    if trimmed.is_empty() {
        return;
    }

    let parsed = serde_json::from_str::<Value>(trimmed).unwrap_or(Value::Null);
    update_task(task_state, |state| {
        append_limited(&mut state.snapshot.progress_log, &(trimmed.to_string() + "\n"));
        if let Some(phase) = parsed.get("phase").and_then(|v| v.as_str()) {
            state.snapshot.phase = human_progress_phase(phase);
        }

        let table = parsed.get("tableId").and_then(|v| v.as_str()).unwrap_or("");
        let message = parsed.get("message").and_then(|v| v.as_str()).unwrap_or("");
        let current = parsed.get("current").and_then(|v| v.as_i64()).unwrap_or(0);
        let total = parsed.get("total").and_then(|v| v.as_i64()).unwrap_or(0);
        state.snapshot.table_id = table.to_string();
        state.snapshot.current = current;
        state.snapshot.total = total;
        state.snapshot.message = if !table.is_empty() && current > 0 && total > 0 {
            format!("{}（{} / {}）：{}", table, current, total, message)
        } else if !table.is_empty() {
            format!("{}：{}", table, message)
        } else if !message.is_empty() {
            message.to_string()
        } else {
            shorten_line(trimmed, 160)
        };
    });
}

fn human_progress_phase(phase: &str) -> String {
    let lower = phase.to_lowercase();
    if lower.contains("registry") {
        "读取注册中心".to_string()
    } else if lower.contains("online") || lower.contains("sheet") || lower.contains("read") {
        "读取在线表".to_string()
    } else if lower.contains("export") || lower.contains("xlsx") {
        "导出 xlsx".to_string()
    } else if lower.contains("triangulation") || lower.contains("compare") {
        "三方一致性检查".to_string()
    } else if lower.contains("hash") {
        "比较 cache hash".to_string()
    } else if lower.contains("report") {
        "生成报告".to_string()
    } else {
        phase.to_string()
    }
}

fn infer_machine_phase_from_args(args: &[String]) -> String {
    let joined = args.join(" ").to_lowercase();
    if joined.contains("sync-cache") {
        "sync-cache-start".to_string()
    } else if joined.contains("sync-status") {
        "sync-status-start".to_string()
    } else if joined.contains("pr-gate") {
        "pr-gate-report".to_string()
    } else if joined.contains("compare") || joined.contains("merge") {
        "compare-merge".to_string()
    } else if joined.contains("doctor") {
        "doctor".to_string()
    } else {
        "start".to_string()
    }
}

fn initial_progress_message(operation: &str, args: &[String]) -> String {
    let joined = args.join(" ").to_lowercase();
    if joined.contains("sync-cache") && joined.contains("--dry-run") {
        "正在预览同步，不会写入文件；会读取在线表并临时导出 xlsx。".to_string()
    } else if joined.contains("sync-status") {
        "正在快速检查 registry 和本地 cache，不导出 xlsx。".to_string()
    } else if joined.contains("doctor") || operation.contains("doctor") {
        "正在检查飞书 CLI 和本机工具授权。".to_string()
    } else {
        "后台任务已开始。".to_string()
    }
}

fn infer_phase_from_args(args: &[String]) -> String {
    let joined = args.join(" ").to_lowercase();
    if joined.contains("sync-cache") {
        "读取注册中心 / 读取在线表 / 导出 xlsx / 三方一致性检查".to_string()
    } else if joined.contains("pr-gate") {
        "生成 PR 检查报告".to_string()
    } else if joined.contains("compare") || joined.contains("merge") {
        "解析 PR 上下文 / 准备语义输入 / 生成报告".to_string()
    } else if joined.contains("doctor") {
        "检查工具授权".to_string()
    } else {
        "运行本机命令".to_string()
    }
}

fn create_task_state(
    operation: String,
    command_line: String,
    executable_path: String,
    source: String,
    attempted_paths: Vec<String>,
    result_path: String,
    progress_path: String,
) -> (String, Arc<Mutex<TaskState>>, TaskSnapshot) {
    let sequence = NEXT_TASK_ID.fetch_add(1, Ordering::Relaxed);
    let task_id = format!("task-{}-{}", chrono_like_timestamp(), sequence);
    let snapshot = TaskSnapshot {
        task_id: task_id.clone(),
        operation,
        status: "running".to_string(),
        phase: "已开始后台任务".to_string(),
        message: "后台任务已启动，窗口仍可切换页面、打开 Debug 或取消。".to_string(),
        elapsed_ms: 0,
        command_line,
        exit_code: -1,
        stdout: String::new(),
        stderr: String::new(),
        progress_log: String::new(),
        result_path,
        result_json: String::new(),
        executable_path,
        source,
        attempted_paths,
        progress_path,
        pid: 0,
        table_id: String::new(),
        current: 0,
        total: 0,
    };
    let state = TaskState {
        snapshot: snapshot.clone(),
        started_at: Instant::now(),
        finished_at: None,
        cancel_requested: false,
    };
    (task_id, Arc::new(Mutex::new(state)), snapshot)
}

fn task_registry() -> &'static Mutex<HashMap<String, Arc<Mutex<TaskState>>>> {
    TASKS.get_or_init(|| Mutex::new(HashMap::new()))
}

fn remember_task(task_id: String, task_state: Arc<Mutex<TaskState>>) {
    if let Ok(mut registry) = task_registry().lock() {
        registry.insert(task_id, task_state);
    }
}

fn find_task(task_id: &str) -> Result<Arc<Mutex<TaskState>>, String> {
    task_registry()
        .lock()
        .map_err(|_| "任务注册表锁已损坏。".to_string())?
        .get(task_id)
        .cloned()
        .ok_or_else(|| format!("没有找到后台任务：{}", task_id))
}

fn snapshot_task(task_state: &Arc<Mutex<TaskState>>) -> TaskSnapshot {
    let guard = task_state.lock().expect("task state poisoned");
    let mut snapshot = guard.snapshot.clone();
    snapshot.elapsed_ms = guard
        .finished_at
        .unwrap_or_else(Instant::now)
        .duration_since(guard.started_at)
        .as_millis();
    snapshot
}

fn update_task<F>(task_state: &Arc<Mutex<TaskState>>, update: F)
where
    F: FnOnce(&mut TaskState),
{
    if let Ok(mut guard) = task_state.lock() {
        update(&mut guard);
    }
}

fn append_limited(buffer: &mut String, text: &str) {
    const MAX_LOG_CHARS: usize = 262_144;
    buffer.push_str(text);
    if buffer.chars().count() > MAX_LOG_CHARS {
        let trimmed: String = buffer
            .chars()
            .rev()
            .take(MAX_LOG_CHARS)
            .collect::<Vec<_>>()
            .into_iter()
            .rev()
            .collect();
        *buffer = "[日志已截断，仅保留最近部分]\n".to_string() + &trimmed;
    }
}

fn find_named_arg_path(root: &Path, args: &[String], name: &str) -> Option<String> {
    for window in args.windows(2) {
        if window[0] == name {
            return Some(normalize_cli_path_arg(root, &window[1]));
        }
    }
    None
}

fn kill_process_tree(pid: u32) {
    if cfg!(windows) {
        let _ = Command::new("taskkill")
            .args(["/PID", &pid.to_string(), "/T", "/F"])
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn();
    } else {
        let _ = Command::new("kill")
            .args(["-TERM", &pid.to_string()])
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn();
    }
}

fn run_capture_resolved(
    root: &Path,
    tool: &ResolvedTool,
    args: &[&str],
    lark_cli: Option<&ResolvedTool>,
    stdin: Option<&str>,
) -> Result<CliRunResult, String> {
    let mut command = Command::new(&tool.program);
    let all_args = tool.command_args(args);
    command.args(&all_args).current_dir(root);
    configure_process_environment(&mut command, lark_cli);
    if stdin.is_some() {
        command.stdin(Stdio::piped());
    }
    command.stdout(Stdio::piped()).stderr(Stdio::piped());

    let mut child = command.spawn().map_err(|e| {
        format!(
            "无法启动 {}：{}。尝试来源：{}",
            tool.display_path, e, tool.source
        )
    })?;
    if let Some(input) = stdin {
        if let Some(mut pipe) = child.stdin.take() {
            pipe.write_all(input.as_bytes())
                .map_err(|e| format!("写入 stdin 失败：{}", e))?;
        }
    }

    let output = child
        .wait_with_output()
        .map_err(|e| format!("等待进程结束失败：{}", e))?;
    let result_path = find_result_path(root, &all_args).unwrap_or_default();
    let result_json = if result_path.is_empty() {
        String::new()
    } else {
        fs::read_to_string(&result_path).unwrap_or_default()
    };

    Ok(CliRunResult {
        command_line: build_display_command(tool, &all_args),
        exit_code: output.status.code().unwrap_or(-1),
        stdout: String::from_utf8_lossy(&output.stdout).to_string(),
        stderr: String::from_utf8_lossy(&output.stderr).to_string(),
        executable_path: tool.display_path.clone(),
        source: tool.source.clone(),
        attempted_paths: tool.attempted_paths.clone(),
        result_path,
        result_json,
    })
}

fn find_result_path(root: &Path, args: &[String]) -> Option<String> {
    for window in args.windows(2) {
        if window[0] == "--out" {
            return Some(normalize_cli_path_arg(root, &window[1]));
        }
    }

    None
}

fn write_task_progress_event(
    progress_path: &str,
    operation: &str,
    phase: &str,
    table_id: &str,
    current: i64,
    total: i64,
    message: &str,
    severity: &str,
) {
    let normalized = normalize_path_string_for_cli(progress_path);
    if normalized.trim().is_empty() {
        return;
    }

    let line = serde_json::json!({
        "operation": operation,
        "phase": phase,
        "tableId": table_id,
        "current": current,
        "total": total,
        "elapsedMs": 0,
        "message": message,
        "severity": severity
    })
    .to_string();

    let path = PathBuf::from(&normalized);
    if let Some(parent) = path.parent() {
        let _ = fs::create_dir_all(parent);
    }

    if let Ok(mut file) = fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(file, "{}", line);
    }
}

fn configure_process_environment(command: &mut Command, lark_cli: Option<&ResolvedTool>) {
    command.env("LARK_CLI_NO_PROXY", "1");
    if let Some(lark) = lark_cli {
        command.env("CONFIG_SHEET_FORGE_LARK_CLI", &lark.display_path);
        command.env("LARK_CLI_PATH", &lark.display_path);
    }

    if cfg!(windows) {
        if let Ok(appdata) = env::var("APPDATA") {
            let npm = Path::new(&appdata).join("npm");
            if npm.exists() {
                let current = env::var_os("PATH").unwrap_or_default();
                let mut parts: Vec<PathBuf> = env::split_paths(&current).collect();
                if !parts.iter().any(|part| part == &npm) {
                    parts.insert(0, npm);
                    if let Ok(joined) = env::join_paths(parts) {
                        command.env("PATH", joined);
                    }
                }
            }
        }
    }
}

fn build_display_command(tool: &ResolvedTool, args: &[String]) -> String {
    let mut parts = vec![quote_for_display(&tool.program)];
    parts.extend(args.iter().map(|arg| quote_for_display(arg)));
    parts.join(" ")
}

fn quote_for_display(value: &str) -> String {
    if value.contains(' ') || value.contains('\t') {
        format!("\"{}\"", value.replace('"', "\\\""))
    } else {
        value.to_string()
    }
}

fn missing_cli_message(root: &Path, config: &Value) -> String {
    let mut attempts = vec![
        "Desktop 内置 sidecar: <Desktop>/cli/config-sheet-forge.exe".to_string(),
        "CONFIG_SHEET_FORGE_CLI".to_string(),
        "CONFIG_SHEET_FORGE_ROOT + sourceCliProjectRelativePath".to_string(),
        "PATH: config-sheet-forge".to_string(),
    ];
    if let Some(relative) = find_string_deep(
        config,
        &["sourceCliProjectRelativePath", "cliProjectRelativePath"],
    ) {
        attempts.push(format!(
            "项目配置 sourceCliProjectRelativePath: {}",
            relative
        ));
    }

    format!(
        "没有找到 Config Sheet Forge CLI。\n\n当前项目：{}\n\n已经尝试：\n- {}\n\n下一步：请重新安装 Desktop，或在高级模式选择 CLI / 设置 CONFIG_SHEET_FORGE_ROOT。",
        root.display(),
        attempts.join("\n- ")
    )
}

fn read_desktop_project_config(root: &Path) -> Value {
    find_project_config(root)
        .and_then(|path| read_json(&path).ok())
        .unwrap_or(Value::Null)
}

fn resolve_project_root(project_root: String) -> Result<PathBuf, String> {
    let root = if project_root.trim().is_empty() {
        env::current_dir().map_err(|e| e.to_string())?
    } else {
        PathBuf::from(strip_windows_verbatim_prefix(project_root.trim()))
    };
    let resolved = root.canonicalize().unwrap_or(root);
    Ok(PathBuf::from(normalize_path_string_for_cli(
        &resolved.to_string_lossy(),
    )))
}

fn normalize_cli_path_args(root: &Path, args: &[String]) -> Vec<String> {
    let mut normalized = args.to_vec();
    let path_args = [
        "--manifest",
        "--out",
        "--progress",
        "--preview-result",
        "--require-preview",
        "--request",
        "--base",
        "--ours",
        "--theirs",
        "--merged",
        "--cache-dir",
        "--excel-cache-dir",
    ];

    let mut index = 0usize;
    while index + 1 < normalized.len() {
        if path_args.iter().any(|name| *name == normalized[index]) {
            normalized[index + 1] = normalize_cli_path_arg(root, &normalized[index + 1]);
            index += 2;
        } else {
            index += 1;
        }
    }

    normalized
}

fn normalize_cli_path_arg(root: &Path, value: &str) -> String {
    let stripped = strip_windows_verbatim_prefix(value.trim().trim_matches('"'));
    if stripped.trim().is_empty() {
        return String::new();
    }

    let path = PathBuf::from(stripped);
    let full = if path.is_absolute() {
        path
    } else {
        root.join(path)
    };

    normalize_path_string_for_cli(&full.to_string_lossy())
}

fn normalize_path_string_for_cli(value: &str) -> String {
    let stripped = strip_windows_verbatim_prefix(value);
    if cfg!(windows) {
        stripped.replace('/', "\\")
    } else {
        stripped
    }
}

fn strip_windows_verbatim_prefix(value: &str) -> String {
    if let Some(rest) = value.strip_prefix(r"\\?\UNC\") {
        return format!(r"\\{}", rest);
    }

    value.strip_prefix(r"\\?\").unwrap_or(value).to_string()
}

fn read_unity_package_version(root: &Path) -> String {
    let manifest = read_json(&root.join("Packages").join("manifest.json")).unwrap_or(Value::Null);
    let dependency = manifest
        .get("dependencies")
        .and_then(|value| value.get("dev.config-sheet-forge.unity"))
        .and_then(|value| value.as_str())
        .unwrap_or("");
    extract_upm_version(dependency)
}

fn extract_upm_version(value: &str) -> String {
    let trimmed = value.trim();
    if trimmed.is_empty() {
        return String::new();
    }

    if let Some((_, tag)) = trimmed.rsplit_once('#') {
        return tag.trim().to_string();
    }

    trimmed.to_string()
}

fn find_project_config(root: &Path) -> Option<PathBuf> {
    let settings = root.join("ProjectSettings");
    let entries = fs::read_dir(settings).ok()?;
    let mut candidates: Vec<PathBuf> = entries
        .filter_map(|entry| entry.ok().map(|e| e.path()))
        .filter(|path| {
            path.file_name()
                .and_then(|name| name.to_str())
                .map(|name| name.contains("ConfigSheetForge") && name.ends_with(".json"))
                .unwrap_or(false)
        })
        .collect();
    candidates.sort();
    candidates.into_iter().next()
}

fn read_json(path: &Path) -> Result<Value, String> {
    let text = fs::read_to_string(path).map_err(|e| e.to_string())?;
    serde_json::from_str(&text).map_err(|e| e.to_string())
}

fn find_string_deep(value: &Value, keys: &[&str]) -> Option<String> {
    match value {
        Value::Object(map) => {
            for (key, item) in map {
                if keys
                    .iter()
                    .any(|candidate| key.eq_ignore_ascii_case(candidate))
                {
                    if let Some(text) = item.as_str() {
                        return Some(text.to_string());
                    }
                }
                if let Some(found) = find_string_deep(item, keys) {
                    return Some(found);
                }
            }
            None
        }
        Value::Array(items) => items.iter().find_map(|item| find_string_deep(item, keys)),
        _ => None,
    }
}

fn read_git_branch_from_files(root: &Path) -> Option<String> {
    let git_dir = resolve_git_dir(root)?;
    let head = fs::read_to_string(git_dir.join("HEAD")).ok()?;
    let trimmed = head.trim();
    if let Some(branch) = trimmed.strip_prefix("ref: refs/heads/") {
        return Some(branch.trim().to_string());
    }
    if trimmed.len() >= 7 {
        return Some(trimmed.chars().take(7).collect());
    }
    None
}

fn resolve_git_dir(root: &Path) -> Option<PathBuf> {
    let dot_git = root.join(".git");
    if dot_git.is_dir() {
        return Some(dot_git);
    }

    let git_file = fs::read_to_string(dot_git).ok()?;
    let path = git_file.trim().strip_prefix("gitdir:")?.trim();
    let candidate = PathBuf::from(path);
    if candidate.is_absolute() {
        Some(candidate)
    } else {
        Some(root.join(candidate))
    }
}

fn github_repo_from_git_config(root: &Path) -> Option<String> {
    let git_dir = resolve_git_dir(root)?;
    let config = fs::read_to_string(git_dir.join("config")).ok()?;
    let mut in_origin = false;
    for line in config.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with('[') {
            in_origin = trimmed.contains("remote \"origin\"");
            continue;
        }

        if in_origin && trimmed.starts_with("url") {
            if let Some((_, value)) = trimmed.split_once('=') {
                return parse_github_repository(value.trim());
            }
        }
    }
    None
}

fn parse_github_repository(remote: &str) -> Option<String> {
    let trimmed = remote.trim().trim_end_matches(".git");
    if let Some(rest) = trimmed.strip_prefix("https://github.com/") {
        return Some(rest.trim_matches('/').to_string());
    }

    if let Some(rest) = trimmed.strip_prefix("git@github.com:") {
        return Some(rest.trim_matches('/').to_string());
    }

    None
}

fn first_line(text: &str) -> Option<String> {
    text.lines()
        .map(|line| line.trim())
        .find(|line| !line.is_empty())
        .map(|line| line.to_string())
}

fn contains_desktop_dev_url(text: &str) -> bool {
    let loopback = "127.0.0.1";
    let local_host = "localhost";
    let dev_port = "1420";
    let loopback_url = format!("http://{}:{}", loopback, dev_port);
    let loopback_host = format!("{}:{}", loopback, dev_port);
    let localhost_url = format!("http://{}:{}", local_host, dev_port);
    let localhost_host = format!("{}:{}", local_host, dev_port);
    text.contains(&loopback_url)
        || text.contains(&loopback_host)
        || text.contains(&localhost_url)
        || text.contains(&localhost_host)
}

fn chrono_like_timestamp() -> String {
    let millis = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|duration| duration.as_millis())
        .unwrap_or(0);
    millis.to_string()
}

fn sanitize_file_name(value: &str) -> String {
    let mut output = String::new();
    for ch in value.chars() {
        if ch.is_ascii_alphanumeric() || ch == '-' || ch == '_' {
            output.push(ch);
        } else {
            output.push('-');
        }
    }

    if output.is_empty() {
        "command".to_string()
    } else {
        output
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    fn powershell_tool() -> ResolvedTool {
        ResolvedTool {
            program: "powershell.exe".to_string(),
            prefix_args: vec![
                "-NoProfile".to_string(),
                "-ExecutionPolicy".to_string(),
                "Bypass".to_string(),
                "-Command".to_string(),
            ],
            display_path: "powershell.exe".to_string(),
            source: "test".to_string(),
            attempted_paths: Vec::new(),
        }
    }

    #[test]
    #[cfg(windows)]
    fn start_process_task_returns_before_long_process_exits() {
        let spec = TaskSpec {
            operation: "test-long-process".to_string(),
            root: env::current_dir().unwrap_or_else(|_| PathBuf::from(".")),
            tool: powershell_tool(),
            args: vec!["Start-Sleep -Seconds 30".to_string()],
            lark_cli: None,
            stdin: None,
            result_path: String::new(),
            progress_path: String::new(),
        };
        let started = Instant::now();
        let snapshot = start_process_task(spec).expect("task should start");
        assert!(started.elapsed() < Duration::from_millis(500));
        assert_eq!(snapshot.status, "running");
        let _ = cancel_task(snapshot.task_id);
    }

    #[test]
    #[cfg(windows)]
    fn cancel_task_kills_child_process_tree() {
        let pid_file =
            env::temp_dir().join(format!("csforge-child-{}.pid", chrono_like_timestamp()));
        let escaped_pid_file = pid_file.to_string_lossy().replace('\'', "''");
        let script = format!(
            "$p=Start-Process powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 60' -PassThru; Set-Content -LiteralPath '{}' $p.Id; Start-Sleep -Seconds 60",
            escaped_pid_file
        );
        let spec = TaskSpec {
            operation: "test-child-tree".to_string(),
            root: env::current_dir().unwrap_or_else(|_| PathBuf::from(".")),
            tool: powershell_tool(),
            args: vec![script],
            lark_cli: None,
            stdin: None,
            result_path: String::new(),
            progress_path: String::new(),
        };
        let snapshot = start_process_task(spec).expect("task should start");
        let mut child_pid = String::new();
        for _ in 0..20 {
            if let Ok(text) = fs::read_to_string(&pid_file) {
                child_pid = text.trim().to_string();
                if !child_pid.is_empty() {
                    break;
                }
            }
            thread::sleep(Duration::from_millis(100));
        }
        assert!(!child_pid.is_empty(), "child process pid should be written");

        let _ = cancel_task(snapshot.task_id.clone()).expect("cancel should start");
        for _ in 0..30 {
            let task = get_task(snapshot.task_id.clone()).expect("task should exist");
            if is_task_terminal(&task.status) {
                break;
            }
            thread::sleep(Duration::from_millis(100));
        }

        let output = Command::new("tasklist")
            .args(["/FI", &format!("PID eq {}", child_pid)])
            .output()
            .expect("tasklist should run");
        let tasklist = String::from_utf8_lossy(&output.stdout);
        assert!(
            !tasklist.contains(&child_pid),
            "child process should be killed with process tree"
        );
        let _ = fs::remove_file(pid_file);
    }

    fn is_task_terminal(status: &str) -> bool {
        matches!(status, "succeeded" | "failed" | "canceled")
    }

    #[test]
    #[cfg(windows)]
    fn desktop_cli_paths_drop_verbatim_prefix_and_slashes() {
        let root = PathBuf::from(r"N:\UnityProject");
        let args = vec![
            "sync-cache".to_string(),
            "--manifest".to_string(),
            r"\\?\N:\UnityProject/ProjectSettings/Project.ConfigSheetForge.json".to_string(),
            "--dry-run".to_string(),
            "--out".to_string(),
            r"\\?\N:\UnityProject/Temp/ConfigSheetForge/desktop/sync-cache.result.json".to_string(),
            "--progress".to_string(),
            r"\\?\N:\UnityProject/Temp/ConfigSheetForge/desktop/sync-cache.progress.ndjson"
                .to_string(),
        ];

        let normalized = normalize_cli_path_args(&root, &args);
        let joined = normalized.join(" ");
        assert!(
            !joined.contains(r"\\?\"),
            "Desktop must not pass verbatim paths to the CLI: {}",
            joined
        );
        assert!(
            !normalized[2].contains('/'),
            "manifest path should use Windows separators: {}",
            normalized[2]
        );
        assert_eq!(
            normalized[5],
            r"N:\UnityProject\Temp\ConfigSheetForge\desktop\sync-cache.result.json"
        );
        assert_eq!(
            normalized[7],
            r"N:\UnityProject\Temp\ConfigSheetForge\desktop\sync-cache.progress.ndjson"
        );
    }

    #[test]
    fn progress_event_is_created_before_child_reports() {
        let progress_path = env::temp_dir().join(format!(
            "csforge-progress-{}.ndjson",
            chrono_like_timestamp()
        ));
        let _ = fs::remove_file(&progress_path);
        write_task_progress_event(
            &progress_path.to_string_lossy(),
            "sync-cache",
            "sync-cache-start",
            "",
            0,
            0,
            "正在预览同步，不会写入文件。",
            "info",
        );

        let text = fs::read_to_string(&progress_path).expect("progress file should exist");
        assert!(
            text.contains("正在预览同步"),
            "progress message should be written"
        );
        let _ = fs::remove_file(progress_path);
    }
}
