$ErrorActionPreference = "Stop"

dotnet build ConfigSheetForge.sln
dotnet run --project tests/ConfigSheetForge.Tests
if (Test-Path apps/desktop/package.json) {
  Push-Location apps/desktop
  try {
    if (Test-Path package-lock.json) {
      npm ci
    }
    else {
      npm install
    }
    npm run lint
    npm run build
    if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
      throw "cargo is required for the Tauri Desktop compile smoke."
    }
    cargo check --manifest-path src-tauri/Cargo.toml
  }
  finally {
    Pop-Location
  }

  $desktopPackage = Get-Content -Raw apps/desktop/package.json | ConvertFrom-Json
  pwsh scripts/Package-DesktopRelease.ps1 -Version "v$($desktopPackage.version)" -OutputDirectory artifacts/desktop-ci-smoke
}
pwsh scripts/Validate-UnityPackage.ps1
pwsh scripts/Check-NoPrivateContent.ps1
