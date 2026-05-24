# Getting Started

Config Sheet Forge keeps a local project configuration and a local table registry. Both live under `.config-sheet-forge/` and are ignored by git because they can contain tenant-specific document ids or private URLs.

## Install Requirements

- .NET 8 SDK or newer for the CLI
- `lark-cli` for Feishu/Lark provider access
- Unity 2021.3 or newer for the UPM package

Build from source:

```bash
dotnet build ConfigSheetForge.sln
```

Run the CLI from source:

```bash
dotnet run --project src/cli/ConfigSheetForge.Cli -- doctor
```

Pack as a local .NET tool:

```bash
dotnet pack src/cli/ConfigSheetForge.Cli -c Release
```

## First Project Setup

Create local templates:

```bash
config-sheet-forge init
```

Check the machine and provider setup:

```bash
config-sheet-forge doctor
```

Find possible roots:

```bash
config-sheet-forge discover-root --query "config root"
```

The command only lists candidates. A human must choose the right root and put it in `.config-sheet-forge/config.json`.

## Register A Table

```bash
config-sheet-forge new-table --id items --name Items --spreadsheet "<sheet-url-or-token>" --sheet-id "<sheet-id>" --range "A1:Z500"
```

Then sync and gate:

```bash
config-sheet-forge sync --table items
config-sheet-forge gate
```

## Unity

Add the package from this repository with the package path `packages/unity`, then open `Tools > Config Sheet Forge`.

The Unity window calls the same shared core assembly for local checks and delegates provider actions to the CLI.
