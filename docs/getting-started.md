# 入门指南

Config Sheet Forge 的核心原则是：飞书在线 Sheet 是正式源头，本地 Excel/cache 只是给 Unity 和 PR 使用的物化结果。

在 Unity 项目里，推荐把共享规则放在 `ProjectSettings/*ConfigSheetForge*.json`，把 live Sheet 定位放在飞书 Base 注册中心：

- `ProjectSettings`：表 ID、分支/profile 命名规则、默认路径、负责人角色、adapter。
- 飞书 Base 注册中心：BranchBindings、ConfigSheets、MergeReviews、SchemaReviews、Waivers。
- `.config-sheet-forge/`：本机状态和 cache，可重建，默认不进 git。

不要要求项目把每张表的 `SpreadsheetToken` / `SheetId` 写回 `ProjectSettings`。Unity 窗口会通过只读 `registry-status` 从 Base 注册中心读取 live locator。

从 0.4.29 开始，推荐日常使用官方 Tauri Desktop 工作台，而不是在 Unity IMGUI 里跑完整流程。Unity 默认窗口只做 thin bridge：打开 Desktop、安装/更新 `SourceOfTruthCache` profile、导入 Unity asset、运行/读取 PR gate。旧完整 Unity 工作流保留在 `Tools > Config Sheet Forge > Legacy`，用于没有 Desktop 或救急 fallback。Desktop 使用说明见 [Desktop 工作台](desktop-workbench.md)。

从 0.4.31 开始，Unity thin bridge 可以一键安装 Windows Desktop。未安装时点 `安装 Desktop`，工具会下载当前 UPM tag 对应的 GitHub Release zip、校验 sha256，并安装到本机用户目录；不会改项目文件。没有网络时，窗口会给出手动下载链接。

从 0.4.32 开始，Desktop zip 是生产构建，不依赖本机 Node/Vite dev server。若 Unity 检测到旧包仍指向 `127.0.0.1:1420`，会提示重新安装同版本 Desktop。

从 0.4.33 开始，Desktop zip 自带 `config-sheet-forge` CLI sidecar。团队成员只需要从 Unity 点“安装 Desktop”，不需要再手动安装全局 `config-sheet-forge`。如果本机装过 `lark-cli`，Desktop 会优先识别 `%APPDATA%/npm/lark-cli.ps1` / `.cmd`；缺 Git、gh、lark-cli 或授权时，环境页会给安装/登录按钮。

从 0.4.34 开始，Desktop 首页是场景向导。第一次不知道点什么时，先看推荐场景和唯一主按钮：通常是“环境检查”或“预览同步”。`cacheStatus=upToDate` 时不会再让你写 cache，而是直接进入“导入 Unity”；`blocked` 时写入按钮会禁用并列出阻断表。

从 0.4.37 开始，Desktop 顶部会显示 Desktop / UPM / CLI 版本；普通视图不会再出现内部 taskId、PID 或 .NET 堆栈。首页刷新默认走“快速状态检查（不导出 xlsx）”；“完整同步预览”才会读取在线表并临时导出 xlsx，可能需要几分钟，但仍然不写 cache、飞书或 ProjectSettings。

从 0.4.38 开始，完整同步预览成功后 Desktop 会自动进入正确下一步：本地 cache 需要更新时显示“写入本地 cache”，cache 已最新时显示“导入 Unity asset”，被阻断时只显示修复建议。结果文件会被 Desktop 自动恢复，重开应用也不会回到“还没有同步预览”。

从 0.4.44 开始，如果 cache 的 semantic/hash 已最新，但 xlsx 第 2 行仍有 `json/integer/number` 等 ExcelToSO 不能直接导入的类型，Desktop 不会让你继续导入 Unity，而是提示“修复 cache 类型行”。这一步只改 `.config-sheet-forge/excel-cache` 里的生成 cache，不联网、不写飞书、不改旧 `Excel/`。

从 0.4.45 开始，这个修复还会检查 xlsx 本身能不能被 ExcelToSO 读取。旧的极简 cache 如果缺少 `sharedStrings.xml` 或 `styles.xml`，修复会离线重写为兼容格式；如果仍不可读，界面会停下来给出中文原因。

## 安装要求

- .NET 8 SDK 或更新版本，用于 CLI。
- `lark-cli`，用于飞书/Lark provider。
- Unity 2021.3 或更新版本，用于 UPM 包。
- git，用于识别当前分支和 merge-base。
- GitHub CLI `gh` 可选但推荐。它只用于 Unity 合并页自动识别当前 PR；没装时仍可手动选择目标分支。
- Node.js / npm 与 Rust toolchain 只在从源码构建 Desktop 时需要；普通项目可以直接使用发布好的 Desktop。

如果普通终端能运行 `lark-cli`，但 Unity 窗口提示“本机没有找到 lark-cli”，通常是 Unity 启动时没有继承 npm 全局路径。优先尝试：

