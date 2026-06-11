using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;
using Hangfire;
using Microsoft.AspNetCore.Hosting;

namespace MeetingWeb.Pages
{
    // SECURITY GATEWAY: Only authenticated users can upload files to the processing queue.
    [Authorize]
    public class UploadModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment _environment;

        public UploadModel(ApplicationDbContext context, UserManager<IdentityUser> userManager, IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
        }

        [BindProperty]
        public IFormFile? UploadFile { get; set; }

        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            ErrorMessage = null;

            // 1. FILE VALIDATION: Prevent empty payloads.
            if (UploadFile == null || UploadFile.Length == 0)
            {
                ErrorMessage = "Lütfen bir dosya seçin.";
                return Page();
            }

            var allowedExtensions = new[] { ".mp3", ".wav", ".m4a" };
            var extension = Path.GetExtension(UploadFile.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                ErrorMessage = "Geçersiz dosya formatı! Lütfen sadece .mp3, .wav veya .m4a yükleyin.";
                return Page();
            }

            var userId = _userManager.GetUserId(User);

            // 2. TENANT RESOLUTION: Retrieve active workspace from session or fallback to default.
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");
            if (activeWorkspaceId == null)
            {
                var firstWorkspace = _context.WorkspaceUsers.FirstOrDefault(wu => wu.UserId == userId);
                if (firstWorkspace != null)
                {
                    activeWorkspaceId = firstWorkspace.WorkspaceId;
                }
            }

            if (activeWorkspaceId == null)
            {
                ErrorMessage = "Herhangi bir çalışma alanına dahil değilsiniz. Lütfen önce bir alan seçin veya oluşturun.";
                return Page();
            }

            // 3. STORAGE PROVISIONING: Ensure physical directory exists in the web root.
            var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsDir))
            {
                Directory.CreateDirectory(uploadsDir);
            }

            // 4. PATH TRAVERSAL PREVENTION: Sanitize filename with high-entropy GUIDs.
            var safeFileName = Path.GetFileName(UploadFile.FileName);
            var fileName = Guid.NewGuid() + "_" + safeFileName;
            var filePath = Path.Combine(uploadsDir, fileName);

            // 5. ASYNCHRONOUS I/O: Stream the file securely to disk.
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await UploadFile.CopyToAsync(stream);
            }

            // 6. ENTITY CREATION: Provision the draft meeting in the database (Strictly UTC for consistency).
            var draftMeeting = new MeetingSummary
            {
                ToplantiKonusu = UploadFile.FileName,
                CreatedDate = DateTime.UtcNow,
                WorkspaceId = activeWorkspaceId,
                Status = MeetingStatus.Processing,
                AudioFilePath = "/uploads/" + fileName
            };

            _context.MeetingSummaries.Add(draftMeeting);
            await _context.SaveChangesAsync();

            // 7. BACKGROUND DELEGATION: Offload the heavy AI processing to Hangfire.
            BackgroundJob.Enqueue<IMeetingProcessor>(processor => processor.ProcessMeetingJob(draftMeeting.Id, filePath));

            // 8. UX ROUTING: Immediately redirect to the Details page for Real-Time SignalR tracking.
            return RedirectToPage("/Details", new { id = draftMeeting.Id });
        }
    }
}