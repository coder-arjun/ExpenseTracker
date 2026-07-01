<#
.SYNOPSIS
    Load a downloaded server backup (.json.gz) into a LOCAL SQL Server database.

.DESCRIPTION
    Turns a pulled snapshot into a real, queryable local database: it ensures the
    schema via the app's EF migrations, then loads every row into the `finoma` schema
    of the target LocalDB database (default ExpenseTrackerDb_Backup — kept separate
    from your dev DB, ExpenseTrackerDb). Reuses the app's tested restore engine.

    After this you can open the DB in SSMS, or point ConnectionStrings:DefaultConnection
    at it to run Finoma locally against your real data.

.PARAMETER Path
    Which .json.gz to load. Defaults to the newest file in .\backups.

.PARAMETER LocalDb
    Target database in (localdb)\mssqllocaldb. Default ExpenseTrackerDb_Backup.
    Pass -LocalDb ExpenseTrackerDb to overwrite your actual dev database instead.

.EXAMPLE
    .\Restore-FinomaLocal.ps1
    # loads the newest pulled snapshot into ExpenseTrackerDb_Backup
.EXAMPLE
    .\Restore-FinomaLocal.ps1 -Path .\backups\finoma-backup-20260702-101500.json.gz
#>
[CmdletBinding()]
param([string]$Path, [string]$LocalDb = 'ExpenseTrackerDb_Backup')

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj      = (Resolve-Path (Join-Path $root '..\ExpenseTracker.csproj')).Path
$backupDir = Join-Path $root 'backups'

# Resolve which snapshot to load.
if (-not $Path) {
    $latest = Get-ChildItem $backupDir -Filter 'finoma-backup-*.json.gz' -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { throw "No snapshots in $backupDir — run Pull-FinomaBackup.ps1 first." }
    $Path = $latest.FullName
}
if (-not (Test-Path $Path)) { throw "File not found: $Path" }

$conn = "Server=(localdb)\mssqllocaldb;Database=$LocalDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
Write-Host "Loading $([System.IO.Path]::GetFileName($Path)) -> [$LocalDb] on (localdb)\mssqllocaldb ..." -ForegroundColor Cyan

# Run the app's restorelocal CLI against the target LocalDB (schema is created via
# EF migrations, then the snapshot is loaded). Env var overrides the connection string.
$env:ConnectionStrings__DefaultConnection = $conn
$env:Email__Enabled = 'false'
& dotnet run --project $proj --no-launch-profile -- restorelocal $Path
$code = $LASTEXITCODE
Remove-Item Env:\ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
Remove-Item Env:\Email__Enabled -ErrorAction SilentlyContinue
if ($code -ne 0) { throw "Restore failed (exit $code)." }

Write-Host "Done. Database [$LocalDb] on (localdb)\mssqllocaldb now holds the snapshot (finoma schema)." -ForegroundColor Green
Write-Host "  Browse it in SSMS, or set ConnectionStrings:DefaultConnection -> Database=$LocalDb to run Finoma against it." -ForegroundColor DarkGray
