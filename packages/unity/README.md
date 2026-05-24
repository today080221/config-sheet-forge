# Config Sheet Forge Unity 包

这个包提供 Unity Editor 窗口，用于在 Unity 项目中运行 Config Sheet Forge 命令。

打开入口：`Tools > Config Sheet Forge > 打开同步窗口`。

窗口包含：

- `状态`：只读刷新项目配置、doctor、root discovery、gate。
- `配表`：注册并同步 source-of-truth 表；项目正式流程建议由 contract 驱动。
- `合并`：从 semantic workbook JSON 生成三方合并报告。
- `PR 检查`：提交或开 PR 前执行校验并生成 gate report。
- `输出`：查看和复制命令输出。

Editor assembly 引用 `ConfigSheetForge.Core`，也就是 CLI 编译的同一份语义工作簿 core。Provider 访问不放在 Unity 里，统一交给已安装的 `config-sheet-forge` CLI。

## 安装

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.3.0
```

## 测试

包内包含 edit-mode tests，覆盖共享 core hash、命令构造、配置路径和人话错误渲染。
