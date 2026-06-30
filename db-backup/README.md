# Finoma — pull server backups to this machine

The free-tier production database **blocks external connections**, so you can't back
it up directly from your PC (SqlPackage/sqlcmd can't reach it). Instead, Finoma backs
itself up **server-side** (`/Backup/Run` dumps every table to gzipped JSON, emails it,
and keeps the latest 8), and this script **pulls that snapshot down to your machine**
over plain HTTPS.

## One-time setup
Copy `secrets.example.ps1` → `secrets.ps1` (gitignored) and set:
- `$BaseUrl` — `https://finoma.runasp.net`
- `$BackupKey` — the `Statements:CronKey` from the server's `appsettings.json`

(These are already filled in if I set them up for you.)

## Pull a backup to local
```powershell
# from the db-backup folder:
./Pull-FinomaBackup.ps1            # triggers a fresh server backup, then downloads it
./Pull-FinomaBackup.ps1 -NoRun     # just download the latest snapshot already on the server
./Pull-FinomaBackup.ps1 -Keep 60   # keep more local copies (default 30)
```
Files land in `./backups/finoma-backup-<timestamp>.json.gz`. Each is a complete,
gzipped JSON snapshot of the `finoma` schema (every table + row) — restorable.

## Automate it (optional)
Run it on a schedule with **Windows Task Scheduler** (Action: `powershell.exe`,
Arguments: `-ExecutionPolicy Bypass -File "D:\MyProject\ExpenseTracker\ExpenseTracker\db-backup\Pull-FinomaBackup.ps1"`).
The server already runs `/Backup/Run` weekly via cron-job.org and emails the snapshot,
so this is just for keeping local copies too.

## Inspecting a snapshot
```powershell
python -c "import gzip,json; d=json.load(gzip.open(r'.\backups\<file>.json.gz')); print(d['tableCount'],'tables',d['rowCount'],'rows')"
```

## Restoring
The server endpoint `POST /Backup/Restore?key=<key>&confirm=replace-finoma` loads a
snapshot back into the `finoma` schema (used during the 2026-06 recovery). Ask if you
want a one-command local-restore script too.
