## Summary

-

## Safety

- [ ] No secrets, access tokens, private team links, owner routes, or private table contents are committed.
- [ ] Project-specific values are read from config or CI secrets.
- [ ] Root discovery recommends candidates only.

## Validation

- [ ] `dotnet build ConfigSheetForge.sln`
- [ ] `dotnet run --project tests/ConfigSheetForge.Tests`
- [ ] `pwsh scripts/Validate-UnityPackage.ps1`
- [ ] `pwsh scripts/Check-NoPrivateContent.ps1`
