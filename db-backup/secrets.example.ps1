# Copy this file to "secrets.ps1" (gitignored) and fill in the values.
# Used by Pull-FinomaBackup.ps1 to pull the server-side backup down to this machine.

$BaseUrl   = 'https://finoma.runasp.net'
$BackupKey = 'YOUR_BACKUP_KEY'   # the Statements:CronKey from the server's appsettings.json
