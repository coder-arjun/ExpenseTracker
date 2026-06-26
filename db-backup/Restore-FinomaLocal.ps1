<#
.SYNOPSIS
    Restore a Finoma .bacpac backup into SQL Server LocalDB.

.DESCRIPTION
    Use this when your machine was offline at backup time and you now want to load
    the latest (or a specific) .bacpac into LocalDB — e.g. to inspect your real
    data or run Finoma locally against a production snapshot.

    It drops the target database first (a .bacpac import always creates the DB
    fresh), then imports. Your live hosted DB is never touched.

.PARAMETER Path
    Which .bacpac to restore. Defaults to the newest file in .\backups.

.PARAMETER LocalDb
    Target database name in LocalDB. Default 'ExpenseTrackerDb_Backup'.
    Pass -LocalDb ExpenseTrackerDb to overwrite your actual dev database.

.EXAMPLE
    .\Restore-FinomaLocal.ps1
    # Restores the newest backup into ExpenseTrackerDb_Backup

.EXAMPLE
    .\Restore-FinomaLocal.ps1 -Path .\backups\finoma-20260626-1200.bacpac -LocalDb ExpenseTrackerDb
#>
[CmdletBinding()]
param(
    [string]$Path,
    [string]$LocalDb = 'ExpenseTrackerDb_Backup'
)

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$backupDir = Join-Path $root 'backups'

function Write-Step($msg) { Write-Host "→ $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "✔ $msg" -ForegroundColor Green }

# Resolve which .bacpac to restore.
if (-not $Path) {
    $latest = Get-ChildItem $backupDir -Filter 'finoma-*.bacpac' -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { throw "No .bacpac files found in $backupDir. Run Backup-Finoma.ps1 first." }
    $Path = $latest.FullName
}
if (-not (Test-Path $Path)) { throw "Backup file not found: $Path" }

# Resolve SqlPackage (installed by Backup-Finoma.ps1, or install now).
function Resolve-SqlPackage {
    $cmd = (Get-Command sqlpackage -ErrorAction SilentlyContinue)?.Source
    if ($cmd) { return $cmd }
    $candidate = Join-Path $env:USERPROFILE '.dotnet\tools\sqlpackage.exe'
    if (Test-Path $candidate) { return $candidate }
    return $null
}
$sqlPackage = Resolve-SqlPackage
if (-not $sqlPackage) {
    Write-Step "Installing SqlPackage (one-time)…"
    dotnet tool install -g microsoft.sqlpackage
    $sqlPackage = Resolve-SqlPackage
    if (-not $sqlPackage) { throw "SqlPackage not available. Install: dotnet tool install -g microsoft.sqlpackage" }
}

$instance = '(localdb)\mssqllocaldb'
Write-Step "Restoring $([System.IO.Path]::GetFileName($Path)) → [$LocalDb] on $instance"

SqlLocalDB start mssqllocaldb | Out-Null

$drop = @"
IF DB_ID(N'$LocalDb') IS NOT NULL
BEGIN
    ALTER DATABASE [$LocalDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$LocalDb];
END
"@
sqlcmd -S $instance -b -Q $drop
if ($LASTEXITCODE -ne 0) { throw "Could not drop existing [$LocalDb]." }

$tcs = "Data Source=$instance;Initial Catalog=$LocalDb;Integrated Security=True;TrustServerCertificate=True;"
& $sqlPackage /Action:Import "/SourceFile:$Path" "/TargetConnectionString:$tcs"
if ($LASTEXITCODE -ne 0) { throw "SqlPackage import failed (exit $LASTEXITCODE)." }

Write-Ok "Restored. Database [$LocalDb] is ready on $instance."
Write-Host "  To run Finoma against it, set ConnectionStrings:DefaultConnection → Database=$LocalDb." -ForegroundColor DarkGray
