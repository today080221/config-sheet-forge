# Config Sheet Forge Desktop 工作台

Desktop 是 Config Sheet Forge 的官方主工作台。它负责日常配表 Source of Truth 流程：看状态、预览同步、写 cache、修复 cache 类型、生成合并预览、提交合并审查、运行 PR 检查。Unity 只保留最后一步必须在 Editor 内完成的导入资产能力。

一句话版：

- 飞书在线 Sheet 是正式源头。
- `.config-sheet-forge/excel-cache` 是给 Unity/CI 用的生成 cache。
- `Excel/` 是旧本地 Excel/OneDrive 工作流，不由 Source of Truth 同步写入。
- 预览永远安全，只读不写。
- 写入 cache、创建在线表、写审查记录、写回 main 都必须确认。

## 为什么从 Unity IMGUI 迁到 Desktop

`sync-cache` 需要读取飞书注册中心、读取在线表、导出 xlsx、做 semantic normalize 和三方一致性检查。16 张表首次跑可能需要几分钟。Unity IMGUI 适合最后调用 ExcelToSO 导入 asset，不适合承载长网络任务和大量日志。

从 0.4.29 开始：

- `Tools > Config Sheet Forge` 打开 thin bridge。
- thin bridge 推荐打开 Desktop。
- 完整旧 IMGUI 工作流仍在 `Tools > Config Sheet Forge > Legacy > 完整 Unity 工作台`。

从 0.4.31 开始，GitHub Release 会附带 Windows x64 Desktop portable zip 和 `.sha256`。Unity thin bridge 找不到 Desktop 时，可以一键下载安装到 `%LOCALAPPDATA%/ConfigSheetForge/Desktop/v<version>/`，并通过 EditorPrefs 记住路径；这个安装不会改项目仓库、ProjectSettings、Packages 或旧 `Excel/`。

从 0.4.32 开始，Windows portable zip 使用 Tauri production build。它不需要本机安装 Node、pnpm、Vite、Rust，也不依赖 `CONFIG_SHEET_FORGE_ROOT`。如果 Unity 检测到已安装 exe 仍指向 `127.0.0.1:1420 / localhost:1420`，会把它视为疑似开发构建并要求升级。

从 0.4.33 开始，Windows portable zip 会一起带上 `cli/config-sheet-forge.exe`。Desktop 默认用这个 sidecar CLI，所以新同事只从 Unity 安装 Desktop 也能跑环境检查和项目识别，不需要提前把 `config-sheet-forge` 加进 PATH。`lark-cli` 会自动查找 `%APPDATA%/npm/lark-cli.ps1` 和 `.cmd`，并在环境页显示实际使用路径。

从 0.4.34 开始，Desktop 默认是场景向导，而不是命令面板。首页会在五个场景里推荐一个下一步：环境/授权、同步并导入 Unity、准备 PR 合并、新建配表、从 main/PR base 派生当前分支。普通用户只需要看当前场景里的“下一步”主按钮；完整命令、路径、stdout/stderr、result JSON 都在 Debug 抽屉里。

从 0.4.35 开始，环境/授权页会把“工具已安装”和“账号已授权”分开显示。`gh` 已登录时只显示“已授权为 xxx”，不会再给醒目的“GitHub 授权”按钮；`lark-cli` bot doctor 通过时会收起 App Secret 输入框，只在更多操作里提供重新配置。普通视图会把 doctor JSON 转成人话，完整路径和 raw 输出仍只在 Debug 里。

Legacy 只用于没有 Desktop、CI 调试或救急 fallback。普通策划不需要从 Legacy 开始。

## Desktop v1 页面

### 三层视图

- `策划视图`：默认视图，只显示人话结论、下一步、安全说明。
- `程序视图`：补充生命周期和读写范围，例如“将调用 sync-cache dry-run，读取 live registry 和在线 Sheet，不写文件”。
- `Debug`：独立开关。打开后才显示完整命令、result JSON、stdout/stderr、工具路径和 attempted paths。

普通视图不会展示 token、长路径或 raw JSON。需要复制诊断时再打开 Debug。

### 智能场景

五个场景对应日常流程：

- `环境/授权`：检查 Git、gh、lark-cli、bot/user 授权。
- `同步并导入 Unity`：预览同步，必要时写入 `.config-sheet-forge` cache，再通过 Unity bridge 导入 ScriptableObject asset。
- `准备 PR 合并`：生成合并预览，提交 MergeReviews，运行 PR gate。
- `新建配表`：结构化字段表单，先预览再创建。
- `从 main/PR base 派生当前分支`：新分支缺在线工作区时，从目标分支派生，不再引导普通用户走历史 Excel Seed。

每个场景只有一个主按钮。次要动作在“更多操作”，工程诊断在 Debug。

### 项目识别

选择 Unity 项目目录后，Desktop 会读取：

- `ProjectSettings/*ConfigSheetForge*.json`
- 当前 git branch
- Feishu profile 规则
- registry Base 配置

它不会要求 ProjectSettings 保存每张在线表的 Sheet token。在线表定位以飞书 Base 注册中心为准。

### 环境检查

Desktop 会检查：

- git：必需，用于分支和 merge-base。
- lark-cli：必需，用 bot 身份读取飞书 Base / Sheet。
- gh：可选但推荐，用于自动识别 GitHub PR base branch。
- 代理：飞书请求建议不走代理，可设置 `LARK_CLI_NO_PROXY=1` 或 `NO_PROXY=*`。

缺 `gh` 时，合并页仍可手动选择目标分支。缺 `lark-cli` 或 bot 权限不足时，同步和 PR gate 会明确阻断。

