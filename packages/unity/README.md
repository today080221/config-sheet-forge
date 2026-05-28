# Config Sheet Forge Unity 包

这个包提供 Unity Editor 入口，用于在 Unity 项目中使用 Config Sheet Forge 配表流程。

普通策划和项目成员请先看 [Unity 配表窗口 5 分钟说明](../../docs/unity-window.md)。这份 README 主要给接入维护者、主程和工具作者看。

打开入口：`Tools > Config Sheet Forge` 或 `Tools > Config Sheet Forge > 打开同步窗口`。

从 0.4.29 开始，官方主工作台迁移到 Tauri Desktop。Unity 默认窗口降级为 thin bridge：打开 Desktop、安装/更新 `SourceOfTruthCache` profile、导入 Unity 配表资产、运行/读取 PR gate、查看最近结果。原来的完整 Unity IMGUI 工作流仍然保留，但移动到 `Tools > Config Sheet Forge > Legacy`，用于没有 Desktop、CI 调试或救急 fallback。

Desktop 工作台文档见 [docs/desktop-workbench.md](../../docs/desktop-workbench.md)。长网络任务（sync-cache、导出 xlsx、三方一致性、compare-merge、PR gate）推荐从 Desktop 跑；Unity 里真正必须执行的是最后的 ExcelToSO `ImportByProfile(SourceOfTruthCache)` 导入 Unity asset。

Legacy 窗口仍采用任务型 Dashboard：第一次打开会提示“飞书在线表是正式源头，本地 Excel 是缓存”；首页会给出“推荐下一步”，通常从 `预览同步计划` 开始。预览类按钮只读取不写文件；写入、创建、写回类按钮必须勾选确认，并要求最近一次同输入预览成功。

从 0.4.23 开始，Unity 状态页把 Feishu Base 注册中心当作 live locator 的 Source of Truth。`ProjectSettings/*ConfigSheetForge*.json` 可以只保存表 ID、分支/profile 规则、路径和治理配置，不需要保存每张表的 `SpreadsheetToken` / `SheetId`。窗口会后台运行只读 `registry-status`，用 BranchBindings + ConfigSheets 判断“当前分支是否已登记在线表”。

从 0.4.25 开始，`sync-cache` 读取在线 Sheet 时不再依赖 `sheets +read` 的无范围默认读取，也不会盲信飞书导出 xlsx 里的错误 `dimension ref=A1`。Lark provider 会扫描实际 `sheetData` 构造显式 A1 范围，遇到 `90202 wrong startRange` 会自动 retry，并把 attemptedRange、retryRange、finalRange、sheetId、脱敏 token、online rows/cols 和 xlsx rows/cols 写进诊断，避免把范围/形状问题误报成权限问题。

从 0.4.26 开始，Unity 窗口可以继续完成 `本地 cache xlsx -> Unity ScriptableObject asset` 这一步。项目显式安装 ExcelToSO 后，`配表` 页会出现 `导入 Unity 配表资产`。它只写 Unity asset，不写飞书、不改在线表、不改 registry、不写 main。

从 0.4.27 开始，Source of Truth 的 semantic JSON 仍使用 `integer/number/json` 等 canonical 类型做 hash，但正式 `.config-sheet-forge/excel-cache/*.xlsx` 会写成 ExcelToSO 可导入的类型行，例如 `int/float/string[]`。如果某列是 `json`，工具会尝试从旧 Excel 类型或字段 `originalType/excelToSoType` 还原为 `int[]`、`float[]`、`string[]` 或 `string`；无法还原时会阻断并给中文修复建议，不会生成会让 ExcelToSO 弹错的 cache。

从 0.4.28 开始，ExcelToSO 需要 v1.0.6 或更新版本。config-sheet-forge 会显式使用 ExcelToSO 的 `SourceOfTruthCache` profile 导入 `.config-sheet-forge/excel-cache/*.xlsx`，不会再把 ExcelToSO 的本地/default profile 改到 cache 路径。窗口里的 `安装/更新 Source of Truth 导入 profile` 只新增或更新 cache profile；人工打开 ExcelToSO UI 时仍默认看到 `本地 Excel` profile，继续指向 `Excel/*.xlsx` / OneDrive 工作流。

从 0.4.29 开始，CLI 增加 `repair-cache-dialect`：当 semantic/hash 已经最新，但 cache xlsx 物理类型行仍是 `integer/number/json` 等旧 dialect 时，可以不联网快速预览/修复。它只重写 `.config-sheet-forge/excel-cache/*.xlsx` 的类型行，不改 semantic/hash、不写飞书、不写旧 `Excel/`。无法把 `json` 还原成 `int[]/float[]/string[]/string` 时会中文阻断。

