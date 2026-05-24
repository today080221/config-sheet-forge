$ErrorActionPreference = "Stop"

$patterns = @(
  "NoTimeToDie",
  "CodeTime3D",
  "N:\\GameDemo",
  "O:\\OneDrive",
  "cli_a94591b21a391bda",
  "appSecret",
  "access_token",
  "refresh_token",
  "oc_[0-9a-f]{8,}",
  "ou_[0-9a-f]{8,}"
)

$argsList = @("--hidden", "--line-number", "--glob", "!/.git/*", "--glob", "!**/bin/**", "--glob", "!**/obj/**", "--glob", "!scripts/Check-NoPrivateContent.ps1")

$failed = $false
foreach ($pattern in $patterns) {
  $output = & rg @argsList --regexp $pattern .
  if ($LASTEXITCODE -eq 0) {
    Write-Error "Potential private content matched pattern '$pattern':`n$output"
    $failed = $true
  } elseif ($LASTEXITCODE -ne 1) {
    throw "rg failed while scanning for pattern '$pattern'"
  }
}

if ($failed) {
  exit 1
}

Write-Host "No private content patterns found."
