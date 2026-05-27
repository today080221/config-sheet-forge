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

`sync-cache` 在 contract/manifest 配置了 Base 注册中心时，会从 live BranchBindings 与 ConfigSheets hydrate 当前 Git branch/profile 的在线表定位。dry-run 会读取在线 Sheet、临时导出 xlsx、做三方一致性和 hash gate，并输出 `cacheStatus`；apply 才会在确认后写正式本地 cache。

```bash
config-sheet-forge registry-status --manifest <project-config-or-contract> --details
config-sheet-forge sync-cache --manifest <project-config-or-contract> --dry-run
config-sheet-forge bootstrap-current-branch-from-target --manifest <project-config-or-contract> --target-branch main --dry-run
config-sheet-forge registry-migrate --base <base-token> --dry-run
```

排查 provider 问题时使用 `--details`。默认输出应保持人能读懂，低层细节放 details。
