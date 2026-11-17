param(
    [string]$AppSettings = "$PSScriptRoot\..\PanelForge_FA26SE251\src\PanelForge.API\appsettings.Development.json",
    [string]$PgAdminDir  = "C:\Program Files\PostgreSQL\18\pgAdmin 4",
    [string]$Group       = "PanelForge"
)

# Registers the backend's PostgreSQL connections (Local + Docker) in pgAdmin 4 desktop
# and writes their passwords to libpq's pgpass.conf, so pgAdmin connects without prompting.
# Safe to re-run: servers already registered under the same name are skipped, never replaced.

$ErrorActionPreference = 'Stop'
$python   = Join-Path $PgAdminDir 'python\python.exe'
$setupPy  = Join-Path $PgAdminDir 'web\setup.py'
$sqlite   = Join-Path $env:APPDATA 'pgAdmin\pgadmin4.db'
$pgpass   = Join-Path $env:APPDATA 'postgresql\pgpass.conf'
$desktopUser = 'pgadmin4@pgadmin.org'   # fixed user pgAdmin uses in desktop mode
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
# pgAdmin's bundled Python loads libpq.dll from runtime\, which the GUI puts on PATH itself
$env:PATH = (Join-Path $PgAdminDir 'runtime') + ';' + $env:PATH

foreach ($p in @($python, $setupPy, $AppSettings)) {
    if (-not (Test-Path $p)) { throw "Not found: $p" }
}
if (-not (Test-Path $sqlite)) {
    throw "pgAdmin config DB not found at $sqlite. Open pgAdmin once so it creates it, then re-run."
}

function ConvertFrom-ConnectionString([string]$cs) {
    $map = @{}
    foreach ($part in $cs.Split(';')) {
        $kv = $part.Split('=', 2)
        if ($kv.Count -eq 2) { $map[$kv[0].Trim().ToLower()] = $kv[1].Trim() }
    }
    $map
}

$connStrings = (Get-Content $AppSettings -Raw | ConvertFrom-Json).ConnectionStrings
$targets = @(
    @{ Name = 'PanelForge Local';  Key = 'PostgreSQL' },
    @{ Name = 'PanelForge Docker'; Key = 'PostgreSQLDocker' }
)

$tmp = Join-Path $env:TEMP "pgadmin_servers_$PID"
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $dumpFile = Join-Path $tmp 'existing.json'
    & $python $setupPy dump-servers $dumpFile --user $desktopUser --sqlite-path $sqlite | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "pgAdmin dump-servers failed (exit $LASTEXITCODE)" }
    $existing = @()
    if (Test-Path $dumpFile) {
        $dump = Get-Content $dumpFile -Raw | ConvertFrom-Json
        if ($dump.Servers) { $existing = $dump.Servers.PSObject.Properties | ForEach-Object { $_.Value.Name } }
    }

    $servers = [ordered]@{}
    $pgpassLines = New-Object System.Collections.Generic.List[string]
    if (Test-Path $pgpass) { $pgpassLines.AddRange([string[]](Get-Content $pgpass)) }

    $i = 1
    foreach ($t in $targets) {
        $raw = $connStrings.($t.Key)
        if (-not $raw) { Write-Host "Skip $($t.Name): ConnectionStrings:$($t.Key) missing" -ForegroundColor DarkYellow; continue }
        $c = ConvertFrom-ConnectionString $raw
        $port = if ($c['port']) { [int]$c['port'] } else { 5432 }

        # pgpass: host:port:database:user:password, with ':' and '\' escaped
        $escaped = $c['password'] -replace '\\', '\\' -replace ':', '\:'
        $prefix = "$($c['host']):$($port):*:$($c['username']):"
        $pgpassLines.RemoveAll({ param($l) $l.StartsWith($prefix) }) | Out-Null
        $pgpassLines.Add($prefix + $escaped)

        if ($existing -contains $t.Name) {
            Write-Host "Skip $($t.Name): already registered in pgAdmin" -ForegroundColor DarkYellow
            continue
        }
        $servers["$i"] = [ordered]@{
            Name          = $t.Name
            Group         = $Group
            Host          = $c['host']
            Port          = $port
            MaintenanceDB = 'postgres'
            Username      = $c['username']
            SSLMode       = 'prefer'
            PassFile      = $pgpass
        }
        Write-Host "Add  $($t.Name): $($c['host']):$port db=$($c['database'])" -ForegroundColor Green
        $i++
    }

    New-Item -ItemType Directory -Force (Split-Path $pgpass) | Out-Null
    [IO.File]::WriteAllLines($pgpass, $pgpassLines, $utf8NoBom)
    Write-Host "Passwords written to $pgpass"

    if ($servers.Count -gt 0) {
        $inputFile = Join-Path $tmp 'servers.json'
        [IO.File]::WriteAllText($inputFile, (@{ Servers = $servers } | ConvertTo-Json -Depth 5), $utf8NoBom)
        & $python $setupPy load-servers $inputFile --user $desktopUser --sqlite-path $sqlite
        if ($LASTEXITCODE -ne 0) { throw "pgAdmin load-servers failed (exit $LASTEXITCODE)" }
        Write-Host "Done. Restart pgAdmin (or refresh Servers) to see group '$Group'." -ForegroundColor Cyan
    }
}
finally {
    Remove-Item -Recurse -Force $tmp
}
