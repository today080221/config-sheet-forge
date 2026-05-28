# Changelog

## 0.4.29

- 新增官方 Tauri Desktop 工作台骨架 `apps/desktop`，用于承载 Source of Truth 主流程：项目识别、环境检查、live registry 状态、sync-cache dry-run、cache dialect repair、compare/PR gate 等长任务入口。Desktop frontend 已纳入 CI build/lint。
- Unity 默认窗口改为 thin bridge：首页只保留 `打开 Config Sheet Forge Desktop`、`安装/更新 SourceOfTruthCache profile`、`导入 Unity 配表资产`、`运行/读取 PR gate`、`查看最近结果`。网络长任务推荐跳转 Desktop，不再默认进入 IMGUI 大工作台。
- 原完整 Unity IMGUI 工作流保留为 Legacy fallback，菜单移动到 `Tools > Config Sheet Forge > Legacy/*`，用于没有 Desktop、CI 调试或救急操作；既有 Editor API 仍可用，并新增 `OpenLegacy*` 明确入口。
- CLI 新增 `repair-cache-dialect`。当 semantic/hash 没变但 cache xlsx 类型行仍是 portable dialect 时，可以不联网预览/修复 `.config-sheet-forge/excel-cache/*.xlsx`，不写飞书、不写旧 `Excel/`、不改 ProjectSettings。
- 新增 structured progress event 基础能力：CLI 操作可通过 `--progress <path>` 或 `--progress-stdout` 输出 NDJSON 进度事件，Desktop 后续可直接订阅阶段、表名、current/total 和 severity。

## 0.4.28

- ExcelToSO 集成改为显式使用 `SourceOfTruthCache` named profile；`导入 Unity 配表资产` 不再要求把本地/default profile 的 `Excel/` 路径改成 `.config-sheet-forge/excel-cache`。
- Unity 窗口新增 `安装/更新 Source of Truth 导入 profile`：只新增/更新 `ProjectSettings/ExcelToScriptableObjectSettings.asset` 里的 cache profile，并在发现本地 profile 被旧版本改到 cache 时恢复到 `oldExcelPath` / `Excel/<TableId>.xlsx`。
- ExcelToSO backend 现在要求 v1.0.6 的 `ImportByProfile` API；未安装或版本过旧时显示中文前置条件提示，仍保持可选 peer dependency，不添加硬 asmdef 依赖。
- 文档说明两套 profile：本地 Excel / OneDrive 工作流给人工 ExcelToSO UI 使用，Source of Truth cache profile 给 config-sheet-forge / CI 使用。

## 0.4.27

- `sync-cache` / seed 写正式 `.config-sheet-forge/excel-cache/*.xlsx` 时，ExcelToSO backend 会把物理 xlsx 的类型行写成 ExcelToSO dialect：`integer -> int`、`number -> float`、`bool/string` 保持可导入；semantic JSON 与 hash 仍继续使用 canonical portable 类型。
- 对旧 Excel 中的数组列，会从 `oldExcelPath/sourceXlsxPath` 或字段 `originalType/excelToSoType` 还原 `int[]`、`float[]`、`string[]`。无法还原的 `json` 会在同步/写 cache 前中文阻断，不再生成会让 ExcelToSO 弹英文错误的 cache。
- Unity `导入 Unity 配表资产` 增加 cache 类型预检：发现 `json/date/datetime/enum` 等 ExcelToSO 不能直接导入的类型时，先显示中文修复建议，阻止直接调用 ExcelToSO。
- 文档更新推荐 ExcelToSO `v1.0.5`，并说明 Source of Truth cache 是生成物，不能手改；需要通过 schema/adapter 声明具体 ExcelToSO 类型。
- 测试覆盖 primitive alias、旧表数组列 json 还原、无法还原 json 阻断，以及正式 cache xlsx 不写出 canonical `json/integer/number` 类型行。

## 0.4.26

