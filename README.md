# Config Sheet Forge

Config Sheet Forge is a personal open source tool for treating Feishu/Lark config sheets as a source of truth without baking team-specific links, owners, table names, or secrets into the repository.

It ships as:

- a Unity UPM package under `packages/unity`
- a cross-platform .NET CLI under `src/cli`
- a shared semantic workbook core under `src/core`
- a Lark/Feishu provider under `src/providers/lark`

The first provider is backed by `lark-cli`. The core keeps provider contracts separate so other document systems can be added later.

## Quick Start

```bash
dotnet build ConfigSheetForge.sln
dotnet run --project src/cli/ConfigSheetForge.Cli -- init
dotnet run --project src/cli/ConfigSheetForge.Cli -- doctor
dotnet run --project src/cli/ConfigSheetForge.Cli -- discover-root --query "config root"
```

After confirming a root manually, register a table:

```bash
dotnet run --project src/cli/ConfigSheetForge.Cli -- new-table --id items --name Items --spreadsheet "<sheet-url-or-token>" --sheet-id "<sheet-id>" --range "A1:Z500"
dotnet run --project src/cli/ConfigSheetForge.Cli -- sync --table items
dotnet run --project src/cli/ConfigSheetForge.Cli -- gate
```

Local state is written under `.config-sheet-forge/` and is ignored by git.

## Unity UPM Install

After this repository is pushed to GitHub and tagged, install the Unity package with:

```text
https://github.com/today080221/config-sheet-forge.git?path=/packages/unity#v0.1.0
```

Then open `Tools > Config Sheet Forge`.

## Safety Rules

- Do not commit access tokens, app secrets, real team links, private business table contents, or owner routing.
- Root discovery only recommends candidates. A human must confirm the source of truth root.
- Errors shown to non-program users should explain what to fix. Low-level ids, hashes, and revisions belong in details.
- All project-specific values belong in local config or CI secrets, never in source code.

## Documentation

- [Getting Started](docs/getting-started.md)
- [Configuration](docs/configuration.md)
- [Human Guide](docs/human-guide.md)
- [Portable Subset](docs/portable-subset.md)
- [Merge Policy](docs/merge-policy.md)
- [CI Gate](docs/ci-gate.md)
- [Lark Provider](docs/providers/lark.md)

## License

Apache-2.0.
