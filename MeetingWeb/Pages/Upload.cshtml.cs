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
        private readonly IAudioStorage _audioStorage;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IActiveWorkspaceService _activeWorkspace;
        private readonly IConfiguration _configuration;

        // Shown on the page and checked in the browser before uploading.
        public int MaxMegabytes => _configuration.GetValue("Upload:MaxMegabytes", 300);

        public UploadModel(ApplicationDbContext context, UserManager<IdentityUser> userManager, IAudioStorage audioStorage,
            IBackgroundJobClient backgroundJobClient, IActiveWorkspaceService activeWorkspace, IConfiguration configuration)
        {
            _configuration = configuration;
            _activeWorkspace = activeWorkspace;
            _context = context;
            _userManager = userManager;
            _audioStorage = audioStorage;
            _backgroundJobClient = backgroundJobClient;
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

            if (UploadFile.Length > MaxMegabytes * 1024L * 1024L)
            {
                ErrorMessage = $"Dosya çok büyük. En fazla {MaxMegabytes} MB'lık bir kayıt yükleyebilirsiniz; uzun toplantıları parçalara bölerek yükleyebilirsiniz.";
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

            // 2. TENANT RESOLUTION (S7): the remembered workspace is re-checked against current membership.
            int? activeWorkspaceId = (await _activeWorkspace.GetActiveMembershipAsync(userId))?.WorkspaceId;

            if (activeWorkspaceId == null)
            {
                ErrorMessage = "Herhangi bir çalışma alanına dahil değilsiniz. Lütfen önce bir alan seçin veya oluşturun.";
                return Page();
            }

            // 3. PRIVATE STORAGE: the recording goes to a folder outside wwwroot and is deleted after analysis (S2).
            var storedName = await _audioStorage.SaveAsync(UploadFile);

            // 4. ENTITY CREATION: Provision the draft meeting in the database (Strictly UTC for consistency).
            var draftMeeting = new MeetingSummary
            {
                ToplantiKonusu = UploadFile.FileName,
                CreatedDate = DateTime.UtcNow,
                WorkspaceId = activeWorkspaceId,
                Status = MeetingStatus.Processing,
                AudioFilePath = storedName
            };

            _context.MeetingSummaries.Add(draftMeeting);
            await _context.SaveChangesAsync();

            // 5. BACKGROUND DELEGATION: Offload the heavy AI processing to Hangfire.
            _backgroundJobClient.Enqueue<IMeetingProcessor>(processor => processor.ProcessMeetingJob(draftMeeting.Id, _audioStorage.GetPath(storedName)));

            // 6. UX ROUTING: Immediately redirect to the Details page for Real-Time SignalR tracking.
            return RedirectToPage("/Details", new { id = draftMeeting.Id });
        }
    }
}