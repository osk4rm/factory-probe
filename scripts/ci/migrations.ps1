#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Fails when an EF Core migration is not backward compatible with the previous app version.

.DESCRIPTION
  This is the gate behind the deployment design (ADR-018 in the factory docs): every
  migration must be expand/contract, because that is what makes a rollback of the
  *application* possible without a rollback of the *database*. Drop a column and the
  previous image, still running for the length of a deploy, fails on its next query.

  It reads the generated migration sources rather than a live database, so it runs in
  seconds, needs no PostgreSQL, and can gate every pull request.

  Destructive operations are not banned outright — sometimes they are exactly what a
  release is for. They are banned *in the same release as the code that stops using
  the old shape*. The escape hatch is a written reason:

      // swarm:destructive-ok the column has not been written since v1.2 and was dropped from the model

  A reason is required, and a reviewer sees it in the diff. That is the whole
  mechanism: make the dangerous thing possible, visible and deliberate.

.EXAMPLE
  pwsh -File scripts/ci/migrations.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$MigrationsPath = 'apps/api/Data/Migrations'
)

$ErrorActionPreference = 'Stop'

$migrationDir = Join-Path $Root $MigrationsPath
if (-not (Test-Path $migrationDir)) {
    Write-Host "no migrations directory at $migrationDir" -ForegroundColor Yellow
    exit 0
}

# Operations that break a running previous version. `AlterColumn` is included
# because it is the usual way a type narrows; a widening change is rare enough that
# annotating it is cheaper than the gate trying to infer intent from generated code.
$destructive = @(
    @{ Pattern = 'migrationBuilder\.DropColumn';        What = 'DropColumn' }
    @{ Pattern = 'migrationBuilder\.DropTable';         What = 'DropTable' }
    @{ Pattern = 'migrationBuilder\.DropSchema';        What = 'DropSchema' }
    @{ Pattern = 'migrationBuilder\.RenameColumn';      What = 'RenameColumn' }
    @{ Pattern = 'migrationBuilder\.RenameTable';       What = 'RenameTable' }
    @{ Pattern = 'migrationBuilder\.AlterColumn';       What = 'AlterColumn' }
    @{ Pattern = '(?i)migrationBuilder\.Sql\(\s*@?"[^"]*\b(DROP|TRUNCATE)\b'; What = 'raw SQL DROP/TRUNCATE' }
)

$annotation = '//\s*swarm:destructive-ok\s+(?<reason>\S.*)$'

# `*.Designer.cs` and the model snapshot are generated descriptions of the *result*,
# not operations, and they legitimately contain the word AlterColumn.
$files = Get-ChildItem -Path $migrationDir -Filter '*.cs' -File |
    Where-Object { $_.Name -notlike '*.Designer.cs' -and $_.Name -ne 'AppDbContextModelSnapshot.cs' }

$violations = [System.Collections.Generic.List[object]]::new()
$annotated = 0

foreach ($file in $files) {
    $lines = Get-Content -Path $file.FullName
    $reason = $null
    foreach ($line in $lines) {
        $m = [regex]::Match($line, $annotation)
        if ($m.Success) { $reason = $m.Groups['reason'].Value.Trim() }
    }

    # Only Up() describes a forward operation. Down() exists precisely to reverse the
    # migration — its DropTable/DropColumn are the rollback path, and scanning it made
    # this gate fail on the very first migration of the template, which is how the
    # omission was found.
    $upStart = -1
    $upEnd = $lines.Count
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($upStart -lt 0 -and $lines[$i] -match 'override\s+void\s+Up\s*\(') { $upStart = $i }
        elseif ($upStart -ge 0 -and $lines[$i] -match 'override\s+void\s+Down\s*\(') { $upEnd = $i; break }
    }
    if ($upStart -lt 0) {
        Write-Host "  warning: $($file.Name) has no Up() method; skipped" -ForegroundColor Yellow
        continue
    }

    for ($i = $upStart; $i -lt $upEnd; $i++) {
        foreach ($rule in $destructive) {
            if ($lines[$i] -match $rule.Pattern) {
                if ($reason) {
                    $annotated++
                    Write-Host "  allowed: $($file.Name):$($i + 1) $($rule.What) — $reason" -ForegroundColor DarkGray
                } else {
                    $violations.Add([pscustomobject]@{
                        File = $file.Name
                        Line = $i + 1
                        What = $rule.What
                        Text = $lines[$i].Trim()
                    })
                }
                break
            }
        }
    }
}

if ($violations.Count -eq 0) {
    $suffix = if ($annotated -gt 0) { " ($annotated annotated operation(s) allowed)" } else { '' }
    Write-Host "migrations are backward compatible$suffix" -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host 'Destructive migration operations without a written reason:' -ForegroundColor Red
foreach ($violation in $violations) {
    Write-Host ("  {0}:{1}  {2}" -f $violation.File, $violation.Line, $violation.What) -ForegroundColor Red
    Write-Host ("     {0}" -f $violation.Text) -ForegroundColor DarkGray
}
Write-Host ''
Write-Host @'
Split the change into two releases:

  1. this release  — stop reading/writing the old shape; ship the new one alongside it
  2. a later one   — drop the old shape, when no deployed version uses it

If the operation is genuinely safe right now, say why on its own line in the migration:

  // swarm:destructive-ok <the reason a reviewer can check>

Why this gate exists: rolling back the application is only safe while the database
still supports the previous version. A destructive migration in the same release
silently removes that option, and you find out during an incident.
'@ -ForegroundColor Yellow
exit 1
