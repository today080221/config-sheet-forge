# CLI

CLI 命令名是 `config-sheet-forge`。

命令：

- `init`
- `doctor`
- `discover-root`
- `new-table`
- `sync`
- `seed-from-xlsx`
- `merge`
- `gate`

`seed-from-xlsx` 用于把旧本地 ExcelToSO xlsx 一次性迁移为在线 Sheet Source of Truth：

```bash
config-sheet-forge seed-from-xlsx --table <id> --source-xlsx <path> --dry-run
config-sheet-forge seed-from-xlsx --all --manifest <project-config-or-contract> --dry-run
```

dry-run 只生成 planned actions 和中文可读失败原因。apply 需要 `--yes`；更新 ExcelToSO settings 还需要 `--confirm-excel-to-so`。

排查 provider 问题时使用 `--details`。默认输出应保持人能读懂，低层细节放 details。
