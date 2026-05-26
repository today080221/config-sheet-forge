# Unity 窗口 5 分钟入门

Config Sheet Forge 的 Unity 窗口是给项目成员使用的配表 Source of Truth 工作台。默认规则很简单：

- 飞书在线 Sheet 是正式源头。
- 本地 Excel / semantic JSON / hash 都是自动生成的 cache。
- `预览` 永远安全，只读取并生成计划，不写飞书、不改本地文件。
- `写入 / 创建 / 写回` 会改变状态，必须先预览成功，再手动确认。
- 不知道下一步时，先点 `预览同步计划`。

## 策划改表 5 分钟流程

1. 在当前分支对应的飞书在线表里改数据。
2. 回到 Unity，打开 `Tools > Config Sheet Forge`。
3. 在首页点 `预览同步计划`。这一步只读取，不写任何文件。
4. 预览通过后，到 `配表` 页勾选确认，再点 `写入本地 cache`。
5. 提交 PR，或找主程 / 配置负责人合并。

如果预览提示当前分支没有在线表记录，先确认这个分支是否已经 Seed；如果要迁移旧 Excel，展开 `本地 Excel Seed`。

## 新建配表流程

1. 进入 `配表` 页，展开 `新建配表`。
2. 填写配表 ID 和中文显示名。
3. 点 `预览新建配表`。这一步只生成计划，不创建在线表。
4. 找配置负责人确认 schema、负责人角色和命名。
5. 勾选确认后点 `创建在线表并登记`。

创建在线表是高风险操作，会写飞书和注册中心，所以必须二次确认，并要求最近一次同输入 dry-run 成功。

## PR 合并流程

1. 进入 `合并` 页。
2. 确认当前分支、目标分支和 PR 识别结果。
3. 点 `生成合并预览`。
4. 处理冲突、Schema review、MergeReviews 或 waiver。
5. 运行 `PR 检查`。
6. 通过后再合 PR。写回 main 必须由负责人确认。

## 安全操作和危险操作

| 操作 | 风险 | 会写东西吗 | 需要确认吗 |
| --- | --- | --- | --- |
| 刷新状态 | 安全 | 不会 | 不需要 |
| 预览同步计划 | 安全 | 不会 | 不需要 |
| 预览新建配表 | 安全 | 不会 | 不需要 |
| 生成合并预览 | 安全 | 不会写 main | 不需要 |
| 运行 PR 检查 | 安全 | 只写检查报告 | 不需要 |
| 写入本地 cache | 中风险 | 会更新本地 cache | 需要勾选确认 |
| 创建在线表并登记 | 高风险 | 会写飞书和注册中心 | 需要预览成功 + 勾选 + 二次确认 |
| 本地 Excel Seed apply | 高风险 | 会写飞书、cache、项目配置或 settings | 需要预览成功 + 勾选 + 二次确认 |
| 确认写回 main | 高风险 | 会写回目标分支工作区 | 需要负责人确认 |

## dry-run 和 apply

`dry-run` 是预览模式：只读取在线注册中心、在线表和本地状态，生成 planned actions 和报告，不写飞书、不改本地 cache、不改 ProjectSettings。

`apply` 是执行模式：只有在 UI 勾选确认、二次确认，并且最近一次同输入预览成功后才允许运行。sync-cache apply 会读取在线 Sheet、导出 xlsx、做三方一致性检查和 hash gate，通过后才可能更新本地 cache。

## 常见失败原因

| 失败原因 | 下一步 |
| --- | --- |
| 当前分支没有在线表记录 | 先确认分支是否已经 Seed；新分支先点 `预览同步计划`；旧 Excel 迁移请展开 `本地 Excel Seed`。 |
| 缺少 MergeReviews 合并审查记录 | 去 `合并` 页生成合并预览，找配置负责人完成合并审查，或申请 waiver。 |
| Schema review 未完成 | 找负责人完成 Schema review，或补充 schema 变更说明后重新运行 PR 检查。 |
| waiver 过期或无效 | 更新豁免记录，或按正常审查流程重新检查。 |
| 权限不足 | 确认 bot / lark-cli 对 Base、Wiki、Sheet 有权限后重试。 |
| 本地 cache 待同步 | 先点 `预览同步计划`，通过后再确认写入本地 cache。 |

## 项目文档入口

Unity 窗口顶部 `教程` 菜单会优先使用项目配置里的文档链接。项目可以在 `ProjectSettings/*ConfigSheetForge*.json` 中声明：

```json
{
  "documentationTargets": {
    "5 分钟入门": "docs/tooling/config-sheet-source-of-truth.md",
    "飞书项目配置表": "https://example.feishu.cn/wiki/xxxxx"
  },
  "localDocs": [
    "docs/tooling/designer-config-flow.md"
  ],
  "feishuRootUrl": "https://example.feishu.cn/wiki/xxxxx"
}
```

如果项目没有声明，窗口会提供 config-sheet-forge 通用教程和 README。
