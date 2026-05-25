# Changelog

## 0.4.3

- 修复 Base `record-list --format json` 矩阵返回解析，统一还原为 `record_id + fields`，registry 查重/定位显式使用 JSON 输出。
- seed/SchemaReviews/ConfigSheets/BranchBindings upsert 按复合 key 定位既有记录；ConfigSheets 不再用裸 `TableId` 覆盖 main 分支记录。
- `sync-cache` 在配置 registry Base 时会 live hydrate 当前 Git branch/profile 的 BranchBindings 与 ConfigSheets，contract 只带 main binding 时也能继续 dry-run。
- 同一 `GitBranch + Profile` 的重复 BranchBindings 会阻断并列出 record_id，避免静默任选一条。
- `registry-migrate --dry-run` 会列出重复 BranchBindings、空白默认行和中英文/旧 schema 字段歧义；删除类 cleanup apply 需要显式 `--yes`。

## 0.4.2

- 修复 Windows + `lark-cli` strict bot seed apply：优先使用 `lark-cli.ps1`，飞书 `--json`/`--values` 参数改为 compact JSON，并保留 action/category/stderr 诊断。
- `sheets +write` fallback 改为分块写入，自动计算明确矩形 A1 range（支持超过 Z 的列），避免命令行过长和多行 range 错误。
- seed/sync 导出临时目录改到 workspace 内的 `Temp/ConfigSheetForge/...`，满足 lark-cli 安全输出路径；seed 在线回读使用本地矩阵推导的精确 used range。
- 三方 semantic triangulation 忽略 provider/source/sheet identity 和由 key 派生的 display name 差异，并保留可定位的行列 diff。
- ProjectSettings 回填优先写既有 `table.feishu.*` 节点；ExcelToSO JSON upsert 更新既有条目并保留顺序、字段命名、BOM 和换行。
- manifest 模式解析 `registry.tableIds` / `feishu.registryBase.tables`，缺失 machine-key table id 时阻断并提示改走 adapter contract。

## 0.4.1

- 新增 branch/profile workspace resolver，seed dry-run/apply 会先定位或创建 `项目配置表/<branch node>`，不再直接挂到 Wiki 根节点。
- 新增 BranchBindings 一对一校验和 upsert 结果字段，ConfigSheets 注册按 `TableId + Branch/Profile` 维度登记。
- 新增 `sync-cache` lifecycle/CLI 预览链路，按 BranchBindings + ConfigSheets 定位在线表，并保留 hash-gated cache 语义。
- PR gate report 增加 BranchBindings 状态检查；`--report` 仍写纯 `PrGateReport` 本体 JSON。
- Unity 窗口新增 branch 工作区状态、同步在线 Cache 入口和 `OpenSyncCache()` 稳定 Editor API。

## 0.4.0

- 新增 `seed-from-xlsx` CLI，以及 `seed-from-local-xlsx` / `bootstrap-from-local-xlsx` lifecycle operation。
- 支持旧本地 ExcelToSO xlsx dry-run 预检、semantic normalize、在线 Sheet 创建/复用、三方一致性校验和 hash-gated cache 回填。
- seed apply 支持回填 ProjectSettings、Base ConfigSheets、SchemaReviews 和 ExcelToSO settings；默认 strict bot，禁止静默 fallback 到 user。
- 扩展 xlsx portable subset 检查：读取失败、公式、图片、合并单元格、富文本、跨表/跨工作簿引用、日期对象和不稳定结构给出中文可读阻断。
- Unity 窗口新增本地 Excel Seed 入口和稳定 `OpenSeedFromLocalXlsx()` Editor API。

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
