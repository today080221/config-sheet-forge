# 配置说明

Config Sheet Forge 有两个本地文件：

- `.config-sheet-forge/config.json`
- `.config-sheet-forge/registry.json`

它们由 `config-sheet-forge init` 生成，并被 git 忽略。

## config.json

```json
{
  "schemaVersion": "1",
  "provider": "lark",
  "rootUrl": "",
  "rootToken": "",
  "rootObjectType": "",
  "registryPath": ".config-sheet-forge/registry.json",
  "cacheDirectory": ".config-sheet-forge/cache",
  "providerSettings": {
    "larkCliPath": "lark-cli",
    "larkCliIdentity": "bot",
    "larkAllowUserFallback": "false"
  }
}
```

`rootUrl` 适合保存人能核对的 URL。`rootToken` 只在 provider 无法通过 URL 工作时使用。真实租户资源不要提交。

`larkCliIdentity` 支持：

- `bot`：默认值，使用机器人身份。默认严格模式下不会 fallback 到 user。
- `user`：直接使用当前用户 OAuth 身份。
- `default`：不向 `lark-cli` 传 `--as`。

`larkAllowUserFallback` 默认为 `false`。只有显式设为 `true`，或命令行传 `--allow-user-fallback`，provider 才会在 bot 权限失败后尝试 user 身份。

## registry.json

```json
{
  "schemaVersion": "1",
  "tables": [
    {
      "id": "items",
      "name": "Items",
      "provider": "lark",
      "spreadsheet": "",
      "sheetId": "",
      "range": "A1:Z500",
      "fieldRow": 0,
      "typeRow": 1,
      "descriptionRow": 2,
      "dataStartRow": 3,
      "treatUnknownTypesAsEnum": false,
      "localSourcePath": ""
    }
  ]
}
```

`fieldRow`、`typeRow`、`descriptionRow`、`dataStartRow` 都是从 0 开始的行号。`typeRow = -1` 表示没有显式类型行，此时会保守自动推断。

## seed-from-xlsx manifest 字段

`seed-from-xlsx --all --manifest <project-config-or-contract>` 可以读取项目配置或 lifecycle contract。项目配置里的表对象建议至少提供：

```json
{
  "tables": [
    {
      "id": "ItemsData",
      "displayName": "道具表",
      "sourceXlsxPath": "Assets/ExcelToSO/ItemsData.xlsx",
      "cacheXlsxPath": ".config-sheet-forge/excel-cache/ItemsData.xlsx",
      "sheetName": "Items",
      "fieldRow": 0,
      "typeRow": 1,
      "descriptionRow": 2,
      "dataStartRow": 3
    }
  ],
  "excelCacheDirectory": ".config-sheet-forge/excel-cache",
  "semanticCacheDirectory": ".config-sheet-forge/cache",
  "wikiRootToken": "",
  "branchWorkspaceRootWikiUrl": "https://example.feishu.cn/wiki/...",
  "baseToken": "",
  "registry": {
    "tableIds": {
      "ConfigSheets": "tbl...",
      "BranchBindings": "tbl...",
      "SchemaReviews": "tbl..."
    }
  },
  "branchWorkspace": {
    "rootWikiToken": "",
    "rootWikiUrl": "https://example.feishu.cn/wiki/...",
    "rootWikiTitle": "项目配置表",
    "gitBranch": "codex/config-sheet-seed-feishu-main",
    "profileNameTemplate": "{gitBranch}",
    "branchNodeTitleTemplate": "branch-{slug}",
    "mainGitBranch": "main",
    "mainFeishuBranch": "main",
    "createIfMissing": true,
    "requireOneToOneBinding": true,
    "bindingRegistryTable": "BranchBindings"
  }
}
```

已有 `spreadsheetToken`/`spreadsheetUrl`/`sheetId` 时，seed apply 会先验证并复用在线 Sheet，不会创建重复表。dry-run 输出和 apply result 都包含 `seedTables`，可作为 Unity 窗口展示和失败后 resume 的依据。直接读取项目配置时，Base 表 ID 必须来自 `registry.tableIds` 或 `feishu.registryBase.tables` 的 machine key 映射；不要把中文显示名当成 `table_id`。

`excelPath` 在项目配置里通常表示本地 cache 或 ExcelToSO 使用路径，`seed-from-xlsx --all --manifest` 不会把它当成旧源 xlsx。旧 Excel 源必须显式写在 `sourceXlsxPath`、`sourceXlsx`、`oldExcelPath` 或 `localSourcePath`。

## Branch Workspace

branch/profile 工作区用于避免不同 git 分支把在线 Sheet 都挂到 Wiki 根节点。推荐由项目配置或 contract 提供：

```json
{
  "branchWorkspace": {
    "mode": "git-branch-to-feishu-branch-profile",
    "rootWikiToken": "<项目配置表 wiki token>",
    "rootWikiUrl": "https://example.feishu.cn/wiki/...",
    "rootWikiTitle": "项目配置表",
    "gitBranch": "feature/config-balance",
    "feishuBranch": "",
    "profile": "",
    "mainGitBranch": "main",
    "mainFeishuBranch": "main",
    "profileNameTemplate": "{gitBranch}",
    "branchNodeTitleTemplate": "branch-{slug}",
    "mainNodeTitle": "main",
    "createIfMissing": true,
    "requireOneToOneBinding": true,
    "bindingRegistryTable": "BranchBindings"
  },
  "branchBindings": [
    {
      "gitBranch": "feature/config-balance",
      "profile": "feature/config-balance",
      "wikiNodeToken": "wik...",
      "wikiNodeUrl": "https://...",
      "status": "active"
    }
  ]
}
```

