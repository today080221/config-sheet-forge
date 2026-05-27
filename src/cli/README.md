# CLI

CLI 命令名是 `config-sheet-forge`。

命令：

- `init`
- `doctor`
- `discover-root`
- `new-table`
- `sync`
- `sync-cache`
- `registry-status`
- `sync-status`
- `bootstrap-current-branch-from-target`
- `seed-from-xlsx`
- `merge`
- `gate`
- `registry-migrate`

`seed-from-xlsx` 用于把旧本地 ExcelToSO xlsx 一次性迁移为在线 Sheet Source of Truth：

```bash
config-sheet-forge seed-from-xlsx --table <id> --source-xlsx <path> --dry-run
config-sheet-forge seed-from-xlsx --all --manifest <project-config-or-contract> --dry-run
```

dry-run 只生成 planned actions 和中文可读失败原因。apply 需要 `--yes`；更新 ExcelToSO settings 还需要 `--confirm-excel-to-so`。

`registry-status` 是只读状态检查：只读 Base 注册中心，不导出在线 Sheet，不写文件。它用于让 Unity 判断当前分支是否已经有 BranchBindings / ConfigSheets。

`sync-status` 也是只读检查：它基于 live registry 和本地 cache / sha 文件估算当前 cache 状态，不读取或导出在线 Sheet，也不写任何文件。最终是否需要写 cache 仍以 `sync-cache --dry-run` 的三方检查为准。

`sync-cache` 在 contract/manifest 配置了 Base 注册中心时，会从 live BranchBindings 与 ConfigSheets hydrate 当前 Git branch/profile 的在线表定位。dry-run 会读取在线 Sheet、临时导出 xlsx、做三方一致性和 hash gate，并输出 `cacheStatus`；apply 才会在确认后写正式本地 cache。

从 v0.4.24 开始，Lark provider 不再依赖 no-range `sheets +read`。如果没有显式 range，会优先从临时导出的 xlsx 或 Sheet 元数据构造 `sheetId!A1:<col><row>`；遇到 Feishu `90202 wrong startRange` 会自动 retry，并把 attemptedRange、retryRange、sheetId 和脱敏 token 写进 diagnostics。

```bash
config-sheet-forge registry-status --manifest <project-config-or-contract> --details
config-sheet-forge sync-status --manifest <project-config-or-contract> --details
config-sheet-forge sync-cache --manifest <project-config-or-contract> --dry-run
config-sheet-forge bootstrap-current-branch-from-target --manifest <project-config-or-contract> --target-branch main --dry-run
config-sheet-forge bootstrap-current-branch-from-target --manifest <project-config-or-contract> --target-branch main --preview-result Temp/ConfigSheetForge/unity-lifecycle/bootstrap-current-branch-from-target.result.json --apply --confirm-create-online-sheets --confirm-registry-upsert --confirm-schema-reviews
config-sheet-forge registry-migrate --base <base-token> --dry-run
```

`bootstrap-current-branch-from-target` apply 会从 PR base/main 的在线 Source of Truth 派生当前分支工作区。它要求最近一次同输入 dry-run result / requestFingerprint；默认不写本地 cache、不改 ProjectSettings、不改 ExcelToSO。

排查 provider 问题时使用 `--details`。默认输出应保持人能读懂，低层细节放 details。
