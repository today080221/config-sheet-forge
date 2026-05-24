# 任务开始

## 任务

- 标题：推进 v0.2.0 上线质量
- 日期：2026-05-25
- 分支：chore/release-v0.2.0

## 范围

- In：typed core、Lark parser/smoke、Unity tests、gate annotations、CI、中文 docs、v0.2.0 release。
- Out：提交 secret、真实业务表内容、私有团队链接；强制阻塞在 GitHub 权限不足上。

## 安全检查

- [x] 不提交 secret、access token、私有团队链接、owner route 或真实业务表内容。
- [x] Provider 值只放本地 config、环境变量或 GitHub secrets。
- [x] Root discovery 只推荐候选项。
- [x] 文档统一中文维护，保留 CLI、API、UPM 等惯用英文术语。

## 计划

1. 重做 git sync preflight。
2. 实现共享 typed inference 和 CLI 配置。
3. 增强 Lark parser，跑 disposable smoke。
4. 增加 gate annotation、Unity edit-mode tests，并准备/尝试恢复 CI workflow。
5. 更新中文文档、版本号、release notes。
6. 尝试 GitHub milestones/issues/protection/Actions，并完成 release 验证。

## 验证

- [x] git sync preflight。
- [x] dotnet build。
- [x] 当前测试 runner。
- [x] disposable Lark smoke。
- [x] full CI。
- [x] Unity smoke。
- [ ] GitHub Actions。
