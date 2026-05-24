# CI Gate

The CI gate should block changes that make config sheets unsafe to ship.

Recommended checks:

```bash
dotnet build ConfigSheetForge.sln
dotnet run --project tests/ConfigSheetForge.Tests
dotnet run --project src/cli/ConfigSheetForge.Cli -- gate --cache .config-sheet-forge/cache
pwsh scripts/Check-NoPrivateContent.ps1
```

`gate` validates semantic workbook JSON files and fails on portable subset errors.

## What CI Should Not Do

CI should not silently choose a Feishu/Lark root. If CI syncs from a real provider, the root must come from reviewed config or CI secrets.

CI should not print raw access tokens or app secrets.

CI should not commit xlsx exports from private tenant data into the open repository.
