# Config Sheet Forge Unity 包

这个包提供 Unity Editor 窗口，用于在 Unity 项目中使用 Config Sheet Forge 配表流程。

普通策划和项目成员请先看 [Unity 配表窗口 5 分钟说明](../../docs/unity-window.md)。这份 README 主要给接入维护者、主程和工具作者看。

打开入口：`Tools > Config Sheet Forge` 或 `Tools > Config Sheet Forge > 打开同步窗口`。

窗口采用任务型 Dashboard：第一次打开会提示“飞书在线表是正式源头，本地 Excel 是缓存”；首页会给出“推荐下一步”，通常从 `预览同步计划` 开始。预览类按钮只读取不写文件；写入、创建、写回类按钮必须勾选确认，并要求最近一次同输入预览成功。

窗口包含：

- `状态`：任务型首页，展示推荐下一步、策划改表/新建配表/合并 PR 流程卡、当前状态卡和安全说明；doctor、CLI、adapter、复制命令等放在“高级诊断”。
- `配表`：同步已有在线表，或申请新建配表。项目配置存在时会走项目 adapter 生成 lifecycle contract；否则提供 generic registry fallback。
- `合并`：按当前分支和目标分支生成合并预览；项目 adapter 模式下会自动推导 PR、共同祖先和语义输入。目标分支支持搜索，识别到 GitHub PR 时默认使用 PR base。
- `PR 检查`：合 PR 前生成检查报告；失败时优先显示中文原因和下一步。
- `输出`：查看摘要、报告和折叠的详细日志；非输出页底部默认只显示一行最近结果，展开后才是可拖拽底部抽屉。

顶部 `教程` 菜单会优先读取项目配置里的 `documentationTargets`、`localDocs`、`feishuRootUrl`，也会 fallback 到 config-sheet-forge 通用教程。

新建配表页默认是表单，而不是工程模板文本。负责人角色来自项目 `roles` 的中文显示名；字段逐行编辑，类型在 `策划视图` 里显示为文本、整数、小数、是/否、日期、日期时间、枚举、JSON。`程序视图` 会额外显示内部 key、canonical 类型、路径和命令摘要。

顶部 `高级` 开关和 `策划视图 / 程序视图` 是两回事：视图只改变信息展示口径；高级入口才解锁手动路径覆盖、raw 字段模板、手动覆盖 PR 目标分支、单表比较和输出路径等风险配置。危险写入仍然需要预览成功、勾选确认和二次确认。

合并页会做 GitHub PR 识别 preflight：git 是必需的；`gh` 可选但推荐，用于自动找到当前 PR 的目标分支。未安装或未登录 `gh` 时，窗口会提示安装 GitHub CLI 或运行 `gh auth login`，并允许手动搜索目标分支。

飞书读取依赖 `lark-cli`。Unity 子进程会自动把 Windows npm global bin 补进 `PATH`，并支持 `CONFIG_SHEET_FORGE_LARK_CLI`、`LARK_CLI_PATH` 和项目配置 `toolkit.larkCliPath`。如果 Unity 窗口提示“本机没有找到 lark-cli”，这通常是本机环境解析问题，不应显示成资源权限不足；高级诊断里会显示 Unity 看到的 PATH、识别到的 lark-cli 来源和 strict bot / user fallback 策略。

常用安全规则：

| 操作 | 风险 | 行为 |
| --- | --- | --- |
| 刷新状态 / 预览同步计划 / 预览新建配表 / 生成合并预览 / 运行 PR 检查 | 安全 | 只读取或生成报告，不写飞书、不改本地 cache |
| 写入本地 cache | 中风险 | 会更新本地 cache，需要勾选确认 |
| 创建在线表并登记 / 本地 Excel Seed apply / 确认写回 main | 高风险 | 会写飞书、项目状态或目标工作区，需要预览成功 + 勾选 + 二次确认 |

Editor assembly 引用 `ConfigSheetForge.Core`，也就是 CLI 编译的同一份语义工作簿 core。Provider 访问不放在 Unity 里，统一交给已安装的 `config-sheet-forge` CLI。

稳定菜单契约：

- `Tools > Config Sheet Forge`
- `Tools > Config Sheet Forge > 打开同步窗口`
- `Tools > Config Sheet Forge > 新建配表向导`
- `Tools > Config Sheet Forge > 本地 Excel Seed`
- `Tools > Config Sheet Forge > 同步在线 Cache`
- `Tools > Config Sheet Forge > 三方比较与合并`
- `Tools > Config Sheet Forge > PR 同步检查`

稳定 Editor API：

- `ConfigSheetForgeEditorApi.OpenStatusWindow()`
- `ConfigSheetForgeEditorApi.OpenNewTableWizard()`
- `ConfigSheetForgeEditorApi.OpenSeedFromLocalXlsx()`
- `ConfigSheetForgeEditorApi.OpenSyncCache()`
- `ConfigSheetForgeEditorApi.OpenCompareMerge()`
- `ConfigSheetForgeEditorApi.OpenPrGate()`