非 main 分支会使用稳定 slug，例如 `feature/config-balance` -> `branch-feature-config-balance`。`requireOneToOneBinding=true` 时，同一 Git 分支不能对应多个 profile，同一 profile 不能被多个 Git 分支复用；冲突会阻断 lifecycle，并给出中文修复建议。

Base 注册中心建议包含这些字段：

- `BranchBindings`：`GitBranch`、`FeishuBranch`、`Profile`、`WikiNodeToken`、`WikiNodeUrl`、`Status`、`CreatedBy`、`OwnerRole`、`UpdatedAt`。
- `ConfigSheets`：`TableId`、`DisplayName`、`Branch`/`Profile`、`WikiNodeToken`、`SpreadsheetToken`、`SheetId`、`OnlineSheetUrl`、`ExcelPath`、`SemanticHash`、`Status`、`OwnerRole`、`SchemaReviewRequired`、`UpdatedAt`。

`registry-migrate` 会补齐这些 machine key 对应的中文字段显示名，并保留已有数据。

## 目标分支初始化

当 `compare-merge` 提示目标分支（通常是 `main`）缺少工作区或在线表定位时，不要手动改 lifecycle contract。使用 `bootstrap-target-branch-from-local-xlsx` 先生成 dry-run：

```bash
config-sheet-forge bootstrap-target-branch-from-local-xlsx --all --manifest "ProjectSettings/Example.ConfigSheetForge.json" --target-branch main --dry-run --out Temp/ConfigSheetForge/bootstrap-main.dry-run.json
```

它会把目标 git branch/profile/wiki node title 显式切到目标分支，并从项目配置的 `sourceXlsxPath` 读取本地 xlsx，计划创建或复用目标分支在线 Sheet。v1 支持 `sourceMode=local-xlsx`。

apply 不能用一个 `--yes` 覆盖所有写入，必须按写入对象分开确认：

```bash
config-sheet-forge bootstrap-target-branch-from-local-xlsx --all --manifest "ProjectSettings/Example.ConfigSheetForge.json" --target-branch main \
  --preview-result Temp/ConfigSheetForge/bootstrap-main.dry-run.json \
  --confirm-create-online-sheets \
  --confirm-registry-upsert \
  --confirm-schema-reviews
```

可选确认：

- `--confirm-write-local-cache`：允许写 `.config-sheet-forge/cache` 和 `excel-cache`。
- `--confirm-write-project-config`：允许回填 `ProjectSettings/*ConfigSheetForge*.json`。
- `--confirm-excel-to-so`：允许更新 ExcelToSO settings。

不传这些可选确认时，result 会把对应写入标为 `skipped`，不会改本地 cache、ProjectSettings 或 ExcelToSO settings。apply 还会校验 dry-run result 里的 `requestFingerprint`；如果 tableIds、target branch/profile、xlsx 来源等输入变了，会直接拒绝写入。

初始化成功后，CLI 会做 postflight：重新读取 BranchBindings、ConfigSheets、SchemaReviews，确认目标分支每张表都有 SpreadsheetToken + SheetId，并确认 apply 阶段完成在线回读、xlsx 导出和三方一致性检查。若发现重复记录，会列出 record_id 并阻断；修复后可以重跑，同名在线 Sheet 会优先复用。

## Unity merge workflow

Unity 窗口的项目 adapter 模式会按当前 git branch 自动推导合并上下文。项目配置可以在根节点、`toolkit` 或 `github` 节点声明：

```json
{
  "toolkit": {
    "defaultTargetBranch": "main",
    "githubRepository": "owner/repo",
    "allowPrAutoDetect": true
  }
}
```

- `defaultTargetBranch`：没有识别到 GitHub PR 时使用的目标分支，默认 `main`。
- `githubRepository`：GitHub 仓库名；为空时 Unity 会尝试从 `origin` remote 推导。
- `allowPrAutoDetect`：是否允许 Unity 通过 `gh pr view` 自动读取 PR number、URL 和 base branch，默认 `true`。

这些字段只用于通用工作流推导。真正的 branch/profile 定位仍由 `branchWorkspace`、`BranchBindings` 和 `ConfigSheets` 决定，项目私有规则应放在 adapter 内。

## lark-cli 路径

默认使用环境中的 `lark-cli`。如果安装在非标准位置，可设置：

- `providerSettings.larkCliPath`
- `LARK_CLI_PATH`

Windows 下优先解析 `lark-cli.ps1`，其次才使用 `.exe` / `.cmd` 等 shim；包含 JSON、中文、换行、方括号或反斜杠的 `--json` / `--values` 参数会以 compact JSON 传递，避免 cmd 层破坏参数。

## secret

不要把 app secret、access token、refresh token、真实业务表 URL 写入这些文件。Lark provider 只依赖 `lark-cli` 的本地认证状态。