1. 重启 Unity。
2. 在项目配置里设置 `toolkit.larkCliPath`，或在系统环境变量里设置 `CONFIG_SHEET_FORGE_LARK_CLI` 指向 `lark-cli.ps1` / `lark-cli.cmd`。
3. Windows 常见路径是 `%APPDATA%\npm\lark-cli.ps1`。

这类错误不是飞书资源权限问题。权限问题会显示 bot scope 缺失，或 Base / Sheet / Wiki 没有共享给 bot。

从源码构建：

```bash
dotnet build ConfigSheetForge.sln
```

从源码运行 CLI：

```bash
dotnet run --project src/cli/ConfigSheetForge.Cli -- doctor
```

打包为本地 .NET tool：

```bash
dotnet pack src/cli/ConfigSheetForge.Cli -c Release
```

## 首次接入项目

创建本地模板：

```bash
config-sheet-forge init --lark-identity bot
```

检查本机和 provider 配置：

```bash
config-sheet-forge doctor --details
```

查找候选 root：

```bash
config-sheet-forge discover-root --query "配置根"
```

`discover-root` 只列候选项。必须由人确认正确 root，再写入 `.config-sheet-forge/config.json`。

## 注册一张表

类型行优先的推荐布局：

```bash
config-sheet-forge new-table --id items --name Items --spreadsheet "<sheet-url-or-token>" --sheet-id "<sheet-id>" --range "A1:Z500" --field-row 0 --type-row 1 --description-row 2 --data-start-row 3
```

同步并执行 gate：

```bash
config-sheet-forge sync --table items --details
config-sheet-forge gate --details --annotations github
```

## 日常分支 Source of Truth 流程

普通分支的日常流程是：

1. 在飞书在线表改数据。
2. Unity 点 `预览同步计划`。这会读取注册中心、在线 Sheet，并临时导出 xlsx 做三方检查；不会写正式 cache。
3. 如果结果是 `upToDate`，不用写 cache，继续合并预览或 PR 检查。
4. 如果结果是 `needsUpdate` 或 `missingCache`，再确认 `写入本地 cache`。
5. 如果项目使用 ExcelToSO，cache 已最新后在 Unity 窗口点 `导入 Unity 配表资产`，把 `.config-sheet-forge/excel-cache/*.xlsx` 导入 ScriptableObject asset。这一步只写 Unity asset，不写飞书、不改 registry、不写 main。

ExcelToSO 是可选 peer dependency。需要项目在 `Packages/manifest.json` 显式安装 `com.greatclock.exceltoscriptableobject`，推荐 pin 到 `https://github.com/today080221/excel_to_scriptableobject.git#v1.0.6` 或更新版本。config-sheet-forge 会使用 ExcelToSO 的 `SourceOfTruthCache` profile 导入 `.config-sheet-forge/excel-cache`，不会覆盖人工 ExcelToSO UI 使用的本地 Excel profile。若窗口提示缺少 cache profile，先点 `安装/更新 Source of Truth 导入 profile`。从 0.4.27 开始，正式 cache xlsx 的类型行会写成 ExcelToSO dialect；如果 `json` 列无法从旧 Excel 或 schema 还原为 `int[]/float[]/string[]/string`，同步会阻断并提示负责人补字段类型。

从 0.4.30 开始，SourceOfTruthCache profile 会从本地/default ExcelToSO profile 镜像目录、namespace、导入选项和 slave 表，只替换 Excel 路径到 `.config-sheet-forge/excel-cache`。如果项目没有可镜像的 setting，可以在 `ProjectSettings/*ConfigSheetForge*.json` 里提供 `unityExcelToSo.scriptDirectory`、`assetDirectory`、`namespace` 作为安全兜底；缺这些信息时工具会阻断，不会把 asset 写到 `Assets` 根目录。

5. 合并 PR 前生成合并预览、提交合并审查记录、运行 PR gate。

新建 git 分支如果还没有在线工作区，应从 PR base/main 派生当前分支在线表。这个流程叫 `bootstrap-current-branch-from-target`。`本地 Excel Seed` 只用于历史迁移，不是日常功能分支入口。

负责人执行当前分支派生 apply 前，需要先生成同输入 dry-run，然后分项确认：创建或复用在线 Sheet、写 BranchBindings / ConfigSheets、登记 SchemaReviews baseline。默认不写本地 cache、不改 `ProjectSettings`、不改 ExcelToSO，也不碰历史 OneDrive/Excel 源目录。

## 从旧本地 xlsx seed

如果项目已经有 ExcelToSO xlsx，可以先做 dry-run：

