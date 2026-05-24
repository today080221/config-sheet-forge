# 里程碑

本文件记录公开项目路线图。v0.2.0 已把首版剩余上线质量项落到本地实现和验证流程；GitHub issue、milestone、Actions、branch protection 以线上状态为准。

## M0 仓库启动

- [x] 初始化仓库。
- [x] 添加 Apache-2.0 license。
- [x] 添加 .NET solution、CLI、core、provider、Unity 包、docs、examples、harness、CI skeleton。
- [x] 添加私有内容扫描。

## M1 核心工作簿模型

- [x] 语义工作簿模型。
- [x] 便携子集 validator。
- [x] 语义 hash。
- [x] 三方合并报告。
- [x] Schema review。
- [x] 类型行优先的 typed cell inference。

## M2 Lark Provider 接入

- [x] Provider 抽象。
- [x] `lark-cli doctor` 集成。
- [x] Root discovery 候选项。
- [x] Sheet export/read 入口。
- [x] 多种 `lark-cli` JSON shape 解析。
- [x] 一次性 disposable Lark smoke。

## M3 Unity 包

- [x] UPM manifest。
- [x] 共享 core assembly。
- [x] Editor window。
- [x] Minimal sample。
- [x] Unity edit-mode tests。

## M4 合并审查与 Gate

- [x] CLI merge。
- [x] CLI gate。
- [x] Markdown merge report。
- [x] GitHub annotation 输出。

## M5 文档与发布

- [x] 中文 README、getting started、configuration、human guide、portable subset、CI gate、provider docs。
- [x] 发布 GitHub repository。
- [x] Release `v0.1.0`。
- [x] Release `v0.1.1` Unity onboarding update。
- [x] 创建/同步 GitHub issues 和 milestones。
- [ ] 恢复公开 GitHub Actions 并验证通过。
- [x] 配置 `main` branch protection。
- [ ] Release `v0.2.0`。
