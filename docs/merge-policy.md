# Merge Policy

Config Sheet Forge uses a three-way merge:

- base: the common version
- ours: the local branch
- theirs: the incoming branch or source

## Automatic Decisions

If only one side changed a cell, that side wins.

If both sides changed a cell to the same semantic value, there is no conflict.

If both sides changed a cell differently, the merge report marks a conflict and keeps `ours` in the merged output until a human resolves it.

## Conflict Report

`config-sheet-forge merge` writes a Markdown report with:

- status
- sheet
- row id
- column key
- human-readable message

Low-level fingerprints are kept in structured details and are not required for non-program users to resolve the row.

## Row And Sheet Deletions

Deletion conflicts are conservative. If one side removes a row or sheet while the other side changes or keeps it, a human should decide.
