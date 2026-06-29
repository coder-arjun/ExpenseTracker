using ExpenseTracker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Controllers
{
    /// <summary>
    /// Key-gated server-side backup endpoints. Schedule <c>/Backup/Run</c> on
    /// cron-job.org (e.g. weekly) the same way as the monthly statement cron;
    /// it emails a gzipped JSON snapshot off-server. <c>/Backup/Download</c>
    /// pulls the latest snapshot on demand. Both reuse the Statements cron key.
    /// </summary>
    [AllowAnonymous]
    public class BackupController : Controller
    {
        private readonly BackupService _backup;
        private readonly IConfiguration _config;

        public BackupController(BackupService backup, IConfiguration config)
        {
            _backup = backup;
            _config = config;
        }

        private bool KeyOk(string? key)
        {
            var expected = _config["Statements:CronKey"];
            return !string.IsNullOrEmpty(expected) && key == expected;
        }

        // GET /Backup/Run?key=<cronkey>  — create + email + retain a snapshot.
        [HttpGet("/Backup/Run")]
        public async Task<IActionResult> Run(string? key)
        {
            if (!KeyOk(key)) return Unauthorized();
            try
            {
                var msg = await _backup.RunAsync(HttpContext.RequestAborted);
                return Content(msg, "text/plain");
            }
            catch (Exception ex)
            {
                return Content("Backup failed: " + ex.Message, "text/plain");
            }
        }

        // GET /Backup/Download?key=<cronkey>  — download the newest snapshot.
        [HttpGet("/Backup/Download")]
        public IActionResult Download(string? key)
        {
            if (!KeyOk(key)) return Unauthorized();
            var f = _backup.Latest();
            if (f == null) return NotFound("No backups yet — hit /Backup/Run first.");
            return PhysicalFile(f.FullName, "application/gzip", f.Name);
        }

        // POST /Backup/Restore?key=<cronkey>&confirm=replace-finoma
        // Body = a dump file (gzip or plain JSON). WIPES + reloads the finoma schema
        // (never touches dbo / DailyPilot). The confirm guard prevents accidents.
        [HttpPost("/Backup/Restore")]
        public async Task<IActionResult> Restore(string? key, string? confirm)
        {
            if (!KeyOk(key)) return Unauthorized();
            if (confirm != "replace-finoma")
                return BadRequest("Add &confirm=replace-finoma — this REPLACES all data in the finoma schema.");

            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, HttpContext.RequestAborted);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                return BadRequest("Empty body — POST the dump file as the raw request body.");

            try
            {
                var msg = await _backup.RestoreAsync(bytes, HttpContext.RequestAborted);
                return Content(msg, "text/plain");
            }
            catch (Exception ex)
            {
                return Content("Restore FAILED (rolled back, nothing changed): " + ex.Message, "text/plain");
            }
        }
    }
}
