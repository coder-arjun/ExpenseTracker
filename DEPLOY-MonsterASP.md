# Deploying Finoma to MonsterASP.NET (free)

## What's already wired into the app for you
- **DB-backed data-protection keys** — auth + "Remember me" cookies survive app
  restarts and redeploys (keys live in the `DataProtectionKeys` table, not the
  local disk).
- **Auto-migration on startup** — a freshly provisioned, empty MSSQL database
  gets its entire schema (Identity tables, app tables, `DataProtectionKeys`)
  automatically on the first launch. No manual `dotnet ef database update`.
- The connection string is read from config key `ConnectionStrings:DefaultConnection`.

## 1. Create the free resources (MonsterASP control panel)
1. Sign up at monsterasp.net (no credit card).
2. Create a free **Hosting** site → note the URL (e.g. `https://finoma.runasp.net`)
   and download the **`.PublishSettings`** (Web Deploy) file.
3. Create a free **MSSQL** database → copy its connection string
   (server, database name, user, password).
4. Set the site's runtime to **.NET 10**. (If .NET 10 isn't offered yet, see the
   self-contained note at the bottom.)

## 2. Point the app at the MSSQL database (NOT LocalDB)
Your local `appsettings.json` still has the LocalDB string, and it gets published
with the app — so override it on the server. Pick ONE:

- **Recommended (keeps the secret out of files):** in the site's `web.config`,
  inside the `<aspNetCore>` element, add:
  ```xml
  <environmentVariables>
    <environmentVariable name="ConnectionStrings__DefaultConnection"
      value="Server=YOUR_SERVER;Database=YOUR_DB;User Id=YOUR_USER;Password=YOUR_PASS;TrustServerCertificate=True;MultipleActiveResultSets=True" />
  </environmentVariables>
  ```
  Environment variables override `appsettings.json`.
- **Simplest:** use MonsterASP's File Manager to edit the deployed
  `appsettings.json` and replace `ConnectionStrings:DefaultConnection` with the
  MSSQL string from the panel.

Use the exact string from the MonsterASP DB page; keep `TrustServerCertificate=True`.

## 3. Publish & upload
- **Visual Studio:** right-click the project → **Publish** → import the
  `.PublishSettings` → **Publish** (one-click Web Deploy).
- **CLI:** `dotnet publish -c Release`, then upload the contents of
  `bin/Release/net10.0/publish/` to the site root via FTP.

## 4. First launch
Browse to your URL. On the first request the app applies all migrations and
creates the tables — including `DataProtectionKeys`. Register an account; you're live.

## Notes
- **Remember me** now persists across restarts because the keys are in the DB.
  Do **not** change `SetApplicationName("ExpenseTracker")` in `Program.cs` — that
  would invalidate every existing cookie.
- **Uploaded receipts** are written to the server disk under the app folder.
  MonsterASP's disk is persistent but counts toward your storage quota.
- **HTTPS** is free (Let's Encrypt); the existing cookie/redirect config works on IIS.
- **No .NET 10 in the panel?** Publish self-contained so the runtime ships with
  the app: `dotnet publish -c Release -r win-x64 --self-contained true`, then upload.
- Keep the real connection string OUT of git — `appsettings.json` is already
  gitignored.
