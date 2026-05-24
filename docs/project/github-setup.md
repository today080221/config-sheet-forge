# GitHub 设置记录

## 推荐 branch protection

- 合并前需要 PR。
- 禁止 force push。
- 禁止删除 `main`。
- CI 通过后再设为 required status check。
- 如果仓库设置兼容，启用 linear history。

## Milestones

- M0 仓库启动
- M1 核心工作簿模型
- M2 Lark Provider
- M3 Unity 包
- M4 合并审查与 Gate
- M5 文档与发布

## Issue 策略

- 已完成的 bootstrap/release 项可关闭，并链接 tag/release。
- v0.2.0 的剩余线上治理项独立 issue 跟踪。
- GitHub 权限不足不阻塞本地 release，但必须写明缺少的 scope 或 API 错误。

## PR 节奏

大改动优先走小 PR。创建 PR 后等待 4-6 分钟让自动 review 跑完，先修 actionable finding，再合并。