- Unity 窗口新增 `导入 Unity 配表资产`：在最近一次 `sync-cache` 成功、`cacheStatus=upToDate`、无 blocked/triangulation failed 后，调用 ExcelToSO v1.0.4 public API，把 `.config-sheet-forge/excel-cache/*.xlsx` 导入 ScriptableObject asset。
- ExcelToSO 被作为可选 peer backend 处理：未安装 `com.greatclock.exceltoscriptableobject` 或版本过旧时，config-sheet-forge 不会编译失败，只在窗口里提示前置条件。
- 导入前会检查 `ProjectSettings/ExcelToScriptableObjectSettings.asset` 是否指向 Source of Truth cache；若仍指向旧 `Excel/` 路径，会阻断并提示“当前 ExcelToSO 还指向旧 Excel 路径，请先更新到 Source of Truth cache”。
- 提供单独的 `更新 ExcelToSO settings 到 cache` 确认步骤，只改对应 settings 的 `excel_name`，不会写飞书、不会导入 asset、不会写旧 Excel。
- ProjectConfigProbe 会从项目 tables 读取 `excelPath`、`oldExcelPath`、`assetDirectory`、`namespace`，并把这些本地导入元数据合并到 live registry 当前分支表列表中。
- Unity smoke 增加 ExcelToSO optional backend / 不直接 asmdef 依赖 / 旧 Excel 路径阻断 / 导入按钮文案断言，防止项目私有 bridge 回流。

## 0.4.25

- 修复飞书导出的 xlsx `dimension ref=A1` 但 `sheetData` 实际有多行多列时，`sync-cache` 误用 `A1:A1` 读取在线表的问题；used range 现在会扫描实际 `c/@r` 并与 dimension 取更大范围。
- `sync-cache` dry-run 的 Lark 读取诊断会记录 attemptedRange、retryRange、finalRange、online rows/cols、xlsx dimension rows/cols 与 sheetData rows/cols，便于定位范围和形状问题。
- 三方一致性比较改为双向 diff：`exported-xlsx` 或 `semantic-normalize` 多出来的列、行、单元格也会报告具体差异，不再出现 hash 不一致但没有可定位 diff 的空诊断。
- 若未来仍出现 hash mismatch 且 diff 为空，报告会输出三路 shape metadata（hash、sheet rows/cols、column keys、stableId 数量、range），便于继续排查。
- 测试覆盖 stale xlsx dimension、完整在线读取与导出 xlsx 三方通过、右侧多列/多行 diff，以及既有 `90202 wrong startRange` retry。

## 0.4.24

- 修复 Lark provider 在 `Range` 为空时依赖 `sheets +read` no-range 默认行为的问题：现在会优先从导出的 xlsx 推导显式 `sheetId!A1:<col><row>`，导出不可用时读取 `sheets +info` 的 grid 信息；遇到 `90202 wrong startRange` 会自动改用显式范围重试。
- `lark.read_failed` 诊断会带出 tableId、脱敏 spreadsheet token、sheetId、attemptedRange、retryRange、lark error code/message 和 retry 结果，避免把 no-range 问题误导成 bot 权限问题。
- Unity 在 `sync-cache` dry-run `cacheStatus=blocked` 时不再推荐“写入本地 cache”，写入按钮保持禁用，并显示“同步预检未通过”和 blocked tables；在线表状态仍信任 live registry。
- `sync-status` 不再只给 `unknown`：它会只读 live registry 和本地 cache/sha 文件，输出 per-table local cache state，不读取/导出在线 Sheet、不写文件。
- `bootstrap-current-branch-from-target` 增加 safe apply 路径：要求最近一次同输入 dry-run result/fingerprint，并分项确认创建/复用在线 Sheet、写 BranchBindings/ConfigSheets、登记 SchemaReviews；默认不写本地 cache、不改 ProjectSettings、不改 ExcelToSO。
- 降低 `cell.bool_invalid` warning 噪音：同一表/列的非阻断布尔解析 warning 会聚合显示，避免淹没真正的 blocked error。

## 0.4.23