```bash
config-sheet-forge seed-from-xlsx --table ItemsData --source-xlsx "Assets/Config/ItemsData.xlsx" --dry-run --out Temp/ConfigSheetForge/seed.result.json
config-sheet-forge seed-from-xlsx --all --manifest "ProjectSettings/Example.ConfigSheetForge.json" --dry-run --out Temp/ConfigSheetForge/seed.result.json
```

dry-run 会检查公式、图片、合并单元格、富文本、跨表/跨工作簿引用、@人/@文档、日期对象、错误单元格和不支持结构，并输出每张表的 planned actions。它还会展示当前 git branch / Feishu profile 将使用或创建的 Wiki 工作区节点，例如 `项目配置表/branch-codex-config-sheet-seed-feishu-main`。它不会写飞书、不会改 cache、不会改 ProjectSettings。

apply 模式会在本地 xlsx、在线回读、在线导出 xlsx 三方语义一致后，才写 `.config-sheet-forge/excel-cache/<TableId>.xlsx`、`.config-sheet-forge/cache/<TableId>.semantic.json`、`.sha256`、项目配置和 Base 注册中心。执行 apply 必须显式确认：

```bash
config-sheet-forge seed-from-xlsx --all --manifest "ProjectSettings/Example.ConfigSheetForge.json" --yes --confirm-excel-to-so
```

策划后续只改飞书 branch 在线表时，先让项目 adapter 生成 `sync-cache` lifecycle contract，再 dry-run / apply。无变化时 semantic hash gate 不会重写 `.xlsx`、`.semantic.json` 或 `.sha256`，mtime 保持不变。

从 0.4.25 开始，Lark provider 不再依赖 `sheets +read` 的无范围默认读取，也不会盲信飞书导出 xlsx 里的 `dimension ref=A1`。`sync-cache` 会扫描实际 `sheetData` 计算 used range，再构造显式 A1 范围；如果遇到 Feishu `90202 wrong startRange`，会自动用显式范围重试，并在结果里写出表 ID、脱敏 token、sheetId、attemptedRange、retryRange、finalRange、online rows/cols、xlsx rows/cols 和错误码。这样范围问题不会再被误判成 bot 权限不足。

`sync-cache` 会从 Base 注册中心 live hydrate 当前 Git branch/profile 的 BranchBindings 与 ConfigSheets。若只是缺 MergeReviews / SchemaReviews / Waivers 的 `状态` 单选选项，先用 `config-sheet-forge registry-migrate --base <base-token> --only review-status-options --dry-run` 看窄迁移计划，确认只补状态选项后再由负责人执行 `--yes`。如果 Base 里同一 `GitBranch + Profile` 出现多条 BranchBindings，或需要清理空白默认行、字段歧义，再使用完整 `registry-migrate --dry-run` 做注册中心 schema 清理审计。

## Unity

通过 Unity Package Manager 安装：

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.4.28
```

打开 `Tools > Config Sheet Forge`。普通使用者优先看首页的 `推荐下一步` 和 [Unity 配表窗口 5 分钟说明](unity-window.md)。

第一次打开窗口时，先看首页 `推荐下一步`。普通策划通常只需要：

1. 在飞书在线表改数据。
2. 回 Unity 点 `预览同步计划`。这一步只读取，不写任何文件。
3. 预览通过后，在 `配表` 页确认并点 `写入本地 cache`。
4. 提交 PR 或找配置负责人合并。

带 `预览` 的按钮只读取，不写飞书、不改本地文件。`写入 / 创建 / 写回` 会改东西，需要勾选确认，并且必须先预览通过。

新建配表时，普通用户不用手写字段模板。窗口会让你逐行填写字段 key、中文名、类型和说明；类型会显示为“文本 / 整数 / 小数 / 是/否 / 日期 / 日期时间 / 枚举 / JSON”。选择枚举时，需要在下面填可选值。

合并 PR 时，如果本机安装并登录了 `gh`，窗口会自动使用当前 PR 的目标分支。没装或没登录时，合并页会提示原因，并提供可搜索的目标分支列表。

生成合并预览时，结果里必须能看到当前分支、目标分支、比较了几张表、报告路径和缺失项。预览通过后，点 `提交合并审查记录`，它只写 Base MergeReviews，不写 main、不写本地 cache。然后重新运行 PR 检查，缺合并审查记录的失败就应该消失。

若窗口提示“目标分支缺少工作区或在线表定位”，程序视图会出现 `初始化目标分支 main（先 dry-run）`。先看 dry-run 会创建或复用哪些在线 Sheet、会写哪些登记表；真正 apply 时要在高级入口分项确认，默认不会改 ProjectSettings、ExcelToSO settings 或本地 cache。apply 还会校验刚刚通过的同输入 dry-run result，并在完成后重新读取注册中心做 postflight；如果发现重复记录或缺表定位，会给出 record_id 和下一步。

更多 Unity 使用说明见 [Unity 配表窗口 5 分钟说明](unity-window.md)。
