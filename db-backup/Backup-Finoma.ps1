<#
.SYNOPSIS
    On-demand backup of the LIVE Finoma database (MonsterASP MSSQL) to a portable
    .bacpac file. Optionally also refreshes a local copy in SQL Server LocalDB.

.DESCRIPTION
    Run this whenever your machine is online — it is fully idempotent and self-
    contained. The EXPORT step only needs internet + your prod credentials, so you
    can take backups even when your LocalDB / dev environment is down. Loading the
    backup into LocalDB is a separate concern (use -RestoreLocal here, or run
    Restore-FinomaLocal.ps1 later when your machine is up).

    A .bacpac is a single, compressed, fully-restorable snapshot (schema + all
    data). To recover in an emergency you can import it into a fresh MonsterASP
    database, or into LocalDB to run Finoma locally against your real data.

.PARAMETER Keep
    How many of the newest .bacpac files to keep in .\backups (older ones are
    pruned). Default 8.

.PARAMETER RestoreLocal
    Also import the just-made backup into LocalDB in the same run.

.PARAMETER LocalDb
    Target database name in LocalDB when -RestoreLocal is used.
    Default 'ExpenseTrackerDb_Backup' (kept separate from your dev DB).

.EXAMPLE
    .\Backup-Finoma.ps1
    # Export only — produces .\backups\finoma-<timestamp>.bacpac

.EXAMPLE
    .\Backup-Finoma.ps1 -RestoreLocal
    # Export AND load it into LocalDB as ExpenseTrackerDb_Backup
#>
[CmdletBinding()]
param(
    [int]$Keep = 8,
    [switch]$RestoreLocal,
    [string]$LocalDb = 'ExpenseTrackerDb_Backup'
)

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$backupDir = Join-Path $root 'backups'
$secrets   = Join-Path $root 'secrets.ps1'

function Write-Step($msg) { Write-Host "→ $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "✔ $msg" -ForegroundColor Green }

# ── 1. Load prod connection string (gitignored secrets.ps1) ──────────────────
if (-not (Test-Path $secrets)) {
    throw "Missing secrets.ps1. Copy secrets.example.ps1 to secrets.ps1 and paste your MonsterASP external connection string."
}
. $secrets
if ([string]::IsNullOrWhiteSpace($ProdConnectionString)) {
    throw "`$ProdConnectionString is empty in secrets.ps1."
}

# ── 2. Ensure SqlPackage is available (auto-install as a .NET global tool) ───
function Resolve-SqlPackage {
    $cmd = (Get-Command sqlpackage -ErrorAction SilentlyContinue)?.Source
    if ($cmd) { return $cmd }
    $candidate = Join-Path $env:USERPROFILE '.dotnet\tools\sqlpackage.exe'
    if (Test-Path $candidate) { return $candidate }
    return $null
}

$sqlPackage = Resolve-SqlPackage
if (-not $sqlPackage) {
    Write-Step "SqlPackage not found — installing it as a .NET global tool (one-time)…"
    dotnet tool install -g microsoft.sqlpackage
    if ($LASTEXITCODE -ne 0) { throw "Failed to install SqlPackage. Run manually: dotnet tool install -g microsoft.sqlpackage" }
    $sqlPackage = Resolve-SqlPackage
    if (-not $sqlPackage) {
        throw "SqlPackage installed but not on PATH. Open a NEW terminal (so PATH refreshes) and re-run, or add '$env:USERPROFILE\.dotnet\tools' to PATH."
    }
}

# ── 3. Export live DB → timestamped .bacpac ──────────────────────────────────
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$bacpac = Join-Path $backupDir "finoma-$stamp.bacpac"

Write-Step "Exporting live database → $bacpac"
& $sqlPackage /Action:Export `
    "/SourceConnectionString:$ProdConnectionString" `
    "/TargetFile:$bacpac" `
    /p:VerifyExtraction=true
if ($LASTEXITCODE -ne 0) { throw "SqlPackage export failed (exit $LASTEXITCODE)." }

$sizeMb = [math]::Round((Get-Item $bacpac).Length / 1MB, 2)
Write-Ok "Backup saved: $bacpac  ($sizeMb MB)"

# ── 4. Prune old backups (keep newest N) ─────────────────────────────────────
$old = Get-ChildItem $backupDir -Filter 'finoma-*.bacpac' |
       Sort-Object LastWriteTime -Descending | Select-Object -Skip $Keep
foreach ($f in $old) {
    Remove-Item $f.FullName -Force
    Write-Host "  pruned old backup $($f.Name)" -ForegroundColor DarkGray
}

# ── 5. Optional: refresh local copy in LocalDB ───────────────────────────────
if ($RestoreLocal) {
    $instance = '(localdb)\mssqllocaldb'
    Write-Step "Refreshing local copy [$LocalDb] on $instance …"

    # Make sure the LocalDB instance is running.
    SqlLocalDB start mssqllocaldb | Out-Null

    # Import creates the DB fresh, so drop any existing target first.
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
    & $sqlPackage /Action:Import "/SourceFile:$bacpac" "/TargetConnectionString:$tcs"
    if ($LASTEXITCODE -ne 0) { throw "SqlPackage import failed (exit $LASTEXITCODE)." }

    Write-Ok "Local copy ready: database [$LocalDb] on $instance"
    Write-Host "  To run Finoma against it, set ConnectionStrings:DefaultConnection → Database=$LocalDb." -ForegroundColor DarkGray
}

Write-Host "`nDone." -ForegroundColor Green
