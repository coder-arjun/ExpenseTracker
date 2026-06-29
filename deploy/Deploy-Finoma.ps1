<#
.SYNOPSIS
    Deploy published files to MonsterASP via FTP (app_offline wrap + retries).
.DESCRIPTION
    Uploads the given files from ../publish to /wwwroot. Uses app_offline.htm to
    release the DLL lock, KeepAlive=$false + a retry loop (MonsterASP FTP quirks).
    Credentials come from the gitignored deploy/secrets.ps1.
.PARAMETER Files
    File names (relative to ../publish) to upload. Default: ExpenseTracker.dll.
.EXAMPLE
    .\Deploy-Finoma.ps1
.EXAMPLE
    .\Deploy-Finoma.ps1 -Files ExpenseTracker.dll, appsettings.json
#>
[CmdletBinding()]
param([string[]]$Files = @('ExpenseTracker.dll'))

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $root 'secrets.ps1')                       # $FtpHost, $FtpUser, $FtpPass
$publish = (Resolve-Path (Join-Path $root '..\publish')).Path
$cred    = New-Object System.Net.NetworkCredential($FtpUser, $FtpPass)

function Send-File($local, $remote) {
    for ($i = 1; $i -le 4; $i++) {
        try {
            $r = [System.Net.FtpWebRequest]::Create("ftp://$FtpHost$remote")
            $r.Credentials = $cred
            $r.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
            $r.UseBinary = $true; $r.KeepAlive = $false; $r.UsePassive = $true
            $b = [System.IO.File]::ReadAllBytes($local); $r.ContentLength = $b.Length
            $s = $r.GetRequestStream(); $s.Write($b, 0, $b.Length); $s.Close()
            $resp = $r.GetResponse(); $resp.Close()
            Write-Host "  uploaded $remote ($($b.Length) bytes)" -ForegroundColor Green; return
        } catch { Write-Host "  try $i failed: $($_.Exception.Message)" -ForegroundColor Yellow; Start-Sleep -Milliseconds 1200 }
    }
    throw "Upload failed: $remote"
}
function Remove-File($remote) {
    for ($i = 1; $i -le 4; $i++) {
        try {
            $r = [System.Net.FtpWebRequest]::Create("ftp://$FtpHost$remote")
            $r.Credentials = $cred
            $r.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
            $r.KeepAlive = $false
            $resp = $r.GetResponse(); $resp.Close()
            Write-Host "  deleted $remote" -ForegroundColor Green; return
        } catch { Start-Sleep -Milliseconds 1200 }
    }
    Write-Host "  (could not delete $remote)" -ForegroundColor DarkYellow
}

$off = Join-Path $env:TEMP 'app_offline.htm'
'<!doctype html><html><body style="font-family:sans-serif;text-align:center;padding:60px">Finoma is updating&hellip;</body></html>' | Set-Content $off -Encoding UTF8

Write-Host "1) app offline (release DLL lock)" -ForegroundColor Cyan
Send-File $off '/wwwroot/app_offline.htm'
Start-Sleep -Seconds 2

Write-Host "2) uploading $($Files.Count) file(s)" -ForegroundColor Cyan
foreach ($f in $Files) {
    $local = Join-Path $publish $f
    if (-not (Test-Path $local)) { throw "Not found in publish: $f" }
    Send-File $local "/wwwroot/$f"
}

Write-Host "3) app online" -ForegroundColor Cyan
Remove-File '/wwwroot/app_offline.htm'

# Clear any stale startup-error diagnostic (it leaks a stack trace). The app writes
# it to <approot>/wwwroot/_diag.txt, which on the FTP tree is /wwwroot/wwwroot/_diag.txt
# (the app's own wwwroot sits one level below the FTP/app root).
Write-Host "4) clearing stale _diag.txt" -ForegroundColor Cyan
Remove-File '/wwwroot/wwwroot/_diag.txt'

Write-Host "Deploy complete." -ForegroundColor Green
