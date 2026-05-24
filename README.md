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

## Unity UPM 安装

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.2.0
```

安装后打开 `Tools > Config Sheet Forge`。

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
