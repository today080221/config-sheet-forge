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

## Contract lifecycle

项目 adapter 可以用 JSON contract 调通用生命周期入口：

```bash
dotnet run --project src/cli/ConfigSheetForge.Cli -- apply-contract --request contract.json --out result.json
dotnet run --project src/cli/ConfigSheetForge.Cli -- registry-migrate --base "<base-token>" --locale zh-Hans --cleanup-default-rows --cleanup-default-fields --dry-run
```

core 支持 `bootstrap-registry`、`new-table`、`sync-cache`、`compare-merge`、`pr-gate-report` 这些 operation。Base 和字段会保留 machine key 到中文显示名的映射，程序逻辑不依赖中文字段名。

## Unity UPM 安装

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.3.0
```

安装后打开 `Tools > Config Sheet Forge` 或 `Tools > Config Sheet Forge > 打开同步窗口`。下游 Unity 项目推荐只保留薄菜单 adapter 和 `ProjectSettings/*ConfigSheetForge*.json` 项目配置，通用窗口、向导、contract 执行、三方比较和 gate UI 都由本包维护。

Unity 项目 adapter 模式会通过 `Temp/ConfigSheetForge/unity-lifecycle/<operation>.inputs.json` 传窗口输入，并生成标准 `Temp/ConfigSheetForge/pr-gate-report.json` 给项目 gate wrapper / CI 使用。

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