从 0.4.30 开始，`安装/更新 SourceOfTruthCache profile` 会镜像现有本地/default ExcelToSO profile：保留 `script_directory`、`asset_directory`、`name_space`、导入选项和 `slaves`，只把主表与 slave 的 `excel_name` 改到 `.config-sheet-forge/excel-cache`。如果没有可镜像的 setting，才使用项目配置里的 `table.scriptDirectory / assetDirectory / namespace` 或 `unityExcelToSo` 默认值；仍缺关键目录或 namespace 时会阻断，避免把 asset 写到 `Assets` 根目录。

Legacy 完整窗口包含：

- `状态`：任务型首页，展示推荐下一步、策划改表/新建配表/合并 PR 流程卡、当前状态卡和安全说明；doctor、CLI、adapter、复制命令等放在“高级诊断”。
- `配表`：同步已有在线表，或申请新建配表。`sync-cache` dry-run 会读取注册中心、临时读取/导出在线 Sheet 并做三方一致性检查；只有 apply 且确认后才写正式本地 cache。
- `合并`：按当前分支和目标分支生成合并预览；项目 adapter 模式下会自动推导 PR、共同祖先和语义输入。目标分支支持搜索，识别到 GitHub PR 时默认使用 PR base。有效预览会列出 source/target 工作区、tableCount、报告路径和缺失项；缺目标分支或表定位时会失败而不是空成功。预览通过后可点 `提交合并审查记录`，只写 Base `MergeReviews`，不写 main、不写本地 cache。
- `PR 检查`：合 PR 前生成检查报告；失败时优先显示中文原因和下一步。缺合并审查记录、Schema 审查、waiver 时会给可执行入口，而不是只让用户手工找 Base。
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
| 导入 Unity 配表资产 | 中风险 | 只写 Unity ScriptableObject asset；要求最近一次同步预览通过且 cache 已最新 |
| 安装/更新 Source of Truth 导入 profile | 中风险 | 只在 `ProjectSettings/ExcelToScriptableObjectSettings.asset` 中维护 `SourceOfTruthCache` profile，不改变本地 Excel profile，不会写旧 Excel |
| 创建在线表并登记 / 本地 Excel Seed apply / 确认写回 main | 高风险 | 会写飞书、项目状态或目标工作区，需要预览成功 + 勾选 + 二次确认 |
| 初始化目标分支 | 高风险 | 先 dry-run；apply 必须校验同输入 dry-run result，再拆分确认在线 Sheet、Base 注册、SchemaReviews、本地 cache、ProjectSettings、ExcelToSO |
| 从目标分支初始化当前分支 | 中/高风险 | 新功能分支缺在线工作区时使用；先 dry-run，apply 前分项确认在线 Sheet、注册中心和 SchemaReviews，默认不写本地 cache / ProjectSettings / ExcelToSO，避免误用历史 Excel Seed |
| 提交合并审查记录 | 中风险 | 只写 Base MergeReviews，不写 main、不写 cache；必须最近一次合并预览通过 |

Editor assembly 引用 `ConfigSheetForge.Core`，也就是 CLI 编译的同一份语义工作簿 core。Provider 访问不放在 Unity 里，统一交给已安装的 `config-sheet-forge` CLI。

## 首次初始化目标分支

当 `compare-merge` 提示目标分支（通常是 `main`）缺 BranchBindings 或 ConfigSheets 定位时，程序视图会出现 `初始化目标分支 main（先 dry-run）`。推荐流程：

1. 先生成 dry-run，确认目标节点、每张表的本地 xlsx 来源、目标在线 Sheet 标题，以及会写哪些 Base 表。
2. apply 前只勾选 `在线 Sheet`、`BranchBindings / ConfigSheets`、`SchemaReviews baseline`。本地 cache、ProjectSettings、ExcelToSO 默认不勾。
3. Unity 会把刚刚通过的 dry-run result 传给 CLI；CLI 会校验 `requestFingerprint`，输入不一致会拒绝写入。
4. apply 完成后会 postflight：重新读 BranchBindings / ConfigSheets / SchemaReviews，确认每张表都有 SpreadsheetToken + SheetId，并确认在线回读、xlsx 导出和三方一致性检查完成。
5. 如果中途失败，修复权限或重复记录后可以重跑；同一目标节点下已有同名在线 Sheet 会被复用，重复 registry 记录会列出 record_id 并阻断。

## 人工合并审查闭环

当 `compare-merge` dry-run 成功后，Unity 会记住这次预览的输入指纹。此时合并页会启用 `提交合并审查记录`：

