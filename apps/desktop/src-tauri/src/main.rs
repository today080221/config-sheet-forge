use serde::Serialize;
use serde_json::Value;
use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;

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
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ToolCheck {
    name: String,
    status: String,
    summary: String,
    detail: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CliRunResult {
    command_line: String,
    exit_code: i32,
    stdout: String,
    stderr: String,
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
    let git_branch = run_capture(&root, "git", &["branch", "--show-current"])
        .ok()
        .map(|r| r.stdout.trim().to_string())
        .filter(|v| !v.is_empty())
        .unwrap_or_else(|| "unknown".to_string());
    let feishu_profile = find_string_deep(&config, &["feishuProfile", "profile", "currentProfile"])
        .filter(|v| !v.is_empty())
        .unwrap_or_else(|| git_branch.clone());

    Ok(ProjectSnapshot {
        project_root: root.to_string_lossy().to_string(),
        unity_project,
        project_config_path: config_path.to_string_lossy().to_string(),
        project_id: find_string_deep(&config, &["projectId", "id", "name"]).unwrap_or_default(),
        git_branch,
        feishu_profile,
        registry_base_token: find_string_deep(&config, &["registryBaseToken", "baseToken"]).unwrap_or_default(),
        registry_base_url: find_string_deep(&config, &["registryBaseUrl", "baseUrl"]).unwrap_or_default(),
    })
}

#[tauri::command]
fn doctor_tools(project_root: String) -> Vec<ToolCheck> {
    let root = resolve_project_root(project_root).unwrap_or_else(|_| env::current_dir().unwrap_or_else(|_| PathBuf::from(".")));
    let mut checks = Vec::new();
    checks.push(tool_check(&root, "git", &["--version"], "git 必需，用于识别当前分支和 merge-base。"));
    checks.push(tool_check(&root, "lark-cli", &["doctor"], "lark-cli 必需，用 bot 身份读取飞书 Base 和在线 Sheet。"));
    checks.push(tool_check(&root, "gh", &["auth", "status"], "GitHub CLI 可选但推荐，用于自动识别 PR base branch。"));

    let no_proxy = env::var("LARK_CLI_NO_PROXY").unwrap_or_default();
    let no_proxy_all = env::var("NO_PROXY").unwrap_or_default();
    let proxy_ok = no_proxy == "1" || no_proxy_all == "*";
    checks.push(ToolCheck {
        name: "Feishu 代理设置".to_string(),
        status: if proxy_ok { "ok" } else { "warning" }.to_string(),
        summary: if proxy_ok {
            "已设置不走代理".to_string()
        } else {
            "建议飞书请求不走代理".to_string()
        },
        detail: "可设置 LARK_CLI_NO_PROXY=1 或 NO_PROXY=*，避免公司代理影响飞书导出。".to_string(),
    });
    checks
}

#[tauri::command]
fn run_cli(project_root: String, args: Vec<String>) -> Result<CliRunResult, String> {
    let root = resolve_project_root(project_root)?;
    let cli = env::var("CONFIG_SHEET_FORGE_CLI").unwrap_or_else(|_| "config-sheet-forge".to_string());
    let arg_refs: Vec<&str> = args.iter().map(|s| s.as_str()).collect();
    run_capture(&root, &cli, &arg_refs)
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![discover_project, doctor_tools, run_cli])
        .run(tauri::generate_context!())
        .expect("error while running Config Sheet Forge desktop");
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

fn tool_check(root: &Path, name: &str, args: &[&str], help: &str) -> ToolCheck {
    match run_capture(root, name, args) {
        Ok(result) if result.exit_code == 0 => ToolCheck {
            name: name.to_string(),
            status: "ok".to_string(),
            summary: first_line(&result.stdout).unwrap_or_else(|| "可用".to_string()),
            detail: help.to_string(),
        },
        Ok(result) => ToolCheck {
            name: name.to_string(),
            status: "warning".to_string(),
            summary: first_line(&result.stderr)
                .or_else(|| first_line(&result.stdout))
                .unwrap_or_else(|| "命令返回非 0。".to_string()),
            detail: help.to_string(),
        },
        Err(err) => ToolCheck {
            name: name.to_string(),
            status: "warning".to_string(),
            summary: format!("未找到或不可运行：{}", name),
            detail: format!("{} {}", help, err),
        },
    }
}

fn run_capture(root: &Path, executable: &str, args: &[&str]) -> Result<CliRunResult, String> {
    let output = Command::new(executable)
        .args(args)
        .current_dir(root)
        .output()
        .map_err(|e| format!("无法启动 {}：{}", executable, e))?;

    Ok(CliRunResult {
        command_line: format!("{} {}", executable, args.join(" ")).trim().to_string(),
        exit_code: output.status.code().unwrap_or(-1),
        stdout: String::from_utf8_lossy(&output.stdout).to_string(),
        stderr: String::from_utf8_lossy(&output.stderr).to_string(),
    })
}

fn first_line(text: &str) -> Option<String> {
    text.lines()
        .map(|line| line.trim())
        .find(|line| !line.is_empty())
        .map(|line| line.to_string())
}
