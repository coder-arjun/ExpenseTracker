using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    /// <summary>
    /// Streams attachment files only to the user who owns them.
    /// Files live outside wwwroot — never directly URL-addressable.
    /// </summary>
    [Authorize]
    public class AttachmentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AttachmentStorage _storage;

        public AttachmentsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
                                     AttachmentStorage storage)
        {
            _context = context;
            _userManager = userManager;
            _storage = storage;
        }

        // GET /Attachments/Download/5
        public async Task<IActionResult> Download(int id)
        {
            var userId = _userManager.GetUserId(User);
            var a = await _context.Attachments.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (a == null) return NotFound();

            var path = _storage.FullPath(a);
            if (!System.IO.File.Exists(path)) return NotFound("File missing on disk.");

            // PhysicalFile streams without buffering. Use the original filename for the download dialog.
            return PhysicalFile(path, a.ContentType, a.FileName);
        }

        // POST /Attachments/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);
            var a = await _context.Attachments.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (a == null) return NotFound();

            var expenseId = a.ExpenseId;
            _storage.Delete(a);
            _context.Attachments.Remove(a);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Attachment removed.";
            return RedirectToAction("Details", "Expenses", new { id = expenseId });
        }
    }
}