- 新增只读 `registry-status` / `branch-status` / `sync-status` lifecycle：Unity 可以后台读取 Feishu Base 注册中心，判断当前分支 BranchBindings、ConfigSheets、缺失表、缺定位、重复记录和下一步建议；不会读取/导出在线 Sheet，也不会写任何文件。
- `sync-cache` dry-run 不再只返回 planned actions。它会从 live registry hydrate 在线表定位，读取在线 Sheet、临时导出 xlsx、做三方一致性检查和 hash gate，并在 result 中输出 `cacheStatus`、changed/missing/up-to-date/blocked tables、resolved online tables 和 no-change mtime 语义。
- Unity 状态页改为信任 live registry 作为在线 Sheet locator 的 Source of Truth：ProjectSettings 里的 SpreadsheetToken/SheetId 可以为空，只要注册中心返回有效 ConfigSheets，就显示“在线表已登记”，不再误报“未读取到在线表”。
- 首页推荐下一步按完整状态机收敛：缺分支工作区时推荐“从目标分支初始化当前分支在线表”，同步预览无变化时推荐 PR 检查/合并预览，有变化或缺 cache 时才推荐写入本地 cache，PR gate 已通过时不再被本地空 token 打回同步循环。
- 新增 `bootstrap-current-branch-from-target` / `branch-workspace-bootstrap-from-target` dry-run 入口，用于把“新分支在线表应从 main/PR base 派生”作为一等工作流展示，避免普通用户误入历史 `本地 Excel Seed`。
- Unity 打开窗口会以后台 job 做只读注册中心状态 probe；所有 lark/CLI 访问仍不阻塞 IMGUI，运行中可切 tab、滚动、复制日志和取消。
- 测试覆盖 ProjectSettings token 为空但 live registry 有 16/16 有效 ConfigSheets 时的状态显示、`sync-cache` dry-run `cacheStatus` 输出，以及推荐状态不再循环到“写入本地 cache”。

## 0.4.22

- PR gate hydrate 现在会统一归一化 Feishu Base 单选字段返回值：`"approved"`、`["approved"]`、`[{ "text": "approved" }]`、`{ "name": "approved" }` 都会按 `approved` 判断。
- `MergeReviews`、`SchemaReviews`、`Waivers` 的状态判断共用同一套归一化逻辑，避免 Base 读回 JSON array string 后误报“状态不是 approved/completed/passed”。
- `pr-gate-report` 输出会写回归一化后的 `mergeReview.status=approved`，有效 MergeReviews 会直接让 gate 进入 `gateState=passed`，不再依赖 waiver 放行。
- `submit-merge-review` 写入后会递归解析 lark-cli upsert 返回中的嵌套 `record_id` / `recordId`，Unity 可以显示真实 `rec...` 记录号。
- 测试覆盖 Feishu 单选状态归一化、live MergeReviews JSON array hydrate，以及 nested upsert record_id 解析。

## 0.4.21

- `registry-migrate` 新增窄迁移模式：`--only review-status-options`。这个模式只检查/补齐 `MergeReviews`、`SchemaReviews`、`Waivers` 的 `状态` 字段选项，不执行字段 ensure、rename、ambiguous alias 或 cleanup。
- Unity PR 检查页复制的注册中心修复命令改为窄命令：`registry-migrate --only review-status-options --dry-run`，避免普通修复误触完整 schema 清理/升级。
- `SchemaReviews.状态` 如果不是单选字段，会以 `registry.field.status_select_mismatch` 明确提示；apply 不会自动转换字段类型，需要负责人先在 Base 中确认迁移方案。
- 完整 `registry-migrate` 保留原来的注册中心 schema 清理/升级诊断能力，但不再作为“补审查状态选项”的默认推荐路径。
- 测试覆盖窄 dry-run 只产生 options/mismatch action、窄 apply 不执行 rename/ensure/cleanup，以及 SchemaReviews 状态字段非单选时不自动转换。

## 0.4.20

- `registry-migrate --dry-run` 现在会检测 `MergeReviews`、`SchemaReviews`、`Waivers` 的 `状态` 单选字段是否缺少治理流程需要的选项，并预告要补齐的 `approved` / `completed` / `passed` 等值。
- `registry-migrate` apply 对在线 Base 写入统一要求显式 `--yes`；补状态选项是幂等操作，只更新对应状态字段，不清理或改动无关字段。
- `submit-merge-review` apply 在写 Base 前会先检查 `MergeReviews.状态` 是否具备必要单选选项；缺选项时中文阻断，并提示先运行 `registry-migrate`。
- PR gate report 新增 `waived` / `gateState=waived` / `waivedFailures` 语义：有效 waiver 会显示“配置负责人临时放行”，不再同时输出普通用户容易误解的 hard failure。
- Gate report 继续保留 waiver 的 `recordId`、`approvedByRole`、`expiresAt`、`reason`，便于 Unity UI 和 CI 展示审计信息。
- Unity PR 检查页会区分“缺合并审查记录”和“已由配置负责人 waiver 临时放行”；注册中心状态选项缺失时提供 `registry-migrate --dry-run` 复制入口。
- Unity 合并页在提交审查前展示本次将写入的 branch、target、table scope、fingerprint 和写入边界，明确不会写 main/cache/ProjectSettings/ExcelToSO。
- 测试覆盖治理状态选项迁移、submit preflight 阻断、有效 waiver 放行语义，以及 Unity smoke 防回退。

