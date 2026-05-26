# Config Sheet Forge

Config Sheet Forge 是一个面向 Unity 配表工作流的开源工具。它把飞书/Lark 电子表格当作可审查、可合并、可 gate 的配置源，同时避免把团队链接、负责人路由、真实业务表内容或 secret 写入仓库。

它包含：

- `packages/unity`：Unity UPM 包。
- `src/cli`：跨平台 .NET CLI。
- `src/core`：CLI 与 Unity 共用的语义工作簿 core。
- `src/providers/lark`：基于 `lark-cli` 的飞书/Lark provider。

## 快速开始

```bash
dotnet build ConfigSheetForge.sln
dotnet run --project src/cli/ConfigSheetForge.Cli -- init --lark-identity bot
dotnet run --project src/cli/ConfigSheetForge.Cli -- doctor
dotnet run --project src/cli/ConfigSheetForge.Cli -- discover-root --query "配置根"
```

确认 root 后注册表：

```bash
dotnet run --project src/cli/ConfigSheetForge.Cli -- new-table --id items --name Items --spreadsheet "<sheet-url-or-token>" --sheet-id "<sheet-id>" --range "A1:Z500" --field-row 0 --type-row 1 --description-row 2 --data-start-row 3
dotnet run --project src/cli/ConfigSheetForge.Cli -- sync --table items
dotnet run --project src/cli/ConfigSheetForge.Cli -- gate --annotations github
```

本地状态写入 `.config-sheet-forge/`，该目录已被 git 忽略。

`sync` 会先导出到临时目录，完成 portable subset 检查和在线读取 / xlsx 导出 / 语义归一化三方一致性比较后，才更新正式 cache。semantic hash 没变时不会重写 `.xlsx`、`.semantic.json` 或 `.sha256`。

一次性迁移旧 ExcelToSO xlsx 到在线 Sheet Source of Truth：

```bash
dotnet run --project src/cli/ConfigSheetForge.Cli -- seed-from-xlsx --table ItemsData --source-xlsx "Assets/Config/ItemsData.xlsx" --dry-run
dotnet run --project src/cli/ConfigSheetForge.Cli -- seed-from-xlsx --all --manifest "ProjectSettings/Example.ConfigSheetForge.json" --dry-run
```

`seed-from-xlsx` 的 dry-run 会读取本地 xlsx 做便携子集预检和 semantic normalize，并按当前 git branch / Feishu profile 预览目标 Wiki 工作区节点，例如 `项目配置表/branch-codex-config-seed`；不会写飞书、不会改本地 cache、不会改 ProjectSettings。apply 模式必须显式传 `--yes`；如果要更新 ExcelToSO settings，还必须传 `--confirm-excel-to-so`。

长期同步在线表到本地 cache 可走 branch-aware lifecycle：

```bash
dotnet run --project src/cli/ConfigSheetForge.Cli -- sync-cache --manifest "ProjectSettings/Example.ConfigSheetForge.json" --dry-run --out Temp/ConfigSheetForge/sync-cache.result.json
dotnet run --project src/cli/ConfigSheetForge.Cli -- apply-contract --request sync-cache.contract.json --out sync-cache.result.json
```

## Contract lifecycle

项目 adapter 可以用 JSON contract 调通用生命周期入口：

```bash
dotnet run --project src/cli/ConfigSheetForge.Cli -- apply-contract --request contract.json --out result.json
dotnet run --project src/cli/ConfigSheetForge.Cli -- registry-migrate --base "<base-token>" --locale zh-Hans --cleanup-default-rows --cleanup-default-fields --dry-run
```

core 支持 `bootstrap-registry`、`new-table`、`seed-from-local-xlsx`、`bootstrap-from-local-xlsx`、`sync-cache`、`compare-merge`、`pr-gate-report` 这些 operation。Base 和字段会保留 machine key 到中文显示名的映射，程序逻辑不依赖中文字段名。

branch/profile 工作区由 contract 的 `branchWorkspace` 和 `branchBindings` 驱动。`requireOneToOneBinding=true` 时，同一 Git 分支不能绑定多个 Feishu profile，同一 profile 也不能被多个 Git 分支复用；冲突会阻断 seed/sync/merge/gate，并输出中文修复建议。

`sync-cache` 会在配置 Base 注册中心时从 live BranchBindings / ConfigSheets hydrate 当前 branch/profile 的在线表定位；如果检测到同一 `GitBranch + Profile` 多条 BranchBindings，会阻断并列出 record_id。清理前先跑 `config-sheet-forge registry-migrate --base <base-token> --dry-run` 审计重复行、空白默认行和中英文重复字段；执行删除类 cleanup 必须额外传 `--yes`。

## Unity UPM 安装

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.4.11
```

安装后打开 `Tools > Config Sheet Forge` 或 `Tools > Config Sheet Forge > 打开同步窗口`。下游 Unity 项目推荐只保留薄菜单 adapter 和 `ProjectSettings/*ConfigSheetForge*.json` 项目配置，通用窗口、向导、contract 执行、三方比较和 gate UI 都由本包维护。

Unity 项目 adapter 模式会通过 `Temp/ConfigSheetForge/unity-lifecycle/<operation>.inputs.json` 传窗口输入，并生成标准 `Temp/ConfigSheetForge/pr-gate-report.json` 给项目 gate wrapper / CI 使用。

`*.inputs.json` 使用 UTF-8 无 BOM。Unity 会向项目 adapter 传 `--inputs <path>`；adapter 读取 inputs 后输出 lifecycle contract，再由 `apply-contract` 生成 `LifecycleContractResult`。`pr-gate-report` 的 `--report` 路径写入纯 `PrGateReport` JSON。

Unity UPM 重新 resolve `packages-lock.json` 时，可能顺带刷新其它 git dependency 的 hash；接入 PR 里应单独核对 manifest/lock diff。

## 类型行约定

v0.2.0 支持显式类型行优先的导入方式，默认与 `excel_to_scriptableobject` 的常见布局对齐：

- 第 0 行：字段名。
- 第 1 行：字段类型。
- 第 2 行：字段说明。
- 第 3 行起：数据。

支持的便携类型包括 `string`、`number`、`integer`、`bool`、`date`、`datetime`、`json`、`enum`。缺少类型行时会做保守自动推断，并把有歧义或有损的值作为 warning 报告。

## 安全规则

- 不提交 access token、app secret、真实团队链接、私有业务表内容或 owner routing。
- `discover-root` 只推荐候选 root，不会静默选择。
- 面向非程序用户的错误要说明怎么修；hash、revision、raw JSON 等放到 details。
- 项目特定值放在本地 config、环境变量或 GitHub secrets。

## 文档

- [入门指南](docs/getting-started.md)
- [配置说明](docs/configuration.md)
- [给非程序用户的说明](docs/human-guide.md)
- [便携子集](docs/portable-subset.md)
- [合并策略](docs/merge-policy.md)
- [CI gate](docs/ci-gate.md)
- [Lark provider](docs/providers/lark.md)

## License

Apache-2.0。