1. 先看合并预览和报告，确认表范围、目标分支和冲突情况。
2. 点 `提交合并审查记录`。
3. 二次确认后，CLI 会再次读取最近一次 compare dry-run result，并校验 source branch、target branch、tableIds、PR 信息和报告路径一致。
4. 校验通过才写 Base `MergeReviews`；写入内容包括审查 ID、配表范围、Git 分支、状态、ApproverRole 和更新时间。
5. 如果注册中心的 `MergeReviews.状态` 单选字段还没有 `approved`、`completed`、`passed` 等选项，提交会提前阻断，并提示先运行窄迁移：`registry-migrate --only review-status-options --dry-run`，确认后再 `--yes`。
6. 重新运行 PR 检查后，缺 MergeReviews 的失败应消失；如果还有 SchemaReviews 或 waiver 问题，PR 检查页会给对应处理入口。

这一步不会写回 main，不会写本地 cache，也不会改 ProjectSettings 或 ExcelToSO settings。

## 从目标分支初始化当前分支

普通新功能分支缺 BranchBindings / ConfigSheets 时，不应该让用户去做 `本地 Excel Seed`。Unity 会推荐 `从 main 初始化当前分支在线表（先预览）`，底层命令是 `bootstrap-current-branch-from-target`：

1. dry-run 只读取目标分支注册中心和在线表定位，列出将复制/复用的表范围。
2. apply 必须传最近一次同输入 dry-run result，并校验 `requestFingerprint`。
3. apply 至少分项确认 `confirm-create-online-sheets`、`confirm-registry-upsert`、`confirm-schema-reviews`。
4. 默认不写本地 cache、不改 ProjectSettings、不改 ExcelToSO，不碰历史 Excel 源路径。
5. 完成后 postflight 会确认当前分支有且只有一个 BranchBindings，所有表都有 SpreadsheetToken + SheetId；如果后续 sync-cache dry-run 仍阻断，会列出 blockedTables。

如果负责人已经批准了有效 waiver，PR 检查页会显示 `已由配置负责人 waiver 临时放行`，同时保留 record_id、过期时间和原因，方便 CI 和人工复核。这和“缺合并审查记录”是两种不同状态。

稳定菜单契约：

- `Tools > Config Sheet Forge`
- `Tools > Config Sheet Forge > 打开同步窗口`
- `Tools > Config Sheet Forge > 打开 Desktop 工作台`
- `Tools > Config Sheet Forge > 安装或更新 SourceOfTruthCache profile`
- `Tools > Config Sheet Forge > 导入 Unity 配表资产`
- `Tools > Config Sheet Forge > 运行 PR 检查`
- `Tools > Config Sheet Forge > Legacy > 完整 Unity 工作台`
- `Tools > Config Sheet Forge > Legacy > 新建配表向导`
- `Tools > Config Sheet Forge > Legacy > 本地 Excel Seed`
- `Tools > Config Sheet Forge > Legacy > 同步在线 Cache`
- `Tools > Config Sheet Forge > Legacy > 三方比较与合并`
- `Tools > Config Sheet Forge > Legacy > PR 同步检查`

稳定 Editor API：

- `ConfigSheetForgeEditorApi.OpenStatusWindow()`
- `ConfigSheetForgeEditorApi.OpenNewTableWizard()`
- `ConfigSheetForgeEditorApi.OpenSeedFromLocalXlsx()`
- `ConfigSheetForgeEditorApi.OpenSyncCache()`
- `ConfigSheetForgeEditorApi.OpenCompareMerge()`
- `ConfigSheetForgeEditorApi.OpenPrGate()`
- `ConfigSheetForgeEditorApi.OpenLegacyStatusWindow()`
- `ConfigSheetForgeEditorApi.OpenLegacySyncCache()`
- `ConfigSheetForgeEditorApi.OpenLegacyCompareMerge()`
- `ConfigSheetForgeEditorApi.OpenLegacyPrGate()`

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
- `targetGitBranch`
- `targetProfile`
- `sourceMode`
- `tableIds`
- `confirmCreateOnlineSheets`
- `confirmRegistryUpsert`
- `confirmSchemaReviews`
- `confirmWriteLocalCache`
- `confirmWriteProjectConfig`
- `confirmExcelToSoSettings`
- `gateReportPath`

接入时注意：Unity UPM 重新 resolve `packages-lock.json` 时，可能顺带刷新其它 git dependency 的 hash。这通常不是 Config Sheet Forge 包本身的改动；请在 PR 里单独核对 manifest/lock diff。

## 安装

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.4.28
```

## 测试

包内包含 edit-mode tests，覆盖共享 core hash、命令构造、配置路径和人话错误渲染。