## 0.4.19

- 新增 `submit-merge-review` / `approve-merge-review` lifecycle：合并预览通过后，可以正式写入 Base `MergeReviews` 审查记录，不再需要项目侧或 AI agent 手工补 Base 行。
- `compare-merge` dry-run 现在输出可复核的 `requestFingerprint` 和 source/target/tableIds/PR/report 摘要；提交合并审查记录时 CLI 会校验最近一次同输入 dry-run，不一致会中文阻断。
- CLI apply 增加 live `MergeReviews` hydrate：PR gate 会按当前分支和项目级/单表范围读取 Base 审查记录，支持 `approved`、`completed`、`passed`、`通过`、`已通过`、`完成`、`已完成`。
- Gate report 的 `MergeReview` 会写明 `recordId`、`reviewId`、`ApproverRole`、`GitBranch`、`TableId`，缺记录时提示去合并页提交合并审查记录。
- Unity 合并页新增 `提交合并审查记录` 按钮：只在最近一次合并预览成功后启用，二次确认文案明确只写 Base `MergeReviews`，不写 main、不写本地 cache、不改 ProjectSettings/ExcelToSO。
- PR 检查失败卡增加可执行导航：缺 MergeReviews 可跳到合并页并高亮提交入口；SchemaReviews / Waivers 提供轻量人工处理入口。
- 新增 `approve-schema-review` 与 `approve-waiver` lifecycle 基础能力，并在 PR 检查页提供简易表单，方便人工闭环处理 Schema 审查和临时放行。
- 测试覆盖 compare-merge 指纹、live MergeReviews gate hydrate，以及 Unity smoke 防止审查按钮和 lifecycle marker 回退。

## 0.4.18

- 目标分支初始化 apply 增加最后护栏：必须传入最近一次同输入 dry-run result，CLI 会校验 `requestFingerprint`，输入不一致或 dry-run 未通过时直接阻断写入。
- `bootstrap-target-branch-from-local-xlsx` result 现在输出 `requestFingerprint`、目标 branch/profile/tableIds 和全部确认项摘要，方便负责人复核“这次 apply 到底是不是刚刚预览的那一份”。
- CLI apply 增加 postflight：完成后重新读取 BranchBindings、ConfigSheets、SchemaReviews，验证目标分支每张表都有 SpreadsheetToken + SheetId，并确认 apply 阶段已完成在线回读、xlsx 导出和三方一致性检查。
- 目标分支初始化摘要会明确列出本地 cache、ProjectSettings、ExcelToSO settings 是 confirmed 还是 skipped；安全 apply 模式可只写飞书在线资产、Base 注册中心和 SchemaReviews。
- postflight 若发现 BranchBindings / ConfigSheets / SchemaReviews 重复记录，会列出 record_id 并阻断，不会静默任选一条；重跑会优先复用目标节点下已有在线 Sheet。
- Unity 初始化 main 向导在 apply 前会把 `--preview-result` 传给 `apply-contract`，二次确认文案明确“将写飞书 main，不会改本地 Excel/ProjectSettings/ExcelToSO（除非勾选）”。
- 测试新增安全 apply、本地写入 skipped、重复 apply 复用、postflight 缺定位阻断，以及 apply-contract preview proof 校验。

## 0.4.17

