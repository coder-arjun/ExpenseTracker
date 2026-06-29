using System.IO.Compression;
using System.Text.Json;
using ExpenseTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Server-side backup for Finoma. The production DB is only reachable from the
    /// host (no external access) and the free tier blocks BACKUP DATABASE, so this
    /// dumps every table in the `finoma` schema to gzipped JSON via raw ADO.NET
    /// (model-independent), keeps the last N copies on disk, and — crucially —
    /// emails the snapshot off-server (the host filesystem can be wiped on redeploy).
    /// Restorable by re-inserting the rows per table.
    /// </summary>
    public class BackupService
    {
        private readonly ApplicationDbContext _db;
        private readonly EmailSender _email;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BackupService> _log;

        public BackupService(ApplicationDbContext db, EmailSender email, IConfiguration config,
            IWebHostEnvironment env, ILogger<BackupService> log)
        {
            _db = db; _email = email; _config = config; _env = env; _log = log;
        }

        public string BackupDir => Path.Combine(_env.ContentRootPath, "App_Data", "backups");
        private int Keep => _config.GetValue<int?>("Backup:Keep") ?? 8;

        private string? Recipient =>
            new[] { _config["Backup:Email"], _config["Email:FromAddress"], _config["Email:User"] }
            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

        // ── Build a gzipped JSON snapshot of every finoma.* table ────────────
        public async Task<(byte[] Gzip, string FileName, int Tables, int Rows)> CreateAsync(CancellationToken ct = default)
        {
            var conn = _db.Database.GetDbConnection();
            await conn.OpenAsync(ct);
            try
            {
                var tables = new List<string>();
                await using (var c = conn.CreateCommand())
                {
                    c.CommandText =
                        "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES " +
                        "WHERE TABLE_SCHEMA = 'finoma' AND TABLE_TYPE = 'BASE TABLE' " +
                        "AND TABLE_NAME <> '__EFMigrationsHistory' ORDER BY TABLE_NAME";
                    await using var r = await c.ExecuteReaderAsync(ct);
                    while (await r.ReadAsync(ct)) tables.Add(r.GetString(0));
                }

                var dump = new Dictionary<string, List<Dictionary<string, object?>>>();
                var totalRows = 0;
                foreach (var t in tables)
                {
                    var rows = new List<Dictionary<string, object?>>();
                    await using var c = conn.CreateCommand();
                    // t comes from INFORMATION_SCHEMA on our own schema (not user input); bracket-quote it.
                    c.CommandText = $"SELECT * FROM [finoma].[{t}]";
                    await using var r = await c.ExecuteReaderAsync(ct);
                    while (await r.ReadAsync(ct))
                    {
                        var row = new Dictionary<string, object?>(r.FieldCount);
                        for (var i = 0; i < r.FieldCount; i++)
                        {
                            var v = r.GetValue(i);
                            row[r.GetName(i)] = v is DBNull ? null : v;
                        }
                        rows.Add(row);
                    }
                    dump[t] = rows;
                    totalRows += rows.Count;
                }

                var payload = new
                {
                    app = "Finoma",
                    schema = "finoma",
                    generatedUtc = DateTime.UtcNow,
                    tableCount = tables.Count,
                    rowCount = totalRows,
                    tables = dump
                };

                var json = JsonSerializer.SerializeToUtf8Bytes(payload);
                using var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    gz.Write(json, 0, json.Length);

                var fileName = $"finoma-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json.gz";
                return (ms.ToArray(), fileName, tables.Count, totalRows);
            }
            finally
            {
                await conn.CloseAsync();
            }
        }

        // ── Create + persist + prune + email ─────────────────────────────────
        public async Task<string> RunAsync(CancellationToken ct = default)
        {
            var (gz, fileName, tableCount, rowCount) = await CreateAsync(ct);

            Directory.CreateDirectory(BackupDir);
            var path = Path.Combine(BackupDir, fileName);
            await File.WriteAllBytesAsync(path, gz, ct);

            // Prune to the newest N.
            foreach (var old in new DirectoryInfo(BackupDir)
                         .GetFiles("finoma-backup-*.json.gz")
                         .OrderByDescending(f => f.LastWriteTimeUtc)
                         .Skip(Keep))
            {
                try { old.Delete(); } catch { /* best effort */ }
            }

            var kb = Math.Round(gz.Length / 1024.0, 1);
            var emailed = "email not configured — backup saved on server only (download it before the next redeploy)";
            var to = Recipient;
            if (_email.IsConfigured && !string.IsNullOrWhiteSpace(to))
            {
                try
                {
                    var html =
                        $"<p>Your Finoma backup for <strong>{DateTime.Now:dd MMM yyyy, HH:mm}</strong> is attached " +
                        $"(<code>{fileName}</code>, {kb} KB gzipped).</p>" +
                        $"<p>{tableCount} tables, {rowCount} rows from the <code>finoma</code> schema. " +
                        $"Keep this email — it's an off-server copy of your data.</p>";
                    await _email.SendAsync(to!, "Finoma", $"Finoma backup — {DateTime.Now:dd MMM yyyy}",
                        html, gz, fileName, "application/gzip", ct);
                    emailed = $"emailed to {to}";
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Backup email failed");
                    emailed = $"email FAILED ({ex.Message}) — backup still saved on server";
                }
            }

            var msg = $"Backup OK: {tableCount} tables, {rowCount} rows, {kb} KB. {emailed}. Saved {fileName}.";
            _log.LogInformation("{Msg}", msg);
            return msg;
        }

        // ── Restore a dump (gz or plain JSON) into the finoma schema ─────────
        // Wipes the finoma domain/Identity tables (NOT DataProtectionKeys or the
        // migrations history) and bulk-loads the dump, preserving primary keys via
        // IDENTITY_INSERT. Scoped strictly to [finoma].* — never touches dbo
        // (DailyPilot). Atomic: any error rolls the whole thing back.
        public async Task<string> RestoreAsync(byte[] dump, CancellationToken ct = default)
        {
            // Decompress if gzip-framed.
            byte[] jsonBytes = dump.Length > 2 && dump[0] == 0x1f && dump[1] == 0x8b
                ? Decompress(dump) : dump;

            using var doc = JsonDocument.Parse(jsonBytes);
            var tablesEl = doc.RootElement.GetProperty("tables");

            var conn = _db.Database.GetDbConnection();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                var preserve = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "__EFMigrationsHistory", "DataProtectionKeys" };

                var finomaTables = await ScalarListAsync(conn, tx,
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_SCHEMA='finoma' AND TABLE_TYPE='BASE TABLE'", ct);

                // 1) Disable FK constraints so load order doesn't matter.
                foreach (var t in finomaTables)
                    await ExecAsync(conn, tx, $"ALTER TABLE [finoma].[{t}] NOCHECK CONSTRAINT ALL", ct);

                // 2) Clear existing rows (except preserved infra tables).
                foreach (var t in finomaTables.Where(t => !preserve.Contains(t)))
                    await ExecAsync(conn, tx, $"DELETE FROM [finoma].[{t}]", ct);

                // 3) Load each dumped table that exists in finoma and isn't preserved.
                var perTable = new List<string>();
                var totalRows = 0;
                foreach (var prop in tablesEl.EnumerateObject())
                {
                    var t = prop.Name;
                    if (!finomaTables.Contains(t, StringComparer.OrdinalIgnoreCase) || preserve.Contains(t))
                        continue;
                    var arr = prop.Value;
                    if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) continue;

                    var colTypes = await GetColumnTypesAsync(conn, tx, t, ct);  // name -> CLR type
                    var hasIdentity = await HasIdentityAsync(conn, tx, t, ct);
                    if (hasIdentity) await ExecAsync(conn, tx, $"SET IDENTITY_INSERT [finoma].[{t}] ON", ct);

                    var inserted = 0;
                    foreach (var row in arr.EnumerateArray())
                    {
                        // Only columns present in BOTH the dump row and the target table.
                        var cols = new List<string>();
                        foreach (var f in row.EnumerateObject())
                            if (colTypes.ContainsKey(f.Name)) cols.Add(f.Name);
                        if (cols.Count == 0) continue;

                        await using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        var names = string.Join(",", cols.Select(c => $"[{c}]"));
                        var ps = string.Join(",", cols.Select((_, i) => "@p" + i));
                        cmd.CommandText = $"INSERT INTO [finoma].[{t}] ({names}) VALUES ({ps})";
                        for (var i = 0; i < cols.Count; i++)
                        {
                            var p = cmd.CreateParameter();
                            p.ParameterName = "@p" + i;
                            p.Value = ConvertValue(row.GetProperty(cols[i]), colTypes[cols[i]]);
                            cmd.Parameters.Add(p);
                        }
                        await cmd.ExecuteNonQueryAsync(ct);
                        inserted++;
                    }

                    if (hasIdentity) await ExecAsync(conn, tx, $"SET IDENTITY_INSERT [finoma].[{t}] OFF", ct);
                    perTable.Add($"{t}={inserted}");
                    totalRows += inserted;
                }

                // 4) Re-enable + validate FK constraints.
                foreach (var t in finomaTables)
                    await ExecAsync(conn, tx, $"ALTER TABLE [finoma].[{t}] WITH CHECK CHECK CONSTRAINT ALL", ct);

                await tx.CommitAsync(ct);
                var msg = $"Restore OK into finoma: {totalRows} rows across {perTable.Count} tables. {string.Join(", ", perTable)}.";
                _log.LogWarning("{Msg}", msg);
                return msg;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        private static byte[] Decompress(byte[] gz)
        {
            using var inMs = new MemoryStream(gz);
            using var gzs = new GZipStream(inMs, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            gzs.CopyTo(outMs);
            return outMs.ToArray();
        }

        private static async Task ExecAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, string sql, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx; cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<List<string>> ScalarListAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, string sql, CancellationToken ct)
        {
            var list = new List<string>();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx; cmd.CommandText = sql;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) list.Add(r.GetString(0));
            return list;
        }

        private static async Task<bool> HasIdentityAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, string table, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT COUNT(*) FROM sys.identity_columns ic " +
                "JOIN sys.tables t ON t.object_id = ic.object_id " +
                "JOIN sys.schemas s ON s.schema_id = t.schema_id " +
                "WHERE s.name='finoma' AND t.name=@t";
            var p = cmd.CreateParameter(); p.ParameterName = "@t"; p.Value = table; cmd.Parameters.Add(p);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
        }

        private static async Task<Dictionary<string, Type>> GetColumnTypesAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, string table, CancellationToken ct)
        {
            var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT TOP 0 * FROM [finoma].[{table}]";
            await using var r = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SchemaOnly, ct);
            for (var i = 0; i < r.FieldCount; i++) map[r.GetName(i)] = r.GetFieldType(i);
            return map;
        }

        private static object ConvertValue(JsonElement v, Type t)
        {
            if (v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return DBNull.Value;
            if (t == typeof(DateTime)) return v.GetDateTime();
            if (t == typeof(DateTimeOffset)) return v.GetDateTimeOffset();
            if (t == typeof(decimal)) return v.GetDecimal();
            if (t == typeof(double)) return v.GetDouble();
            if (t == typeof(float)) return v.GetSingle();
            if (t == typeof(bool)) return v.GetBoolean();
            if (t == typeof(long)) return v.GetInt64();
            if (t == typeof(int)) return v.GetInt32();
            if (t == typeof(short)) return v.GetInt16();
            if (t == typeof(byte)) return v.GetByte();
            if (t == typeof(Guid)) return v.GetGuid();
            if (t == typeof(byte[])) return v.GetBytesFromBase64();
            return v.GetString() ?? (object)DBNull.Value;
        }

        public FileInfo? Latest() =>
            Directory.Exists(BackupDir)
                ? new DirectoryInfo(BackupDir).GetFiles("finoma-backup-*.json.gz")
                    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
    }
}
