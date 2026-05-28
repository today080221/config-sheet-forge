use serde::Serialize;
use serde_json::Value;
use std::env;
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ProjectSnapshot {
    project_root: String,
    unity_project: bool,
    project_config_path: String,
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
struct ToolCheck {
    name: String,
    status: String,
    summary: String,
    detail: String,
    executable_path: String,
    source: String,
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
    let git_branch = run_simple_capture(&root, "git", &["branch", "--show-current"])
        .ok()
        .map(|r| r.stdout.trim().to_string())
        .filter(|v| !v.is_empty())
        .unwrap_or_else(|| "unknown".to_string());
    let feishu_profile = find_string_deep(&config, &["feishuProfile", "profile", "currentProfile"])
        .filter(|v| !v.is_empty())
        .unwrap_or_else(|| git_branch.clone());
    let github_repository = find_string_deep(&config, &["githubRepository", "repository"])
        .or_else(|| github_repo_from_git_remote(&root))
        .unwrap_or_default();
    let pr = detect_current_pr(&root);

    Ok(ProjectSnapshot {
        project_root: root.to_string_lossy().to_string(),
        unity_project,
        project_config_path: config_path.to_string_lossy().to_string(),
        project_id: find_string_deep(&config, &["projectId", "id", "name"]).unwrap_or_default(),
        git_branch_url: if github_repository.is_empty() || git_branch == "unknown" {
            String::new()
        } else {
            format!("https://github.com/{}/tree/{}", github_repository, git_branch)
        },
        git_branch,
        feishu_profile,
        registry_base_token: find_string_deep(&config, &["registryBaseToken", "baseToken"]).unwrap_or_default(),
        registry_base_url: find_string_deep(&config, &["registryBaseUrl", "baseUrl"]).unwrap_or_default(),
        github_repository,
        pr_url: pr.1,
        pr_number: pr.0,
    })
}

#[tauri::command]
fn doctor_tools(project_root: String) -> Vec<ToolCheck> {
    let root = resolve_project_root(project_root).unwrap_or_else(|_| env::current_dir().unwrap_or_else(|_| PathBuf::from(".")));
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

    checks.push(tool_check_resolved(
        &root,
        "lark-cli",
        resolve_lark_cli(&root, &config),
        &["doctor"],
        "lark-cli 必需，用 bot/user 身份读取飞书 Base 和在线 Sheet。Desktop 子进程默认设置 LARK_CLI_NO_PROXY=1，飞书直连。",
        "安装 lark-cli",
        "install_lark_cli",
        "https://github.com/larksuite/cli",
    ));

    checks.push(tool_check_resolved(
        &root,
        "gh",
        resolve_path_tool("gh", &[".exe", ".cmd", ".bat", ""]),
        &["auth", "status"],
        "GitHub CLI 推荐安装，用于自动识别当前 PR 和目标分支；缺失时仍可手动选择目标分支。",
        "安装 GitHub CLI",
        "install_gh",
        "https://cli.github.com/",
    ));

    checks.push(ToolCheck {
        name: "飞书直连".to_string(),
        status: "ok".to_string(),
        summary: "Desktop 子进程会设置 LARK_CLI_NO_PROXY=1".to_string(),
        detail: "飞书导出和 Base 读取默认不走代理；如果公司网络需要特殊代理，请在程序视图里检查本机环境。".to_string(),
        executable_path: String::new(),
        source: "desktop-env".to_string(),
        action: String::new(),
        action_label: String::new(),
        url: String::new(),
    });

    checks
}

#[tauri::command]
fn run_cli(project_root: String, args: Vec<String>) -> Result<CliRunResult, String> {
    let root = resolve_project_root(project_root)?;
    let config = read_desktop_project_config(&root);
    let resolved = resolve_config_sheet_forge_cli(&root, &config).ok_or_else(|| missing_cli_message(&root, &config))?;
    let lark = resolve_lark_cli(&root, &config);
    let arg_refs: Vec<&str> = args.iter().map(|s| s.as_str()).collect();
    run_capture_resolved(&root, &resolved, &arg_refs, lark.as_ref(), None)
}

#[tauri::command]
fn run_setup_action(
    project_root: String,
    action: String,
    app_id: String,
    secret_value: String,
) -> Result<CliRunResult, String> {
    let root = resolve_project_root(project_root)?;
    let config = read_desktop_project_config(&root);
    match action.as_str() {
        "install_git" => run_setup_tool(&root, "winget", &["install", "--id", "Git.Git", "-e", "--source", "winget"], None),
        "install_gh" => run_setup_tool(&root, "winget", &["install", "--id", "GitHub.cli", "-e", "--source", "winget"], None),
        "gh_auth" => {
            let gh = resolve_path_tool("gh", &[".exe", ".cmd", ".bat", ""]).ok_or_else(|| "没有找到 gh。请先点击“安装 GitHub CLI”。".to_string())?;
            run_capture_resolved(&root, &gh, &["auth", "login"], None, None)
        }
        "install_lark_cli" => run_setup_tool(&root, "npm", &["install", "-g", "@larksuite/cli"], None),
        "lark_auth_user" => {
            let lark = resolve_lark_cli(&root, &config).ok_or_else(|| "没有找到 lark-cli。请先点击“安装 lark-cli”。".to_string())?;
            run_capture_resolved(&root, &lark, &["auth", "login", "--recommend"], None, None)
        }
        "lark_doctor" => {
            let lark = resolve_lark_cli(&root, &config).ok_or_else(|| "没有找到 lark-cli。请先点击“安装 lark-cli”。".to_string())?;
            run_capture_resolved(&root, &lark, &["doctor"], None, None)
        }
        "configure_lark_bot" => {
            if app_id.trim().is_empty() || secret_value.is_empty() {
                return Err("请先填写飞书 App ID 和 App Secret。Secret 只会通过 stdin 传给 lark-cli，不写入仓库或日志。".to_string());
            }

            let lark = resolve_lark_cli(&root, &config).ok_or_else(|| "没有找到 lark-cli。请先点击“安装 lark-cli”。".to_string())?;
            let stdin = format!("{}\n", secret_value);
            run_capture_resolved(
                &root,
                &lark,
                &["config", "init", "--app-id", app_id.trim(), "--app-secret-stdin", "--brand", "feishu"],
                None,
                Some(&stdin),
            )
        }
        _ => Err("未知安装/授权动作：".to_string() + &action),
    }
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

    command.spawn().map_err(|e| format!("无法打开链接：{}", e))?;
    Ok(())
}

fn main() {
    let args: Vec<String> = env::args().collect();
    if args.iter().any(|arg| arg == "--smoke-release") {
        std::process::exit(run_release_smoke(args.iter().any(|arg| arg == "--expect-sidecar")));
    }

    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![startup_project_root, discover_project, doctor_tools, run_cli, run_setup_action, open_external_url])
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
        .and_then(|tool| run_capture_resolved(&env::current_dir().unwrap_or_else(|_| PathBuf::from(".")), tool, &["help"], None, None).ok())
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
    let source_relative = find_string_deep(config, &["sourceCliProjectRelativePath", "cliProjectRelativePath"])
        .unwrap_or_else(|| "src/cli/ConfigSheetForge.Cli".to_string());
    if !source_root.trim().is_empty() {
        let project = Path::new(source_root.trim()).join(source_relative);
        if project.exists() {
            return Some(ResolvedTool {
                program: "dotnet".to_string(),
                prefix_args: vec!["run".to_string(), "--project".to_string(), project.to_string_lossy().to_string(), "--".to_string()],
                display_path: format!("dotnet run --project {}", project.display()),
                source: "CONFIG_SHEET_FORGE_ROOT source checkout".to_string(),
                attempted_paths: vec![project.to_string_lossy().to_string()],
            });
        }
    }

    let mut attempted = Vec::new();
    let mut path_tool = resolve_path_tool_with_attempts("config-sheet-forge", &[".exe", ".cmd", ".bat", ""], &mut attempted);
    if let Some(tool) = path_tool.as_mut() {
        tool.attempted_paths.extend(attempted);
        return path_tool;
    }

    let source_project = root.join("src/cli/ConfigSheetForge.Cli");
    if source_project.exists() {
        return Some(ResolvedTool {
            program: "dotnet".to_string(),
            prefix_args: vec!["run".to_string(), "--project".to_string(), source_project.to_string_lossy().to_string(), "--".to_string()],
            display_path: format!("dotnet run --project {}", source_project.display()),
            source: "project-local source checkout".to_string(),
            attempted_paths: vec![source_project.to_string_lossy().to_string()],
        });
    }

    None
}