下游 Unity 项目推荐只保留薄菜单 adapter 和项目 config。通用窗口、向导、contract 执行、三方比较、gate UI 都由本包维护；项目 adapter 只负责把项目配置转成 lifecycle contract。`.config-sheet-forge` 是 gitignored 本地状态/cache，可忽略、可重建，不作为共享项目配置是否完整的判断依据。

项目 adapter 模式会把窗口输入写入 `Temp/ConfigSheetForge/unity-lifecycle/<operation>.inputs.json`，并向 adapter 传 `--inputs <path>`，避免把字段模板等复杂数据塞进长 inline JSON 参数。`pr-gate-report` 会额外生成标准 `Temp/ConfigSheetForge/pr-gate-report.json`，该文件是 `PrGateReport` 本体，供项目 gate wrapper / CI 直接读取。

项目可以在 `ProjectSettings/*ConfigSheetForge*.json` 中声明帮助入口：

```json
{
  "documentationTargets": {
    "5 分钟入门": "docs/tooling/config-sheet-source-of-truth.md",
    "飞书项目配置表": "https://example.feishu.cn/wiki/xxxxx"
  },
  "localDocs": [
    "docs/tooling/designer-config-flow.md"
  ],
  "feishuRootUrl": "https://example.feishu.cn/wiki/xxxxx"
}
```

项目还可以提供角色、新建配表默认字段和 GitHub 帮助入口；这些字段都是可选的，旧项目不需要立即迁移：

```json
{
  "roles": {
    "tableOwner": { "displayName": "配表负责人", "canRequestMerge": true },
    "schemaReviewer": { "displayName": "Schema 审查人", "canApproveSchemaReview": true },
    "configOwner": { "displayName": "配置负责人", "canApproveWaiver": true, "canApproveMainWriteBack": true }
  },
  "newTable": {
    "defaultOwnerRole": "tableOwner",
    "supportedFieldTypes": ["string", "integer", "number", "bool", "date", "datetime", "enum", "json"],
    "defaultFields": [
      { "key": "id", "displayName": "ID", "valueKind": "string", "description": "唯一ID", "isPrimary": true },
      { "key": "name", "displayName": "名称", "valueKind": "string", "description": "显示名称" }
    ]
  },
  "github": {
    "installHelpUrl": "https://cli.github.com/",
    "requiredForPrAutoDetect": false
  },
  "toolkit": {
    "larkCliPath": "C:/Users/<you>/AppData/Roaming/npm/lark-cli.ps1",
    "larkCliEnvironmentVariable": "CONFIG_SHEET_FORGE_LARK_CLI"
  }
}
```

## Unity lifecycle adapter contract

当 Unity 窗口发现 `ProjectSettings/*ConfigSheetForge*.json` 且配置了 `adapterScript`、`contractCommand` 或 `contractArgs` 时，会进入项目 adapter 模式：

1. Unity 先写入 UTF-8 无 BOM 的 `Temp/ConfigSheetForge/unity-lifecycle/<operation>.inputs.json`。
2. Unity 调用项目 adapter，并传入 `--inputs <path>`。如果项目自定义了 `contractArgs` 且没有显式包含 `--inputs` 或 `{inputs}`，Unity 会自动补上。
3. 项目 adapter 读取 inputs JSON，生成 lifecycle contract request。
4. Unity 调用 `config-sheet-forge apply-contract --request <contract.json> --out <result.json>`。
5. `pr-gate-report` 额外传 `--report <path>`，该路径必须写入 `PrGateReport` 本体 JSON，不要外包 `LifecycleContractResult`。

inputs JSON 至少包含这些字段：

- `operation`
- `dryRun`
- `gitBranch`
- `feishuProfile`
- `branchWikiNodeTitle`
- `branchWikiNodeUrl`
- `tableId`
- `title`
- `displayName`
- `ownerRole`
- `schemaChangeSummary`
- `excelPath`
- `sheetName`
- `fields`
- `basePath`
- `oursPath`
- `theirsPath`
- `sourceBranch`
- `targetBranch`
- `targetFeishuProfile`
- `targetBranchWikiNodeTitle`
- `targetBranchWikiNodeUrl`
- `targetBranchWikiNodeToken`
- `mergeBase`
- `githubRepository`
- `prNumber`
- `prUrl`
- `allowPrAutoDetect`
- `mergeReportPath`
- `mergedPath`
- `writeBackToMain`
- `confirmWriteMain`
- `confirmApply`
- `confirmExcelToSoSettingsUpdate`
- `gateReportPath`

接入时注意：Unity UPM 重新 resolve `packages-lock.json` 时，可能顺带刷新其它 git dependency 的 hash。这通常不是 Config Sheet Forge 包本身的改动；请在 PR 里单独核对 manifest/lock diff。

## 安装

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.4.15
```

## 测试

包内包含 edit-mode tests，覆盖共享 core hash、命令构造、配置路径和人话错误渲染。
