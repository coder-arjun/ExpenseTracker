# Finoma — database backup & local restore

On-demand backup of the **live** Finoma database (MonsterASP MSSQL) to a portable
`.bacpac` file, plus a one-command restore into local SQL Server LocalDB. Run it
**whenever your machine is online** — nothing is scheduled, nothing runs without you.

A `.bacpac` is a single compressed file holding the **full schema + all data**. It's
a real, restorable snapshot: import it into a fresh hosted database to recover prod,
or into LocalDB to run Finoma locally against your real data.

> The **export** only needs internet + your prod credentials, so you can keep taking
> backups even when LocalDB/your dev setup is down. Loading a backup into LocalDB is
> a separate step you run later.

---

## One-time setup

1. **Add your credentials** (kept out of git):
   - Copy `secrets.example.ps1` → `secrets.ps1`.
   - Paste your MonsterASP **external** connection string (panel → your MSSQL DB →
     *Connection strings* → the one whose server is a public address).
   - If you hit a TLS error, append `TrustServerCertificate=True;` (the template
     already includes it).

2. **SqlPackage** — the scripts auto-install it on first run via
   `dotnet tool install -g microsoft.sqlpackage`. If PATH doesn't pick it up, open a
   **new** terminal and re-run (or add `%USERPROFILE%\.dotnet\tools` to PATH).

That's it — `sqlcmd` and `SqlLocalDB` you already have.

---

## Weekly backup (run whenever you're up)

```powershell
# from the db-backup folder:
./Backup-Finoma.ps1
```

- Produces `./backups/finoma-YYYYMMDD-HHmmss.bacpac`.
- Keeps the newest **8** backups (`-Keep 12` to keep more).
- Want a local copy in the same run? add `-RestoreLocal`:

```powershell
./Backup-Finoma.ps1 -RestoreLocal      # export AND load into LocalDB
```

Keep a calendar nudge once a week; the script is idempotent, so running it twice in
a day is harmless.

---

## Restore a backup into LocalDB (when your machine is up)

```powershell
./Restore-FinomaLocal.ps1                       # newest backup → ExpenseTrackerDb_Backup
./Restore-FinomaLocal.ps1 -Path .\backups\finoma-20260626-120000.bacpac
```

By default it restores into a **separate** DB `ExpenseTrackerDb_Backup` so your dev
database is untouched. To run the app against the restored copy, point
`ConnectionStrings:DefaultConnection` at `Database=ExpenseTrackerDb_Backup`.

---

## Emergency recovery (prod is gone)

1. Provision a fresh MSSQL database on MonsterASP (or any SQL Server).
2. Import your latest `.bacpac` into it:

   ```powershell
   sqlpackage /Action:Import `
     "/SourceFile:.\backups\finoma-YYYYMMDD-HHmmss.bacpac" `
     "/TargetConnectionString:<new database connection string>"
   ```

3. Update the host's `ConnectionStrings__DefaultConnection` to the new DB and restart.
   (Finoma's startup auto-migration will no-op since the schema is already present.)

---

## Notes

- `secrets.ps1`, the `backups/` folder, and every `*.bacpac` are **gitignored** —
  real data and your password never enter git.
- Backups are only as fresh as the last run. For a personal app, a weekly cadence is
  usually plenty; bump frequency around big changes.
- Optional automation: if you ever want it hands-off, Windows **Task Scheduler** can
  run `Backup-Finoma.ps1` on a weekly trigger (`-RestoreLocal` left off so it works
  headless). Ask and I'll set that up.
