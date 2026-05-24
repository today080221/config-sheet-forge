# Portable Subset

The portable subset is the part of a workbook that can move between providers and can be merged safely.

## Supported Column Kinds

- `string`
- `text`
- `number`
- `integer`
- `bool`
- `boolean`
- `date`
- `datetime`
- `enum`
- `json`

Formula columns are warning-level because they often depend on provider-specific behavior. Keep formulas out of source-of-truth fields unless the project has a review rule for them.

## Stable IDs

Every editable row needs a stable id. Prefer an `id` or `key` column.

Good:

```text
item_sword
enemy_slime_01
reward_daily_login
```

Risky:

```text
row 7
new item
Sword
```

## Column Keys

Column keys must match:

```text
^[A-Za-z_][A-Za-z0-9_]*$
```

Display names can be friendly, but keys should stay stable.

## Semantic Hash

The semantic hash ignores row order and provider-specific noise. It is based on sheet names, column definitions, row ids, and normalized cell values.

Changing formatting only should not change the semantic hash. Changing a gameplay value should.