fn resolve_sidecar_cli() -> Option<ResolvedTool> {
    let exe_dir = env::current_exe().ok().and_then(|path| path.parent().map(|p| p.to_path_buf()))?;
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
    let local_config = read_json(&root.join(".config-sheet-forge").join("config.json")).unwrap_or(Value::Null);
    let mut attempted = Vec::new();
    for (source, value) in [
        ("env:CONFIG_SHEET_FORGE_LARK_CLI", env::var("CONFIG_SHEET_FORGE_LARK_CLI").unwrap_or_default()),
        ("env:LARK_CLI_PATH", env::var("LARK_CLI_PATH").unwrap_or_default()),
        ("project:toolkit.larkCliPath", find_string_deep(config, &["larkCliPath", "larkCliExecutable"]).unwrap_or_default()),
        ("local:.config-sheet-forge/config.json", find_string_deep(&local_config, &["larkCliPath", "larkCliExecutable"]).unwrap_or_default()),
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

    resolve_path_tool_with_attempts("lark-cli", &[".ps1", ".cmd", ".exe", ".bat", ""], &mut attempted)
}

fn resolve_path_tool(name: &str, extensions: &[&str]) -> Option<ResolvedTool> {
    let mut attempted = Vec::new();
    resolve_path_tool_with_attempts(name, extensions, &mut attempted)
}

fn resolve_path_tool_with_attempts(name: &str, extensions: &[&str], attempted: &mut Vec<String>) -> Option<ResolvedTool> {
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

fn resolve_explicit_tool_with_attempts(value: &str, source: &str, attempted: &mut Vec<String>) -> Option<ResolvedTool> {
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
    let extension = path.extension().and_then(|value| value.to_str()).unwrap_or("").to_lowercase();
    let display_path = path.to_string_lossy().to_string();
    if cfg!(windows) && extension == "ps1" {
        return ResolvedTool {
            program: "powershell.exe".to_string(),
            prefix_args: vec!["-NoProfile".to_string(), "-ExecutionPolicy".to_string(), "Bypass".to_string(), "-File".to_string(), display_path.clone()],
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

fn run_setup_tool(root: &Path, name: &str, args: &[&str], stdin: Option<&str>) -> Result<CliRunResult, String> {
    let tool = resolve_path_tool(name, &[".exe", ".cmd", ".bat", ""]).ok_or_else(|| format!("没有找到 {}，请先安装或手动配置 PATH。", name))?;
    run_capture_resolved(root, &tool, args, None, stdin)
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
                detail: format!("{} 来源：{}。路径：{}", help, tool.source, tool.display_path),
                executable_path: tool.display_path,
                source: tool.source,
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
                detail: format!("{} 已识别路径：{}。请按提示修复授权或配置。", help, tool.display_path),
                executable_path: tool.display_path,
                source: tool.source,
                action: action.to_string(),
                action_label: action_label.to_string(),
                url: url.to_string(),
            },
            Err(err) => ToolCheck {
                name: name.to_string(),
                status: "warning".to_string(),
                summary: "已找到但无法运行".to_string(),
                detail: format!("{} 路径：{}。原因：{}", help, tool.display_path, err),
                executable_path: tool.display_path,
                source: tool.source,
                action: action.to_string(),
                action_label: action_label.to_string(),
                url: url.to_string(),
            },
        },
        None => ToolCheck {
            name: name.to_string(),
            status: if name == "gh" { "warning" } else { "error" }.to_string(),
            summary: format!("未找到 {}", name),
            detail: help.to_string(),
            executable_path: String::new(),
            source: "not-found".to_string(),
            action: action.to_string(),
            action_label: action_label.to_string(),
            url: url.to_string(),
        },
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

    let mut child = command
        .spawn()
        .map_err(|e| format!("无法启动 {}：{}。尝试来源：{}", tool.display_path, e, tool.source))?;
    if let Some(input) = stdin {
        if let Some(mut pipe) = child.stdin.take() {
            pipe.write_all(input.as_bytes()).map_err(|e| format!("写入 stdin 失败：{}", e))?;
        }
    }

    let output = child.wait_with_output().map_err(|e| format!("等待进程结束失败：{}", e))?;
    Ok(CliRunResult {
        command_line: build_display_command(tool, &all_args),
        exit_code: output.status.code().unwrap_or(-1),
        stdout: String::from_utf8_lossy(&output.stdout).to_string(),
        stderr: String::from_utf8_lossy(&output.stderr).to_string(),
        executable_path: tool.display_path.clone(),
        source: tool.source.clone(),
        attempted_paths: tool.attempted_paths.clone(),
    })
}

fn run_simple_capture(root: &Path, executable: &str, args: &[&str]) -> Result<CliRunResult, String> {
    let tool = ResolvedTool {
        program: executable.to_string(),
        prefix_args: Vec::new(),
        display_path: executable.to_string(),
        source: "direct".to_string(),
        attempted_paths: Vec::new(),
    };
    run_capture_resolved(root, &tool, args, None, None)
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
    if let Some(relative) = find_string_deep(config, &["sourceCliProjectRelativePath", "cliProjectRelativePath"]) {
        attempts.push(format!("项目配置 sourceCliProjectRelativePath: {}", relative));
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
        PathBuf::from(project_root.trim())
    };
    Ok(root.canonicalize().unwrap_or(root))
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
                if keys.iter().any(|candidate| key.eq_ignore_ascii_case(candidate)) {
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

fn github_repo_from_git_remote(root: &Path) -> Option<String> {
    let remote = run_simple_capture(root, "git", &["remote", "get-url", "origin"]).ok()?.stdout.trim().to_string();
    parse_github_repository(&remote)
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

fn detect_current_pr(root: &Path) -> (String, String) {
    let Some(gh) = resolve_path_tool("gh", &[".exe", ".cmd", ".bat", ""]) else {
        return (String::new(), String::new());
    };
    let Ok(result) = run_capture_resolved(root, &gh, &["pr", "view", "--json", "number,url"], None, None) else {
        return (String::new(), String::new());
    };
    if result.exit_code != 0 {
        return (String::new(), String::new());
    }

    let json = serde_json::from_str::<Value>(&result.stdout).unwrap_or(Value::Null);
    let number = json.get("number").and_then(|v| v.as_i64()).map(|v| v.to_string()).unwrap_or_default();
    let url = json.get("url").and_then(|v| v.as_str()).unwrap_or_default().to_string();
    (number, url)
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
