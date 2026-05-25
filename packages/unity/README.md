# Config Sheet Forge Unity 包

这个包提供 Unity Editor 窗口，用于在 Unity 项目中运行 Config Sheet Forge 命令。

打开入口：`Tools > Config Sheet Forge` 或 `Tools > Config Sheet Forge > 打开同步窗口`。

窗口包含：

- `状态`：只读刷新项目配置、doctor、root discovery、gate。
- `配表`：发现 `ProjectSettings/*ConfigSheetForge*.json` 且配置了 adapter 时，走项目 adapter 生成 `new-table` lifecycle contract dry-run；否则提供 generic registry fallback。
- `合并`：项目 adapter 模式下生成 `compare-merge` lifecycle contract 预览；否则从 semantic workbook JSON 生成三方合并报告。
- `PR 检查`：项目 adapter 模式下生成 `pr-gate-report`；否则执行 generic gate。
- `输出`：查看和复制命令输出。

Editor assembly 引用 `ConfigSheetForge.Core`，也就是 CLI 编译的同一份语义工作簿 core。Provider 访问不放在 Unity 里，统一交给已安装的 `config-sheet-forge` CLI。

稳定菜单契约：

- `Tools > Config Sheet Forge`
- `Tools > Config Sheet Forge > 打开同步窗口`
- `Tools > Config Sheet Forge > 新建配表向导`
- `Tools > Config Sheet Forge > 三方比较与合并`
- `Tools > Config Sheet Forge > PR 同步检查`

稳定 Editor API：

- `ConfigSheetForgeEditorApi.OpenStatusWindow()`
- `ConfigSheetForgeEditorApi.OpenNewTableWizard()`
- `ConfigSheetForgeEditorApi.OpenCompareMerge()`
- `ConfigSheetForgeEditorApi.OpenPrGate()`

下游 Unity 项目推荐只保留薄菜单 adapter 和项目 config。通用窗口、向导、contract 执行、三方比较、gate UI 都由本包维护；项目 adapter 只负责把项目配置转成 lifecycle contract。

项目 adapter 模式会把窗口输入写入 `Temp/ConfigSheetForge/unity-lifecycle/<operation>.inputs.json`，并向 adapter 传 `--inputs <path>`，避免把字段模板等复杂数据塞进长 inline JSON 参数。`pr-gate-report` 会额外生成标准 `Temp/ConfigSheetForge/pr-gate-report.json`，该文件是 `PrGateReport` 本体，供项目 gate wrapper / CI 直接读取。

接入时注意：Unity UPM 重新 resolve `packages-lock.json` 时，可能顺带刷新其它 git dependency 的 hash。这通常不是 Config Sheet Forge 包本身的改动；请在 PR 里单独核对 manifest/lock diff。

## 安装

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.3.0
```

## 测试

包内包含 edit-mode tests，覆盖共享 core hash、命令构造、配置路径和人话错误渲染。
