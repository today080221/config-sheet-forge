# Human Guide

This page is for designers, planners, and other non-program users who edit config sheets.

## You Can Usually Change

- Text shown to players.
- Numeric balance values.
- Enum-like choices from an agreed list.
- Boolean switches such as enabled/disabled.
- New rows, if you give each row a unique stable id.

## Ask Before Changing

- Column names.
- The `id` or `key` of an existing row.
- Formula columns.
- Sheet names.
- Hidden columns that look technical.
- Any value that looks like a token, password, URL, owner route, or private system id.

## Do Not Put In Sheets

- Access tokens or app secrets.
- Personal account credentials.
- Private team routing rules.
- One-off business history that should not ship in an open source repository.

## Common Error Messages

`A row is missing a stable id`

Add a value in the id/key column. The tool needs it to match old and new versions safely.

`Two rows have the same stable id`

Change one id. Every editable row needs a unique id.

`Column keys must use letters, numbers, and underscores`

Rename the machine key. Good examples are `display_name`, `power`, and `unlock_level`.

`This column type is not part of the portable subset`

Ask an engineer whether this should become text, number, bool, date, enum, or json.

## When A Merge Conflicts

The merge report tells you the sheet, row, and column. Open the compared versions, choose the correct value, then run the gate again.
