using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Hangfire;
using MeetingWeb.Data;
using MeetingWeb.Models;
using MeetingWeb.Services;

namespace MeetingWeb.Pages
{
    // SECURITY GATEWAY: Restrict access to authenticated users only.
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IWebHostEnvironment _environment;

        // Constructor injection for required services and physical path resolution.
        public DetailsModel(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            IBackgroundJobClient backgroundJobClient,
            IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _backgroundJobClient = backgroundJobClient;
            _environment = environment;
        }

        public MeetingSummary Meeting { get; set; } = default!;
        public List<WorkspaceUser> WorkspaceMembers { get; set; } = new();
        public List<MeetingComment> Comments { get; set; } = new();

        // Data Transfer Object (DTO) to hold cached user metadata for UI rendering.
        public class UserProfileData
        {
            public string FullName { get; set; } = "";
            public string Initials { get; set; } = "";
            public string AvatarUrl { get; set; } = "";
        }

        // O(1) Lookup dictionary to prevent N+1 Database Query problems in the View.
        public Dictionary<string, UserProfileData> UserProfiles { get; set; } = new();

        // HTTP GET: Resolves the meeting data, state, and associated user contexts.
        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null) return NotFound();

            // Eager load the core meeting entity and its actionable tasks.
            var meeting = await _context.MeetingSummaries
                .Include(m => m.AksiyonMaddeleri)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (meeting == null) return NotFound();
            Meeting = meeting;

            // Fetch tenant members for the sharing/collaboration module.
            int? activeWorkspaceId = HttpContext.Session.GetInt32("ActiveWorkspaceId");
            if (activeWorkspaceId != null)
            {
                WorkspaceMembers = await _context.WorkspaceUsers
                    .Include(wu => wu.User)
                    .Where(wu => wu.WorkspaceId == activeWorkspaceId)
                    .ToListAsync();
            }

            // Eager load root comments, nested replies, and associated user reactions.
            Comments = await _context.MeetingComments
                .Include(c => c.User)
                .Include(c => c.Reactions)
                .Include(c => c.Replies).ThenInclude(r => r.User)
                .Include(c => c.Replies).ThenInclude(r => r.Reactions)
                .Where(c => c.MeetingId == id && c.ParentCommentId == null)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            // --- BULK METADATA RESOLUTION PIPELINE ---
            // Aggregate all unique user IDs across members, comments, and manual tasks.
            var allRelevantUserIds = WorkspaceMembers.Select(w => w.UserId).ToList();
            allRelevantUserIds.AddRange(Comments.Select(c => c.UserId));
            allRelevantUserIds.AddRange(Comments.SelectMany(c => c.Replies).Select(r => r.UserId));

            // Extract embedded user IDs from manually added string properties using the '|~|' delimiter.
            var manualIds = Meeting.GorusulenKonular
                .Concat(Meeting.AlinanKararlar)
                .Concat(Meeting.AksiyonMaddeleri.Select(a => a.Text))
                .Where(t => t.Contains("|~|"))
                .Select(t => t.Split("|~|")[1]);

            allRelevantUserIds.AddRange(manualIds);
            allRelevantUserIds = allRelevantUserIds.Distinct().ToList();

            // Execute a single batched query to retrieve all required Identity Claims.
            var claims = await _context.UserClaims
                .Where(c => allRelevantUserIds.Contains(c.UserId))
                .ToListAsync();

            // Populate the lookup dictionary.
            foreach (var uid in allRelevantUserIds)
            {
                var userClaims = claims.Where(c => c.UserId == uid).ToList();
                var fullName = userClaims.FirstOrDefault(c => c.ClaimType == "FullName")?.ClaimValue ?? "İsimsiz Üye";
                var avatarUrl = userClaims.FirstOrDefault(c => c.ClaimType == "AvatarUrl")?.ClaimValue ?? "";

                // Fallback avatar generation logic (Initials).
                var nameParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var initials = nameParts.Length > 1
                    ? (nameParts[0][0].ToString() + nameParts[nameParts.Length - 1][0].ToString()).ToUpper()
                    : (fullName.Length > 1 ? fullName.Substring(0, 2).ToUpper() : fullName.ToUpper());

                UserProfiles[uid] = new UserProfileData { FullName = fullName, Initials = initials, AvatarUrl = avatarUrl };
            }

            return Page();
        }

        // --- FAULT TOLERANCE: RETRY MECHANISM ---
        public async Task<IActionResult> OnPostRetryProcessingAsync(int id)
        {
            var meeting = await _context.MeetingSummaries.FindAsync(id);
            if (meeting == null) return NotFound();

            // Revert state machine back to Processing status.
            meeting.Status = MeetingStatus.Processing;
            await _context.SaveChangesAsync();

            string relativePath = meeting.AudioFilePath ?? "";

            if (!string.IsNullOrEmpty(relativePath))
            {
                // PATH RECONSTRUCTION: Rebuild absolute physical path for the Hangfire background worker.
                string fileName = Path.GetFileName(relativePath);
                string absolutePath = Path.Combine(_environment.WebRootPath, "uploads", fileName);

                // Re-enqueue the AI pipeline job.
                _backgroundJobClient.Enqueue<IMeetingProcessor>(x => x.ProcessMeetingJob(meeting.Id, absolutePath));
                TempData["StatusMessage"] = "Yeniden analiz işlemi başlatıldı. Lütfen bekleyin...";
            }
            else
            {
                TempData["ErrorMessage"] = "Sistemde ses dosyası bulunamadığı için işlem başlatılamadı.";
            }

            return RedirectToPage(new { id = meeting.Id });
        }

        // --- COLLABORATION & INTERACTION HANDLERS ---

        public async Task<IActionResult> OnPostAddCommentAsync(int meetingId, string content, int? parentCommentId)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null || string.IsNullOrWhiteSpace(content))
                return RedirectToPage(new { id = meetingId });

            var newComment = new MeetingComment
            {
                MeetingId = meetingId,
                UserId = userId,
                Content = content,
                ParentCommentId = parentCommentId,
                CreatedAt = DateTime.UtcNow
            };

            _context.MeetingComments.Add(newComment);
            await _context.SaveChangesAsync();

            // Track the newly created comment ID for UI highlighting.
            TempData["NewCommentId"] = newComment.Id;

            return Redirect($"{Url.Page("", new { id = meetingId })}#comment-{newComment.Id}");
        }

        public async Task<IActionResult> OnPostDeleteCommentAsync(int commentId, int meetingId)
        {
            var currentUserId = _userManager.GetUserId(User);

            var comment = await _context.MeetingComments
                .Include(c => c.Replies)
                .Include(c => c.Reactions)
                .FirstOrDefaultAsync(c => c.Id == commentId);

            if (comment == null) return NotFound();

            // Authorization: Ensure the requester owns the comment.
            if (comment.UserId != currentUserId)
            {
                TempData["ErrorMessage"] = "Sadece kendi yorumlarınızı silebilirsiniz.";
                return RedirectToPage(new { id = meetingId });
            }

            // Cascade Delete: Wipe nested replies before removing the parent.
            if (comment.Replies.Any())
            {
                _context.MeetingComments.RemoveRange(comment.Replies);
            }

            _context.MeetingComments.Remove(comment);
            await _context.SaveChangesAsync();

            TempData["StatusMessage"] = "Yorum başarıyla silindi.";
            return Redirect($"{Url.Page("", new { id = meetingId })}#comments-section");
        }

        // AJAX Handler: Toggles Like/Dislike state asynchronously.
        public async Task<IActionResult> OnPostToggleReactionAsync(int commentId, bool isLike)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return new JsonResult(new { success = false });

            var existingReaction = await _context.CommentReactions
                .FirstOrDefaultAsync(r => r.CommentId == commentId && r.UserId == userId);

            if (existingReaction != null)
            {
                // Toggle off if the same reaction is clicked, else update the reaction type.
                if (existingReaction.IsLike == isLike)
                    _context.CommentReactions.Remove(existingReaction);
                else
                    existingReaction.IsLike = isLike;
            }
            else
            {
                _context.CommentReactions.Add(new CommentReaction { CommentId = commentId, UserId = userId, IsLike = isLike });
            }

            await _context.SaveChangesAsync();

            var likes = await _context.CommentReactions.CountAsync(r => r.CommentId == commentId && r.IsLike);
            var dislikes = await _context.CommentReactions.CountAsync(r => r.CommentId == commentId && !r.IsLike);

            return new JsonResult(new { success = true, likes, dislikes });
        }

        // --- MANUAL METADATA INJECTION HANDLERS ---

        public async Task<IActionResult> OnPostAddTopicAsync(int meetingId, string topic)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(topic) || userId == null) return RedirectToPage(new { id = meetingId });

            var meeting = await _context.MeetingSummaries.FindAsync(meetingId);
            if (meeting != null)
            {
                meeting.GorusulenKonular ??= new List<string>();
                var updatedList = meeting.GorusulenKonular.ToList();

                // Append the user ID using the delimiter for future author resolution.
                updatedList.Add($"{topic}|~|{userId}");

                meeting.GorusulenKonular = updatedList;
                _context.MeetingSummaries.Update(meeting);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage(new { id = meetingId });
        }

        public async Task<IActionResult> OnPostAddDecisionAsync(int meetingId, string decision)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(decision) || userId == null) return RedirectToPage(new { id = meetingId });

            var meeting = await _context.MeetingSummaries.FindAsync(meetingId);
            if (meeting != null)
            {
                meeting.AlinanKararlar ??= new List<string>();
                var updatedList = meeting.AlinanKararlar.ToList();

                updatedList.Add($"{decision}|~|{userId}");

                meeting.AlinanKararlar = updatedList;
                _context.MeetingSummaries.Update(meeting);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage(new { id = meetingId });
        }

        public async Task<IActionResult> OnPostAddActionItemAsync(int meetingId, string actionText)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(actionText) || userId == null) return RedirectToPage(new { id = meetingId });

            var meeting = await _context.MeetingSummaries
                .Include(m => m.AksiyonMaddeleri)
                .FirstOrDefaultAsync(m => m.Id == meetingId);

            if (meeting != null)
            {
                meeting.AksiyonMaddeleri.Add(new ActionItem { Text = $"{actionText}|~|{userId}", IsDone = false });
                await _context.SaveChangesAsync();
            }
            return RedirectToPage(new { id = meetingId });
        }

        // AJAX Handler: Updates the completion status of a task without page reload.
        public async Task<IActionResult> OnPostToggleTaskAsync(int taskId)
        {
            var task = await _context.Set<ActionItem>().FindAsync(taskId);
            if (task != null)
            {
                task.IsDone = !task.IsDone;
                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true, isDone = task.IsDone });
            }
            return new JsonResult(new { success = false });
        }

        // Hangfire Integration: Dispatches background jobs to transmit reports via email.
        public IActionResult OnPostShareAsync(int meetingId, List<string> selectedEmails)
        {
            foreach (var email in selectedEmails)
            {
                BackgroundJob.Enqueue<IEmailService>(x => x.SendMeetingSummaryAsync(meetingId, email));
            }
            TempData["StatusMessage"] = "Rapor gönderimi arka planda başlatıldı!";
            return RedirectToPage(new { id = meetingId });
        }
    }
}