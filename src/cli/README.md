# CLI

CLI 命令名是 `config-sheet-forge`。

命令：

- `init`
- `doctor`
- `discover-root`
- `new-table`
- `sync`
- `sync-cache`
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

`sync-cache` 在 contract/manifest 配置了 Base 注册中心时，会从 live BranchBindings 与 ConfigSheets hydrate 当前 Git branch/profile 的在线表定位。重复 BranchBindings 会阻断并列出 record_id。

```bash
config-sheet-forge sync-cache --manifest <project-config-or-contract> --dry-run
config-sheet-forge registry-migrate --base <base-token> --dry-run
```

排查 provider 问题时使用 `--details`。默认输出应保持人能读懂，低层细节放 details。
