# Changelog

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
