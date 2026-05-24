# 给非程序用户的说明

这页给策划、设计和其他会编辑配置表的人看。

## 通常可以改

- 展示给玩家的文本。
- 数值平衡字段。
- 已约定枚举列表中的选项。
- `enabled` 这类开关。
- 新增行，但每行必须有唯一稳定 `id` 或 `key`。

## 改之前先问

- 列名和字段 key。
- 现有行的 `id` 或 `key`。
- 公式列。
- sheet 名称。
- 看起来像技术用途的隐藏列。
- token、password、URL、owner route、私有系统 ID。

## 不要放进表里

- access token 或 app secret。
- 个人账号凭据。
- 私有团队路由规则。
- 不应该进入开源仓库的一次性业务历史。

## 常见错误

`A row is missing a stable id`

在 id/key 列补一个值。工具需要它安全匹配新旧版本。

`Two rows have the same stable id`

改掉其中一个 id。每一行都需要唯一稳定 id。

`Column keys must use letters, numbers, and underscores`

修改机器字段名。推荐形如 `display_name`、`power`、`unlock_level`。

`This column type is not part of the portable subset`

找工程确认是否应改成 `string`、`number`、`integer`、`bool`、`date`、`datetime`、`enum` 或 `json`。

## 合并冲突时

合并报告会写出 sheet、行 id、列 key 和人能读懂的说明。打开两边版本，选出正确值，再重新跑 gate。
