<#
.SYNOPSIS
    Pull Finoma's server-side backup down to THIS machine.

.DESCRIPTION
    Triggers a fresh server backup (GET /Backup/Run) and then downloads the latest
    snapshot (GET /Backup/Download) into .\backups, keeping the newest N. It's plain
    HTTPS to the app, so it works from anywhere and needs NO direct database access
    (the free-tier MSSQL blocks external connections — which is why the old SqlPackage
    approach didn't work). Run it whenever your machine is online, or schedule it via
    Windows Task Scheduler.

.PARAMETER NoRun
    Skip triggering a fresh server backup; just download whatever's latest on the server.

.PARAMETER Keep
    How many local snapshots to keep in .\backups (default 30).

.EXAMPLE
    .\Pull-FinomaBackup.ps1            # fresh server backup, then download to .\backups
.EXAMPLE
    .\Pull-FinomaBackup.ps1 -NoRun     # just download the latest existing snapshot
#>
[CmdletBinding()]
param([switch]$NoRun, [int]$Keep = 30)

$ErrorActionPreference = 'Stop'
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$secrets = Join-Path $root 'secrets.ps1'
if (-not (Test-Path $secrets)) {
    throw "Missing secrets.ps1 — copy secrets.example.ps1 to secrets.ps1 and set `$BaseUrl + `$BackupKey."
}
. $secrets   # sets $BaseUrl, $BackupKey
if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($BackupKey)) {
    throw "Set `$BaseUrl and `$BackupKey in secrets.ps1."
}
$BaseUrl = $BaseUrl.TrimEnd('/')

$backupDir = Join-Path $root 'backups'
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

if (-not $NoRun) {
    Write-Host "Triggering a fresh server backup..." -ForegroundColor Cyan
    $msg = Invoke-RestMethod -Uri "$BaseUrl/Backup/Run?key=$BackupKey" -TimeoutSec 180
    Write-Host "  server: $msg" -ForegroundColor DarkGray
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out   = Join-Path $backupDir "finoma-backup-$stamp.json.gz"
Write-Host "Downloading latest snapshot to local..." -ForegroundColor Cyan
Invoke-WebRequest -Uri "$BaseUrl/Backup/Download?key=$BackupKey" -OutFile $out -TimeoutSec 180

$kb = [math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Host "Saved $out  ($kb KB)" -ForegroundColor Green

# Prune to the newest N local copies.
Get-ChildItem $backupDir -Filter 'finoma-backup-*.json.gz' |
    Sort-Object LastWriteTime -Descending | Select-Object -Skip $Keep |
    ForEach-Object { Remove-Item $_.FullName -Force }

$count = (Get-ChildItem $backupDir -Filter 'finoma-backup-*.json.gz').Count
Write-Host "Done. $count backup(s) in $backupDir" -ForegroundColor Green
