# Changelog

## 0.4.11

- Unity 首页状态卡改为策划向人话摘要，不再把 `passed=false`、`failures=1` 这类 raw debug 字段放进主流程；BranchBindings、Wiki token、CLI 来源和路径继续放在“高级诊断”。
- `配表`页改为“同步当前分支 cache / 新建配表 / 本地 Excel Seed”折叠结构；默认只展开同步，Seed 作为高风险迁移入口默认收起且不会自动执行。
- 按钮文案区分 `预览同步计划`、`预览新建配表`、`预览本地 Excel Seed`、`生成合并预览`；写入按钮明确目标，并要求确认加最近一次同输入预览通过。
- 新建配表表单不再默认填 `items / Items`，必填为空时禁用预览并提示填写配表ID和显示名称；字段模板先以可读示例展示，文本编辑入口默认折叠。
- 合并页按 GitHub PR 心智展示当前分支、目标分支、PR、状态和下一步；merge-base、target profile/wiki、单表比较等收进高级选项。
- PR 检查页把失败原因映射成人话卡片，例如缺 MergeReviews、Schema review 未完成、waiver 过期、权限不足，并给出下一步。
- 非输出页底部默认只显示一行最近结果摘要，完整命令、详细日志和 result JSON 默认折叠；输出页保留大日志视图并继续自动换行。
- Unity package smoke 增加 UI 文案断言，防止“当前工作流 / 合并输入 / passed=false / failures=”等调试文案回流到主 UI。

## 0.4.10

- 修复 v0.4.9 Unity Editor assembly 编译失败：`ConfigSheetForgeWindow.ExtractJsonString` 现在使用窗口类内可见的 JSON string parser，不再误调用 helper class 私有方法。
- 将 `EditorGUILayout.Space(float)` 改为 `GUILayout.Space(float)`，提升 Unity managed assembly smoke 的兼容性。
- `Validate-UnityPackage.ps1` 新增真实 Editor assembly 编译 smoke，会生成临时 `ConfigSheetForge.Core.dll` 和 `ConfigSheetForge.Editor.dll`，CI 可捕获 CS0103 这类 Unity asmdef 编译错误。

## 0.4.9

- Unity 首页固定展示当前分支、Feishu branch/profile、在线表可读状态、cache 状态、PR gate 和下一步建议；CLI、路径、Wiki token/url、复制命令等工程信息默认收进“高级诊断”。
- 非输出页改为底部可折叠/可拖拽结果面板，成功态优先显示摘要；完整命令和原始日志默认折叠，失败时自动展开关键日志。
- 合并页改为 PR-like 工作流：自动读取当前分支、目标分支、GitHub PR、merge-base，并按 source/target 自动生成 base/ours/theirs semantic 输入路径。
- Project config summary 新增 `defaultTargetBranch`、`githubRepository`、`allowPrAutoDetect`，供通用 merge 工作流使用，不写项目私有逻辑。
- `compare-merge` inputs 会写入 source/target/target Feishu workspace/merge-base/PR 信息；写回 main 需要显式确认，并在确认后向 `apply-contract` 传 `--yes`。

## 0.4.8

- Unity lifecycle 操作改为后台 job 执行，seed、sync-cache、compare-merge、pr-gate-report 不再在 IMGUI MouseUp 链路同步 `WaitForExit`。
- 后台 job 实时追加日志、展示阶段状态、运行中禁用相关按钮，并提供取消按钮终止进程树。
- 输出区改为“摘要 + 详细日志”结构，dry-run 成功后优先显示成功/失败、模式、planned actions、branch node、result path 和是否写 cache。
- 输出面板随窗口高度扩展；输出 tab 改为日志/报告主视图，最近命令默认折叠，并保留复制命令、复制输出、打开 result 和 lifecycle 目录按钮。
- 首页同步按钮改为“生成同步预览”，明确等同 dry-run，不写飞书、不改本地 cache、不改 ProjectSettings。

## 0.4.7

- Unity `sync-cache` 按钮在项目 adapter 模式下改走 lifecycle adapter + `apply-contract`，dry-run/apply 均复用 contract 输入，不再绕过项目桥接直接调用裸 CLI。
- Unity CLI 解析支持 `CONFIG_SHEET_FORGE_CLI`，以及 `CONFIG_SHEET_FORGE_ROOT + sourceCliProjectRelativePath` 的 `dotnet run --project ... --` 源码 checkout fallback。
- `sync-cache` apply 使用同步页确认开关并向 `apply-contract` 传 `--yes`；未确认的 apply 会被 core 阻断。
- 最近命令和命令输出区改为自动换行，CLI 启动失败按“命令 / 原因 / 下一步”结构化中文展示。

## 0.4.6

- Unity 状态页改为工作流摘要：优先展示当前 Git 分支、Feishu profile、Wiki branch 节点、当前 branch 表数量、cache 状态与最近 PR gate 摘要。
- `ProjectConfigProbe` 改用结构化轻量 JSON DOM 解析，避免把 `feishu.registryBase.tables` 这类表 ID 映射误当成项目配表清单。
- 当前 branch 表列表展示 TableId、显示名称、在线 Sheet 链接、cache 状态、semantic hash、更新时间、schema 状态与负责人角色。
- `.config-sheet-forge` 改为高级诊断中的“本地状态/cache，可忽略、可重建”，不再参与共享项目状态摘要。
- `sync-cache` 无变化时输出“无变化，未重写 cache”。

## 0.4.5

- 修复 lark-cli 1.0.40 `base +table-list` 不支持 `--format` 的 argv 兼容问题。
- `registry-migrate` registry snapshot 加载不再给 `table-list` / `field-list` 传 `--format json`；`record-list` 仍显式使用 `--format json`。
- 新增 fake lark-cli CLI smoke，验证 `table-list` / `field-list` 无 `--format` 也能解析默认 JSON，并继续列出重复 BranchBindings record_id。

## 0.4.4

- 修复 lark-cli 1.0.40 `base +table-list --format json` 的 `data.tables[].id/name` 解析，registry snapshot 不再因缺少 `table_id` 为空。
- `base +field-list` 同步兼容 `data.fields[].id/name` 与旧 `field_id/field_name` 形态。
- 新增 lark-cli 1.0.40 table-list + field-list + record-list 组合 fixture，覆盖 `registry-migrate --dry-run` 重复 BranchBindings record_id 输出。
- `sync-cache` dry-run 现在能基于 live registry hydrate 当前 GitBranch + Profile，并在重复 BranchBindings 时中文阻断且列出 record_id。

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
