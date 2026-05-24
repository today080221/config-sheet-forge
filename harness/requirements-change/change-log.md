# 需求变更日志

## 2026-05-24

- 初始 bootstrap 请求作为 baseline。

## 2026-05-25

- 用户要求将剩余上线质量项沉淀到仓库文档。
- 用户确认 v0.2.0 计划：typed core、Lark parser/smoke、Unity tests、gate annotations、GitHub 治理、docs/release。
- GitHub workflow/admin/protection 权限失败改为非阻塞，但必须认真尝试并记录。
- 项目文档统一中文维护；CLI、API、UPM、CI、PR、AI 等中文语境惯用英文术语保留。
- typed inference 采用类型行优先，并尽量对齐 `excel_to_scriptableobject` 的 field/type/description/data 行约定。
- `gh auth refresh -s workflow` 在本机非交互环境超时，当前 token 仍只有 `repo/gist/read:org` scope；workflow/protection 继续按 best-effort 处理。
- GitHub milestones M0-M5 已创建，M0 issue/milestone 已关闭，其余工作项用中文 issue 追踪。