环境页提供按钮处理常见前置条件：

- `安装 Git`：Windows 下优先尝试 `winget install Git.Git`。
- `安装 GitHub CLI`：Windows 下优先尝试 `winget install GitHub.cli`，随后可点 `GitHub 授权`。
- `安装 lark-cli`：通过 `npm install -g @larksuite/cli` 安装；如果没有 npm，会提示先安装 Node.js LTS。
- `配置飞书 bot`：App Secret 通过 stdin 传给 `lark-cli config init --app-secret-stdin`，不会写入仓库或日志。
- `登录飞书用户身份`：用于交互式 Desktop 的本机诊断和安全预览；CI / PR hard gate 仍默认 strict bot。

### 同步预览

`预览同步计划` 是安全操作：

- 读取 live registry。
- 读取在线 Sheet。
- 临时导出 xlsx。
- 做 semantic normalize。
- 做三方一致性检查。
- 做 hash gate。

它不写飞书、不写正式 cache、不改 ProjectSettings、不碰旧 `Excel/`。

### 写入本地 cache

只有最近一次同输入 dry-run 通过时才允许 apply。0.4.34 起 Desktop 会把 dry-run 的 result 通过 `--preview-result <result.json>` 交给 CLI；CLI 会校验 branch/profile/table scope/fingerprint，防止“预览的不是这一次写入”。写入范围只包括：

- `.config-sheet-forge/excel-cache/*.xlsx`
- `.config-sheet-forge/cache/*.semantic.json`
- `.config-sheet-forge/cache/*.sha256`

无变化时不会重写，mtime 保持不变。

如果 dry-run 显示 `cacheStatus=upToDate`，Desktop 不会再推荐“写入本地 cache”，而是直接提示“下一步导入 Unity”。如果 `blocked`，写入按钮禁用，并显示阻断表和修复建议。

### 重写 cache dialect

如果内容语义没变，但 cache xlsx 的第 2 行类型仍是旧 portable dialect，例如 `integer`、`number`、`json`，可以走快速修复：

```bash
config-sheet-forge repair-cache-dialect --manifest ProjectSettings/Your.ConfigSheetForge.json --dry-run
config-sheet-forge repair-cache-dialect --manifest ProjectSettings/Your.ConfigSheetForge.json --yes
```

这个操作不联网，只修 `.config-sheet-forge/excel-cache/*.xlsx` 的物理类型行。

常见映射：

| Source of Truth 类型 | ExcelToSO cache 类型 |
| --- | --- |
| `integer` | `int` |
| `number` | `float` |
| `boolean` | `bool` |
| `text` | `string` |
| `integer[]` | `int[]` |
| `number[]` | `float[]` |
| `text[]` | `string[]` |

`json` 不能自动猜。工具必须能从 schema、`originalType`、`excelToSoType` 或旧 Excel 类型里还原为 `int[]`、`float[]`、`string[]` 或 `string`，否则会中文阻断。

### 导入 Unity asset

这一步仍由 Unity bridge 执行，因为它要调用 Unity Editor 和 ExcelToSO：

1. 确认 `SourceOfTruthCache` profile 已安装。
2. 确认 cache 最新且 dialect 可导入。
3. 调用 ExcelToSO `ImportByProfile(SourceOfTruthCache)`。
4. 只写 Unity ScriptableObject asset。

它不写飞书、不写 registry、不写 main、不改变 ExcelToSO default/local profile。

如果 Desktop 是从 Unity thin bridge 启动的，它会收到一个 bridge session 目录。此时 Desktop 可以直接向 Unity 发送 `import-assets`、`install-profile` 或 `read-pr-gate` 命令；独立启动时则会提示回到 Unity 完成导入。

安装/更新 `SourceOfTruthCache` profile 时，Unity bridge 会镜像现有 default/local ExcelToSO profile：保留脚本目录、asset 目录、namespace、导入选项和 slave 表，只替换 Excel 路径到 `.config-sheet-forge/excel-cache`。如果缺少可用模板，则使用项目配置里的 `unityExcelToSo` 默认值；仍缺目录或 namespace 时会阻断，避免把生成 asset 写到 `Assets` 根目录。

### 合并和 PR gate

Desktop 会按 PR 心智展示：

- 当前分支。
- 目标分支。
- GitHub PR。
- merge-base。
- 合并预览报告。

生成合并预览不写 main。预览通过后，人工可以提交 MergeReviews 审查记录。PR gate 会从 live MergeReviews / SchemaReviews / Waivers hydrate 状态，并输出纯 `Temp/ConfigSheetForge/pr-gate-report.json`。

## Unity bridge

Unity 默认窗口只保留：

- 打开 Config Sheet Forge Desktop。
- 安装/更新 SourceOfTruthCache profile。
- 导入 Unity 配表资产。
- 运行/读取 PR gate report。
- 查看最近结果。
- 打开 Legacy 完整 Unity 工作台。

如果 Desktop 未安装，可以设置：

- `CONFIG_SHEET_FORGE_DESKTOP`：指向 Desktop 可执行文件。
- `CONFIG_SHEET_FORGE_ROOT`：指向 config-sheet-forge checkout，Unity 会尝试用源码模式启动 `apps/desktop`。

## 安全边界

Desktop 和 Unity bridge 都必须遵守：

- strict bot 默认开启，不静默 fallback 到 user。
- 不写旧 `Excel/`。
- 不写 secret。
- 写入动作必须有 dry-run 和确认。
- ProjectSettings 只保存策略和路径，不保存每张 live Sheet token。
