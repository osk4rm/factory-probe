#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Write-Host '==> dotnet build' -ForegroundColor Cyan
dotnet build "$root/SwarmApp.slnx" --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '==> web build' -ForegroundColor Cyan
Push-Location "$root/apps/web"
try {
    if (Test-Path package-lock.json) { npm ci } else { npm install }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    npm run build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
