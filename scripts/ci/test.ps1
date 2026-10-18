#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Write-Host '==> dotnet test' -ForegroundColor Cyan
dotnet test "$root/SwarmApp.slnx" --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '==> web test' -ForegroundColor Cyan
Push-Location "$root/apps/web"
try {
    npm test
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
