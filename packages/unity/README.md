# Config Sheet Forge Unity 包

这个包提供 Unity Editor 窗口，用于在 Unity 项目中运行 Config Sheet Forge 命令。

打开入口：`Tools > Config Sheet Forge`。

窗口包含：

- `Start`：首次接入 checklist、doctor、init、root discovery、gate。
- `Tables`：注册并同步 source-of-truth 表。
- `Merge`：从 semantic workbook JSON 生成三方合并报告。
- `Gate`：提交或开 PR 前执行校验。
- `Output`：查看和复制命令输出。

Editor assembly 引用 `ConfigSheetForge.Core`，也就是 CLI 编译的同一份语义工作簿 core。Provider 访问不放在 Unity 里，统一交给已安装的 `config-sheet-forge` CLI。

## 安装

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.2.0
```

## 测试

包内包含 edit-mode tests，覆盖共享 core hash、命令构造、配置路径和人话错误渲染。
