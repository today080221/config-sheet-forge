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

Legacy 只用于没有 Desktop、CI 调试或救急 fallback。普通策划不需要从 Legacy 开始。

## Desktop v1 页面

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

只有最近一次同输入 dry-run 通过时才允许 apply。写入范围只包括：

- `.config-sheet-forge/excel-cache/*.xlsx`
- `.config-sheet-forge/cache/*.semantic.json`
- `.config-sheet-forge/cache/*.sha256`

无变化时不会重写，mtime 保持不变。

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
