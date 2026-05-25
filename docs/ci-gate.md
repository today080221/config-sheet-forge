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

每次 gate 还会写出 PR gate report，默认位置是 `Temp/ConfigSheetForge/pr-gate-report.json`，可用 `--report <path>` 覆盖。report 包含 gitHead、branch、BranchBindings 状态、权限状态、portable subset、三方一致性、schema review、waiver、changedTables、cache hashes 和面向策划的失败原因。

项目 adapter 需要把 BranchBindings、MergeReviews、Waivers、SchemaReviews 等注册中心状态填进 `pr-gate-report` lifecycle contract，再由 core 统一生成可消费 report。分支未绑定、绑定冲突、过期 waiver、非配置负责人批准、schema review 未完成、无权限读取注册中心、缺同步报告等都会生成普通人能看懂的失败文案。

## CI 不应该做什么

- 不应该静默选择飞书/Lark root。
- 不应该打印 raw access token 或 app secret。
- 不应该把私有租户导出的 xlsx 或 semantic cache 提交到公开仓库。
- 如果 CI 需要 provider 同步，root 和凭证必须来自已审查 config 或 GitHub secrets。
