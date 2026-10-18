#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Fails when a Dockerfile's runtime stage is not safe to run in production.

.DESCRIPTION
  Reading a Dockerfile is the only way to check these without a daemon, and a daemon
  is exactly what CI may not have. Four rules, each from a real failure mode:

  * No SDK in the final stage. Shipping the .NET SDK turns a ~220 MB image into ~800 MB
    and hands an attacker a compiler plus the NuGet client.
  * A non-root USER. Without it the process runs as uid 0 inside the container, which
    is one kernel bug away from the host.
  * A HEALTHCHECK. A container with no healthcheck reports "running" while its
    application is broken, and orchestrators route traffic to it.
  * No world-writable or privileged directives: chmod 777, --privileged, remote ADD.

.EXAMPLE
  pwsh -File scripts/ci/check-dockerfile.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string[]]$Files = @('apps/api/Dockerfile', 'apps/web/Dockerfile')
)

$ErrorActionPreference = 'Stop'
$failures = 0

function Get-Stages {
    param([string[]]$Lines)
    $stages = @()
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match '^\s*FROM\s+(?<image>\S+)') {
            $stages += [pscustomobject]@{ Index = $i; Image = $Matches['image'] }
        }
    }
    return $stages
}

foreach ($relative in $Files) {
    $path = Join-Path $Root $relative
    if (-not (Test-Path $path)) {
        Write-Host "[FAIL] $relative is missing" -ForegroundColor Red
        $failures++
        continue
    }

    $lines = @(Get-Content -Path $path)
    $stages = @(Get-Stages -Lines $lines)
    if ($stages.Count -eq 0) {
        Write-Host "[FAIL] $relative has no FROM stage" -ForegroundColor Red
        $failures++
        continue
    }

    $final = $stages[$stages.Count - 1]
    $finalLines = @($lines[$final.Index..($lines.Count - 1)])
    $problems = @()

    if ($final.Image -match '(?i)/sdk:') {
        $problems += "the final stage uses an SDK image ($($final.Image)); use a runtime image and copy the published output"
    }

    $userLines = @($finalLines | Where-Object { $_ -match '^\s*USER\s+\S' })
    if ($userLines.Count -eq 0) {
        $problems += 'the final stage has no USER directive, so it runs as root'
    }
    else {
        $lastUser = ($userLines[$userLines.Count - 1] -replace '^\s*USER\s+', '').Trim()
        if ($lastUser -match '^(root|0)(:0)?$') {
            $problems += "the final stage runs as '$lastUser'"
        }
    }

    if (-not (@($finalLines | Where-Object { $_ -match '^\s*HEALTHCHECK\b' }).Count)) {
        $problems += 'the final stage has no HEALTHCHECK, so a broken container reports as running'
    }

    for ($i = $final.Index; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'chmod\s+(-R\s+)?777') { $problems += "world-writable permission at line $($i + 1)" }
        if ($lines[$i] -match '^\s*ADD\s+https?://') { $problems += "remote ADD at line $($i + 1); it cannot be verified by digest" }
        if ($lines[$i] -match '--privileged') { $problems += "--privileged at line $($i + 1)" }
    }

    if ($problems.Count -eq 0) {
        $user = (($userLines[$userLines.Count - 1]) -replace '^\s*USER\s+', '').Trim()
        Write-Host "[ OK ] $relative - runtime '$($final.Image)', user '$user', healthcheck present" -ForegroundColor Green
    }
    else {
        Write-Host "[FAIL] $relative" -ForegroundColor Red
        foreach ($problem in $problems) { Write-Host "       - $problem" -ForegroundColor DarkGray }
        $failures += $problems.Count
    }
}

Write-Host ''
if ($failures -gt 0) {
    Write-Host "$failures Dockerfile problem(s)." -ForegroundColor Red
    exit 1
}
Write-Host 'Runtime stages are hardened.' -ForegroundColor Green
exit 0
