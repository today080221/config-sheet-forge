# 合并策略

Config Sheet Forge 使用三方合并：

- `base`：共同祖先版本。
- `ours`：当前分支版本。
- `theirs`：传入分支或传入来源。

## 自动决策

只有一边改了单元格时，采用改动方。

两边改成同一个语义值时，不产生冲突。

两边改成不同语义值时，合并报告标记冲突，merged 文件暂时保留 `ours`，等待人工确认。

## 冲突报告

`config-sheet-forge merge` 会写 Markdown 报告，包含：

- 状态。
- sheet。
- 行 id。
- 列 key。
- 人能读懂的说明。

低层 fingerprint 保留在 structured details 中，非程序用户无需读取。

## 删除行和删除 sheet

删除冲突采用保守策略。一边删除、另一边修改或保留时，需要人工决定。
