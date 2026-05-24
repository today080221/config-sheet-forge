# CI Gate

CI gate 用于阻止不安全的配表变更进入主干。

推荐检查：

```bash
dotnet build ConfigSheetForge.sln
dotnet run --project tests/ConfigSheetForge.Tests
dotnet run --project src/cli/ConfigSheetForge.Cli -- gate --cache .config-sheet-forge/cache --annotations github
pwsh scripts/Check-NoPrivateContent.ps1
```

`gate` 会读取 semantic workbook JSON，执行便携子集和 schema review。传入 `--annotations github` 时，会输出 GitHub workflow command annotation，让 PR 页面能直接显示错误或 warning。

## CI 不应该做什么

- 不应该静默选择飞书/Lark root。
- 不应该打印 raw access token 或 app secret。
- 不应该把私有租户导出的 xlsx 或 semantic cache 提交到公开仓库。
- 如果 CI 需要 provider 同步，root 和凭证必须来自已审查 config 或 GitHub secrets。
