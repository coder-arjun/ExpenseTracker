<#
.SYNOPSIS
    Delete the prod startup-error diagnostic (/_diag.txt). It can leak a stack
    trace, so clear it once the app is healthy. Safe to run anytime.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $root 'secrets.ps1')
$cred = New-Object System.Net.NetworkCredential($FtpUser, $FtpPass)

# The app writes _diag.txt into its own wwwroot → /wwwroot/wwwroot on the FTP tree.
# Try the deeper (correct) path first, then the shallow one just in case.
foreach ($p in @('/wwwroot/wwwroot/_diag.txt', '/wwwroot/_diag.txt')) {
    try {
        $r = [System.Net.FtpWebRequest]::Create("ftp://$FtpHost$p")
        $r.Credentials = $cred
        $r.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
        $r.KeepAlive = $false
        $resp = $r.GetResponse(); $resp.Close()
        Write-Host "deleted $p" -ForegroundColor Green
    } catch {
        Write-Host "skip $p ($($_.Exception.Message))" -ForegroundColor DarkYellow
    }
}
