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

## Load a snapshot into a LOCAL database (server → local migration)
`Pull-FinomaBackup.ps1` only downloads the snapshot *file*. To turn it into a real,
queryable local database, run:
```powershell
./Restore-FinomaLocal.ps1                       # newest snapshot -> ExpenseTrackerDb_Backup
./Restore-FinomaLocal.ps1 -Path .\backups\finoma-backup-<ts>.json.gz
./Restore-FinomaLocal.ps1 -LocalDb ExpenseTrackerDb   # overwrite your dev DB instead
```
It builds the schema (via EF migrations) and loads every row into the `finoma` schema
of the target LocalDB database (default **`ExpenseTrackerDb_Backup`**, separate from your
dev DB). Then open it in SSMS, or set `ConnectionStrings:DefaultConnection` →
`Database=ExpenseTrackerDb_Backup` to run Finoma locally against your real data.
It reuses the app's tested restore engine and is **guarded to LocalDB only** — it will
refuse any non-LocalDB connection, so it can't touch production.

## Restoring on the server
The endpoint `POST /Backup/Restore?key=<key>&confirm=replace-finoma` loads a snapshot
back into the live `finoma` schema (used during the 2026-06 recovery).
