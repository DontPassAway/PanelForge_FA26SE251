param(
    [string]$HostName = "localhost",
    [int]$Port = 5432,
    [string]$User = "postgres",
    [string]$Database = "panelforge_dev",
    [string]$DumpFile = (Join-Path $PSScriptRoot "panelforge_full.sql"),
    [string]$PsqlPath = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
)

$env:PGPASSWORD = '123456'

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       DATABASE & DUMP FILE VERIFICATION SCRIPT           " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $DumpFile)) {
    Write-Error "File dump not found: $DumpFile"
    exit 1
}

if (-not (Test-Path $PsqlPath)) {
    Write-Error "psql not found at: $PsqlPath"
    exit 1
}

Write-Host "[1/3] Reading dump file: $DumpFile ..." -ForegroundColor Yellow
$dumpCounts = @{}
$reader = [System.IO.File]::OpenText($DumpFile)
$currentTable = $null

try {
    while (($line = $reader.ReadLine()) -ne $null) {
        if ($line -match '^COPY\s+public\.("?[a-zA-Z0-9_]+"?)') {
            $tableName = $matches[1].Trim('"')
            $currentTable = $tableName
            if (-not $dumpCounts.ContainsKey($currentTable)) {
                $dumpCounts[$currentTable] = 0
            }
        }
        elseif ($line -eq '\.') {
            $currentTable = $null
        }
        elseif ($null -ne $currentTable) {
            $dumpCounts[$currentTable]++
        }
    }
}
finally {
    $reader.Close()
}

Write-Host "-> Found $($dumpCounts.Count) tables in dump file.`n" -ForegroundColor Green

Write-Host "[2/3] Querying table row counts from database '$Database' ..." -ForegroundColor Yellow
$dbCounts = @{}
$tables = $dumpCounts.Keys | Sort-Object

$quote = [char]34

foreach ($tbl in $tables) {
    $quotedTbl = $quote + $tbl + $quote
    $query = "SELECT count(*) FROM public.$quotedTbl;"
    $res = $query | & $PsqlPath -h $HostName -p $Port -U $User -d $Database -t -A 2>$null
    if ($LASTEXITCODE -eq 0 -and $res -match '^\d+$') {
        $dbCounts[$tbl] = [int]$res.Trim()
    } else {
        $dbCounts[$tbl] = -1
    }
}

Write-Host "[3/3] Detailed Comparison:`n" -ForegroundColor Yellow

$formatHeader = "{0,-32} | {1,12} | {2,12} | {3,-15}"
Write-Host ($formatHeader -f "Table Name", "Dump File", "Database", "Status") -ForegroundColor White
Write-Host ("-" * 78) -ForegroundColor Gray

$matchCount = 0
$mismatchCount = 0

foreach ($tbl in $tables) {
    $dumpVal = $dumpCounts[$tbl]
    $dbVal = $dbCounts[$tbl]

    $status = ""
    $statusColor = "Green"

    if ($dbVal -eq -1) {
        $status = "MISSING TABLE"
        $statusColor = "Red"
        $mismatchCount++
    }
    elseif ($dumpVal -eq $dbVal) {
        $status = "MATCH (OK)"
        $statusColor = "Green"
        $matchCount++
    }
    else {
        $diff = $dbVal - $dumpVal
        $diffStr = if ($diff -gt 0) { "+$diff" } else { "$diff" }
        $status = "DIFF ($diffStr)"
        $statusColor = "Yellow"
        $mismatchCount++
    }

    $dumpValStr = [string]$dumpVal
    $dbValStr = if ($dbVal -eq -1) { "N/A" } else { [string]$dbVal }

    $rowPrefix = "{0,-32} | {1,12} | {2,12} | " -f $tbl, $dumpValStr, $dbValStr
    Write-Host $rowPrefix -NoNewline
    Write-Host ("{0,-15}" -f $status) -ForegroundColor $statusColor
}

Write-Host ("-" * 78) -ForegroundColor Gray
Write-Host "`nSummary:" -ForegroundColor Cyan
Write-Host " - Fully matched tables: $matchCount" -ForegroundColor Green
if ($mismatchCount -gt 0) {
    Write-Host " - Mismatched or missing tables: $mismatchCount" -ForegroundColor Yellow
}

Write-Host "`nChecking latest Migration (__EFMigrationsHistory):" -ForegroundColor Cyan
$migrationQuery = "SELECT " + $quote + "MigrationId" + $quote + " FROM public." + $quote + "__EFMigrationsHistory" + $quote + " ORDER BY 1 DESC LIMIT 1;"
$lastMigration = $migrationQuery | & $PsqlPath -h $HostName -p $Port -U $User -d $Database -t -A 2>$null
if ($lastMigration) {
    Write-Host " -> Latest Migration in DB: $lastMigration" -ForegroundColor Green
} else {
    Write-Host " -> No migration found in DB." -ForegroundColor Red
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
