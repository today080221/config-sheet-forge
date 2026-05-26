# 入门指南

Config Sheet Forge 会在项目根目录下维护本地配置和表注册表。它们位于 `.config-sheet-forge/`，可能包含租户文档 ID 或私有 URL，因此默认不进 git。

## 安装要求

- .NET 8 SDK 或更新版本，用于 CLI。
- `lark-cli`，用于飞书/Lark provider。
- Unity 2021.3 或更新版本，用于 UPM 包。

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
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.4.10
```

打开 `Tools > Config Sheet Forge`。Unity 窗口会使用同一份共享 core 做本地检查，provider 访问仍交给已安装的 CLI。