- 新增 `bootstrap-target-branch-from-local-xlsx` lifecycle operation，用于把目标分支（例如 PR base `main`）从本地 xlsx 正式初始化为在线 Source of Truth 工作区，不再需要项目侧手动 patch seed contract。
- 目标分支初始化会覆盖 target git branch/profile/wiki node title，默认使用 `local-xlsx` 源，并按项目配置表范围生成每张表的初始化计划。
- apply 确认拆为 `confirmCreateOnlineSheets`、`confirmRegistryUpsert`、`confirmSchemaReviews`、`confirmWriteLocalCache`、`confirmWriteProjectConfig`、`confirmExcelToSoSettings`；目标初始化不接受一个 `--yes` 覆盖所有写入。
- 未确认的本地 cache、ProjectSettings、ExcelToSO settings 写入会被显式跳过并写进 result actions，避免初始化 main 时误改旧 Excel 路径或项目配置。
- Unity 合并页在程序视图检测到目标分支缺工作区/表定位时，会显示“初始化目标分支 main（先 dry-run）”；执行 apply 入口只在“高级”开启后显示，并保留二次确认。
- CLI 新增同名命令和 apply-contract flags，strict bot 语义保持不变，不会静默 fallback 到 user。
- 测试覆盖目标分支 override、ProjectSettings/ExcelToSO 未确认不写，以及 Unity smoke 防回退。

## 0.4.16

- `compare-merge` dry-run 不再返回空成功：会解析 source/target 分支工作区、表范围、base/ours/theirs 路径、merge report/merged path，并在 actions details 中输出 tableCount、source/target wiki 和缺失表信息。
- 如果目标分支 BranchBindings、ConfigSheets 或在线 Sheet 定位信息缺失，`compare-merge` 现在返回 `success=false` 和中文修复建议，避免误导用户继续跑 PR 检查。
- `apply-contract compare-merge` 会从 live registry hydrate source 与 target 分支，支持同一 TableId 在不同 profile 下各保留一条 ConfigSheets 定位记录。
- Unity 窗口刷新状态增加节流，后台任务完成后才强制刷新；合并 PR/branch probe 继续走后台缓存，减少点击按钮后的 Repaint 卡顿体感。
- Unity 输出日志改为有上限的 StringBuilder buffer，长日志不再用字符串反复累加；源码 fallback 会显示“首次运行可能较慢”状态。
- Unity 标题统一为“配表 Source of Truth”，并对旧的空 compare-merge 成功结果做 UI 侧保护：没有有效 `merge.inputs.prepare` 时不会视为可继续 PR 检查。
- Unity package smoke 增加 v0.4.16 源码断言，防止 compare-merge 空预览、刷新节流和日志缓冲回退。

## 0.4.15

- Unity 顶部显示模式从“高级模式”拆为 `策划视图 / 程序视图`：策划视图默认使用人话摘要，程序视图补充内部 key、canonical 类型、路径和命令摘要。
- 新增独立 `高级` 开关，用于解锁手动路径覆盖、raw 字段模板、手动覆盖 PR 目标分支、单表比较和输出路径等风险配置入口；开启时会先弹出确认。
- 高级入口不等同于写入权限：创建在线表、Seed apply、写回 main 等危险写入仍保留预览成功、勾选确认和二次确认链路。
- 新建配表与合并页的风险配置不再因为进入程序视图自动出现，避免“只想看内部 key”时顺手暴露危险操作。
- Unity package smoke 增加 v0.4.15 源码断言，防止 `策划视图 / 程序视图 / 高级` 三层语义回退到旧“高级模式”。

## 0.4.14

- Unity 合并页的 GitHub PR preflight 和远端分支读取改为后台 probe + TTL 缓存；打开窗口、切 tab、刷新合并上下文不再在 IMGUI/Repaint 链路同步等待 `git` / `gh`。
- Unity lifecycle 子进程会补齐 npm global bin 到 `PATH`，并支持 `toolkit.larkCliPath` / `CONFIG_SHEET_FORGE_LARK_CLI` / `LARK_CLI_PATH`，避免 Unity 进程没继承终端 PATH 时找不到 `lark-cli`。
- Lark provider discovery 新增 `CONFIG_SHEET_FORGE_LARK_CLI`，并在运行 `lark-cli` / `npm` / `node` fallback 时使用增强后的 PATH；Windows 下继续优先识别 npm `.ps1` shim。
- PR 检查失败文案区分 `lark-cli` 缺失、doctor 失败、bot scope 缺失、资源未共享给 bot、strict bot 不允许 user fallback，不再把“本机没有找到 lark-cli”误报成“权限不足”。
- 高级诊断显示 Unity 子进程 PATH、最终识别到的 `lark-cli` 来源，以及 strict bot / user fallback 策略，便于主程定位本机环境问题。
- CLI 在 lark-cli 缺失时输出可操作中文错误，提示设置 `CONFIG_SHEET_FORGE_LARK_CLI` / `LARK_CLI_PATH` 或确认 `%APPDATA%\npm`。
- Unity package smoke 增加 v0.4.14 源码断言，防止合并页 preflight 回退到 UI 线程同步外部命令，防止 lark-cli resolver/PATH 注入回退。

