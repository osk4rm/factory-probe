#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Write-Host '==> dotnet format' -ForegroundColor Cyan
dotnet format "$root/SwarmApp.slnx" --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '==> web lint' -ForegroundColor Cyan
Push-Location "$root/apps/web"
try {
    npm run lint
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
