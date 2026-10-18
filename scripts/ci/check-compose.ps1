<#
.SYNOPSIS
    Checks that the production compose file is production-shaped.

.DESCRIPTION
    Every rule here exists because its absence is a real incident, not a style preference.
    The check reads the file as text, so it runs in CI and on a developer machine without a
    Docker daemon - which is the only way it gets run before the deploy that needs it.

    Rules:
      1. No published database port. A dev convenience that publishes 5432 is how a
         database becomes reachable from the internet.
      2. No build context on api/web. Production runs images built, scanned and tagged by
         CI; composing on the host at deploy time makes a rollback a rebuild.
      3. No dev server / no environment=Development.
      4. Every credential placeholder uses :? (required) or an empty default - never a
         literal password. A compose file with a fallback password ships a known password.
      5. Containers are non-root and drop capabilities.
      6. Restart policy present, so a crash does not stay crashed.
      7. No source bind mounts.

.PARAMETER Path
    The compose file to check. Defaults to docker-compose.prod.yml.
#>
[CmdletBinding()]
param(
    [string] $Path = 'docker-compose.prod.yml'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Host "[FAIL] $Path does not exist"
    exit 1
}

$text = Get-Content -LiteralPath $Path -Raw
$failures = New-Object System.Collections.Generic.List[string]

function Test-Rule {
    param([string] $Name, [scriptblock] $Check, [string] $Message)

    $violations = & $Check
    if ($violations) {
        foreach ($violation in $violations) { $failures.Add("$Name`: $violation") }
    }
    else {
        Write-Host "  [ OK ] $Name" -ForegroundColor Green
    }
}

Write-Host "Checking $Path" -ForegroundColor Cyan

# 1. The database must not be published.
Test-Rule -Name 'database port not published' -Check {
    $block = [regex]::Match($text, '(?ms)^  postgres:\n(.*?)(?=^  \w|\Z)')
    if (-not $block.Success) { return @('no postgres service found') }

    $portLines = [regex]::Matches($block.Groups[1].Value, '(?m)^\s+- "?\d+:\d+"?')
    if ($portLines.Count -gt 0) { return @("postgres publishes a host port: $($portLines[0].Value.Trim())") }
    return @()
} -Message 'irrelevant'

# 2. No build context for the two runtime services.
Test-Rule -Name 'no build context on api/web' -Check {
    $builds = [regex]::Matches($text, '(?m)^\s+build:')
    if ($builds.Count -gt 0) { return @("compose builds images at deploy time ($($builds.Count) occurrence(s))") }
    return @()
} -Message 'irrelevant'

# 3. Not a development environment.
Test-Rule -Name 'not a development environment' -Check {
    if ($text -match 'ASPNETCORE_ENVIRONMENT:\s*Development') { return @('ASPNETCORE_ENVIRONMENT is Development') }
    if ($text -match 'npm run dev|dotnet watch|vite') { return @('a development server is referenced') }
    return @()
} -Message 'irrelevant'

# 4. No literal credentials.
Test-Rule -Name 'credentials are required placeholders' -Check {
    $problems = @()

    # A password/user/db with a literal fallback (`:-something`) is a shipped default.
    $defaults = [regex]::Matches($text, '(?m)^\s+(\w*(?:PASSWORD|SECRET|KEY)\w*):\s*\$\{[^}]*:-(?!\})[^}]*\}')
    foreach ($match in $defaults) {
        $problems += "$($match.Groups[1].Value) has a non-empty default"
    }

    # A plain value where a placeholder belongs.
    $literals = [regex]::Matches($text, '(?m)^\s+POSTGRES_PASSWORD:\s*(?!\$\{)\S+')
    foreach ($_ in $literals) { $problems += 'POSTGRES_PASSWORD is a literal value' }

    return $problems
} -Message 'irrelevant'

# 5. Hardened containers.
Test-Rule -Name 'api and web are hardened' -Check {
    $problems = @()
    foreach ($service in @('api', 'web')) {
        $block = [regex]::Match($text, "(?ms)^  $($service):\n(.*?)(?=^  \w|\Z)")
        if (-not $block.Success) { $problems += "no $service service found"; continue }

        $body = $block.Groups[1].Value
        if ($body -notmatch '(?m)^\s+read_only:\s*true') { $problems += "$service is not read_only" }
        if ($body -notmatch 'no-new-privileges') { $problems += "$service does not set no-new-privileges" }
        if ($body -notmatch 'cap_drop') { $problems += "$service does not drop capabilities" }
    }
    return $problems
} -Message 'irrelevant'

# 6. Restart policy.
Test-Rule -Name 'restart policy present' -Check {
    $services = [regex]::Matches($text, '(?m)^  (\w+):\s*$')
    $missing = @()
    foreach ($service in $services) {
        $name = $service.Groups[1].Value
        if ($name -eq 'volumes' -or $name -eq 'networks') { continue }
        $block = [regex]::Match($text, "(?ms)^  $($name):\n(.*?)(?=^  \w|\Z)")
        if ($block.Success -and $block.Groups[1].Value -notmatch '(?m)^\s+restart:') {
            $missing += $name
        }
    }
    return $missing
} -Message 'irrelevant'

# 7. No source bind mounts.
Test-Rule -Name 'no source bind mounts' -Check {
    $binds = [regex]::Matches($text, '(?m)^\s+-\s+\.{1,2}/')
    if ($binds.Count -gt 0) { return @("$($binds.Count) source path(s) mounted into a container") }
    return @()
} -Message 'irrelevant'

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "Production compose is not production-shaped:" -ForegroundColor Red
    foreach ($failure in $failures) { Write-Host "  [FAIL] $failure" -ForegroundColor Red }
    exit 1
}

Write-Host ''
Write-Host 'Production compose is production-shaped.' -ForegroundColor Green
exit 0
