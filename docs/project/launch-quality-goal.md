# v0.2.0 上线质量目标记录

本文件记录从 v0.1.1 首版推进到 v0.2.0 上线质量的范围、约束和验证证据。它不保存任何真实业务表 URL、token、租户 ID 或私有项目名。

## 当前目标

- 公开仓库：`https://github.com/today080221/config-sheet-forge`
- 上一版公开 UPM tag：`v0.1.1`
- v0.2.0 目标 UPM URL：`https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.2.0`
- v1.0.0 暂缓，等 API 和 provider contract 稳定后再承诺兼容性。

## 不可妥协项

- 不提交 secret、access token、真实团队链接、私有项目名、owner route 或私有业务表内容。
- 项目特定值只放 config、环境变量或 GitHub secrets。
- Root discovery 只能推荐候选项，不能静默选择。
- CLI 与 Unity 继续共享同一份 core 源码。
- 人能看到的错误要可执行；hash、revision、raw JSON 等低层信息放 details。
- 每次 release 前必须跑私有内容扫描。

## v0.2.0 已覆盖工作流

- Core：类型行优先的 `string`、`number`、`integer`、`bool`、`date`、`datetime`、`json`、`enum` 语义导入。
- Lark provider：bot 优先、user fallback；支持 wrapped values、items/records、stdout/stderr 混合 JSON 提取。
- Unity：Editor helper 抽取和 edit-mode tests。
- Gate：GitHub workflow command annotation 输出。
- CI：本地 `Run-CI.ps1` 覆盖 build、测试、Unity 包结构、私有内容扫描、CLI pack；GitHub Actions workflow 因缺少 `workflow` scope 暂未能推送。
- 文档：项目文档统一中文维护，保留 CLI、API、UPM、CI、PR 等中文语境惯用英文术语。

## 验证要求

- `pwsh scripts/Run-CI.ps1`
- `dotnet run --project tests/ConfigSheetForge.Tests`
- `dotnet pack src/cli/ConfigSheetForge.Cli/ConfigSheetForge.Cli.csproj -c Release`
- Unity edit-mode tests 和干净 UPM import/compile smoke。
- 一次性 Lark smoke：`doctor -> discover-root -> new-table -> sync -> gate`。
- GitHub Actions、issues、milestones、branch protection 尽力配置；权限失败不阻塞 release，但必须记录。

## 本轮证据

- 本地 CI：`pwsh scripts/Run-CI.ps1` 通过，包含 build、12 个测试、Unity 包结构检查和私有内容扫描。
- CLI 包：`ConfigSheetForge.Cli.0.2.0.nupkg` 已由 `dotnet pack -c Release` 生成。
- Lark smoke：使用 disposable 表，bot 身份完成 `doctor -> discover-root -> new-table -> sync -> gate`；未记录真实业务表 URL/token。
- Unity smoke：Unity `6000.3.12f1` 干净临时项目导入本地 UPM 包并运行 EditMode tests，4/4 通过；临时项目已清理。
- GitHub 治理：milestones `M0`-`M5` 和 issues `#1`-`#6` 已创建，`M0` 已关闭。
- GitHub 权限：`gh auth refresh -s workflow` 在非交互环境超时，当前 token 未获得 `workflow` scope；workflow push 被 GitHub 拒绝，Actions 暂未恢复。

## 停止规则

- 同一测试或外部 API 连续失败 3 次且没有新证据时，停止该 workstream 并总结。
- 没有 disposable Lark sheet 时，不使用真实业务表。
- GitHub workflow/admin/protection 权限失败为非阻塞项，但要认真尝试并记录。
