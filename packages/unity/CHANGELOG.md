# Changelog

## 0.3.0

- 新增 lifecycle contract、PR gate report、strict bot、三方一致性和 hash-gated cache 能力。
- Unity package 排除 CLI-only xlsx zip/xml 读取器，降低 asmdef/import 风险。
- Editor window 增加中文 Source of Truth 文案和 ProjectSettings/*ConfigSheetForge*.json 状态发现。
- 保留 `Tools/Config Sheet Forge` 根菜单 alias，并暴露稳定 Editor API / 菜单契约。
- 项目配置存在时，Unity UI 会通过 adapter 生成 lifecycle contract，再调用 core `apply-contract`。
- `pr-gate-report` lifecycle 会写标准 PrGateReport JSON，并在 Unity adapter 模式下把表单输入写入 `*.inputs.json` 后传给项目 adapter。
- ExcelToSO settings upsert 支持 JSON 结构，避免把项目 JSON 设置当 YAML 追加。

## 0.2.0

- 新增共享 typed matrix import，支持 field/type/description/data 行布局。
- 新增 Unity edit-mode tests，覆盖共享 core hash、命令构造、配置路径和人话 CLI 启动错误。
- 将 Editor window 复制的 UPM URL 更新到 v0.2.0。

## 0.1.1

- 重做 Editor window：Start、Tables、Merge、Gate、Output tabs。
- 增加 tooltip、copy-command helper、本地 config/registry 状态、文档入口。
- 改进 Unity 侧 CLI 启动逻辑，先解析 PATH/PATHEXT 可执行文件再启动进程。

## 0.1.0

- 初始 Unity package scaffold。
- 共享 core assembly，包含语义工作簿模型、便携 validation、语义 hash、schema review、三方合并。
- Editor window 提供 doctor、discover-root、sync、gate 入口。
- Lark CLI discovery 支持显式路径、`LARK_CLI_PATH`、Windows npm shim、PATH/PATHEXT、node run.js fallback。
