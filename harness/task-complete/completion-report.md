# 任务完成

## 摘要

v0.2.0 上线质量工作已进入发布阶段：共享 typed inference、Lark parser、gate annotation、Unity edit-mode tests 和中文文档都已落地到工作区，并完成本地 CI、disposable Lark smoke、干净 Unity import/edit-mode smoke。

## 已改文件类别

- Core：共享矩阵导入、类型规范化、语义 hash details。
- CLI：表布局配置、Lark 身份设置、GitHub annotation 输出。
- Provider：Lark JSON shape 解析、bot/user fallback、相对导出路径。
- Unity：Editor helper、edit-mode tests、UPM 版本。
- Docs/harness：中文化并记录 v0.2.0 目标。

## 验证记录

- [x] `dotnet build ConfigSheetForge.sln`
- [x] `dotnet run --project tests/ConfigSheetForge.Tests`
- [x] disposable Lark smoke：bot 创建一次性表，临时工作区完成 `doctor -> discover-root -> new-table -> sync -> gate`。
- [x] `pwsh scripts/Run-CI.ps1`（12 个测试通过）
- [x] `dotnet pack src/cli/ConfigSheetForge.Cli/ConfigSheetForge.Cli.csproj -c Release`
- [x] Unity edit-mode/import smoke：Unity `6000.3.12f1` 干净临时项目导入本地 UPM 包，EditMode 4/4 通过。
- [x] PR review 修复：datetime normalization 不再依赖本地时区；Lark object-array 行 lookup 改为大小写不敏感。
- [x] GitHub milestones/issues：M0-M5 已创建，M0 issue/milestone 已关闭。
- [x] GitHub workflow scope 尝试：`gh auth refresh -s workflow` 超时，当前 token 未获得 `workflow` scope。
- [x] GitHub workflow push 尝试：因缺少 `workflow` scope 被 GitHub 拒绝，本次 release 不包含 `.github/workflows/ci.yml`。
- [ ] GitHub Actions
- [x] branch protection：`main` 禁止 force push 和删除，未要求 status checks。
- [ ] tag/release/clean clone UPM 验证

## 剩余风险

- GitHub workflow scope 未刷新成功；workflow 文件无法推送，需要用户后续用带 `workflow` scope 的 token 恢复 GitHub Actions。
- Unity 外部项目 smoke 未执行；本轮只使用干净临时项目，避免打扰可能存在的 active worker。
