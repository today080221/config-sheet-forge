# 便携子集

便携子集是可以跨 provider 移动、可稳定合并、可进入 CI gate 的工作簿语义。

## 支持类型

- `string`
- `number`
- `integer`
- `bool`
- `date`
- `datetime`
- `enum`
- `json`

`text`、`boolean` 等常见别名会规范化到便携类型。公式列、图片、合并单元格、富文本、跨表引用、@人、@文档、日期对象和不支持的单元格类型都会阻断导出。

错误会定位到表和 A1 单元格/范围，并说明怎么修，例如：

```text
SkillsData!C12 使用了公式。配表导出只支持普通值，请把公式结果复制为纯文本或数字。
```

## 类型行

推荐表格布局：

- 第 0 行：字段名。
- 第 1 行：字段类型。
- 第 2 行：字段说明。
- 第 3 行起：数据。

CLI 使用从 0 开始的行号配置：`--field-row 0 --type-row 1 --description-row 2 --data-start-row 3`。

## 稳定 ID

每个可编辑行都需要稳定 id。优先使用 `id` 或 `key` 列。

推荐：

```text
item_sword
enemy_slime_01
reward_daily_login
```

风险较高：

```text
row 7
new item
Sword
```

## 字段 key

字段 key 必须匹配：

```text
^[A-Za-z_][A-Za-z0-9_]*$
```

展示名可以友好，但 key 要稳定。

## 语义 hash

语义 hash 基于 sheet、列定义、稳定行 id 和规范化值。它忽略行顺序和 provider 噪声。只改格式不应改变 hash；改 gameplay 值应该改变 hash。