## 0.4.13

- 合并页目标分支改为可搜索列表，支持按分支名过滤；识别到 GitHub PR 时主界面固定使用 PR base branch，手动覆盖只放在高级选项。
- 新增 GitHub PR 识别 preflight：明确提示 git、GitHub remote、gh 安装和 gh 登录状态；gh 缺失时不阻断同步/新建/seed，只提示手动选择目标分支。
- 新建配表页改为结构化表单：负责人角色从项目 `roles` 读取并显示中文名，审批规则只读展示；字段用行编辑器增删、排序、选择类型。
- 字段类型支持策划视图和程序视图：策划视图显示文本、整数、小数、是/否、日期、日期时间、枚举、JSON；程序视图显示 canonical 类型和内部 key。
- 枚举字段提供结构化枚举值编辑，并按现有 `enum:a,b,c` 模板写入 contract；字段 key、中文名、说明、类型、枚举值和唯一 ID 都会实时校验。
- 新建配表本地 Excel cache 路径在普通界面只读自动推导，高级入口才允许覆盖，避免和旧 Excel Seed 混淆。
- 运行中状态卡增强：显示操作、人话阶段、安全性、已用时间、取消按钮，并在耗时较长时提示可切到输出页查看日志。
- Project config summary 新增可选读取 `roles`、`newTable.defaultOwnerRole`、`newTable.supportedFieldTypes`、`newTable.defaultFields`、`github.requiredForPrAutoDetect` 和 `github.installHelpUrl`。
- Unity package smoke 增加 v0.4.13 源码断言，防止目标分支选择器、新建配表表单、运行中状态和旧 debug 文案回退。

## 0.4.12

- 修复非“输出”页底部“最近结果”收起后仍占大块高度的问题；collapsed 状态现在只绘制 34px 状态条，主工作区会回收高度。
- 展开态才绘制底部结果抽屉，抽屉高度按窗口 25%-35% 左右取默认值，并支持拖拽 splitter 调整。
- 底部结果抽屉展开/收起状态与用户拖拽高度写入 `EditorPrefs`，不会自动调整 Unity EditorWindow 外部尺寸。
- collapsed 状态条显示运行中/成功/失败、当前 operation 和下一步，并保留复制输出、打开 result、打开 lifecycle 目录、展开以及运行中取消按钮。
- 非输出页不再在 collapsed 路径创建日志 ScrollView；完整命令、stdout、stderr、result JSON 继续放在展开抽屉或“输出”tab。
- 首页改为任务型 Dashboard：突出“推荐下一步”，提供一个主按钮、一个次按钮和安全说明，并增加“我该做什么”流程卡。
- 新增首次打开 onboarding，说明飞书在线表是 Source of Truth、预览安全、写入/创建/写回需要确认；支持“我知道了 / 打开教程 / 不再提示”。
- 顶部 `教程` 菜单支持通用教程、策划改表、新建配表、PR 合并、常见失败原因，并优先读取项目配置里的 `documentationTargets` / `localDocs` / `feishuRootUrl`。
- 每个主 tab 顶部增加一句任务定位说明，帮助普通用户判断什么时候进入状态、配表、合并、PR 检查或输出页。
- 状态卡和失败原因继续隐藏默认 debug 术语，失败卡附带“下一步”。
- Unity package smoke 增加布局源码断言，防止回退到 collapsed 状态仍使用大面板/ScrollView/ExpandHeight 的旧布局。

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
