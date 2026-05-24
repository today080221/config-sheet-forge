$ErrorActionPreference = "Stop"

dotnet build ConfigSheetForge.sln
dotnet run --project tests/ConfigSheetForge.Tests
pwsh scripts/Validate-UnityPackage.ps1
pwsh scripts/Check-NoPrivateContent.ps1
