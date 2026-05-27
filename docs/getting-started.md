# 入门指南

Config Sheet Forge 会在项目根目录下维护本地配置和表注册表。它们位于 `.config-sheet-forge/`，可能包含租户文档 ID 或私有 URL，因此默认不进 git。

## 安装要求

- .NET 8 SDK 或更新版本，用于 CLI。
- `lark-cli`，用于飞书/Lark provider。
- Unity 2021.3 或更新版本，用于 UPM 包。
- git，用于识别当前分支和 merge-base。
- GitHub CLI `gh` 可选但推荐。它只用于 Unity 合并页自动识别当前 PR；没装时仍可手动选择目标分支。

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

`sync-cache` 会从 Base 注册中心 live hydrate 当前 Git branch/profile 的 BranchBindings 与 ConfigSheets。若 Base 里同一 `GitBranch + Profile` 出现多条 BranchBindings，会阻断并列出 record_id；先用 `config-sheet-forge registry-migrate --base <base-token> --dry-run` 审计重复行、空白默认行和字段歧义，确认后再做 cleanup。

## Unity

通过 Unity Package Manager 安装：

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.4.17
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

生成合并预览时，结果里必须能看到当前分支、目标分支、比较了几张表、报告路径和缺失项。若窗口提示“目标分支缺少工作区或在线表定位”，程序视图会出现 `初始化目标分支 main（先 dry-run）`。先看 dry-run 会创建或复用哪些在线 Sheet、会写哪些登记表；真正 apply 时要在高级入口分项确认，默认不会改 ProjectSettings、ExcelToSO settings 或本地 cache。

更多 Unity 使用说明见 [Unity 配表窗口 5 分钟说明](unity-window.md)。
