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
        private readonly IAudioStorage _audioStorage;
        private readonly IMeetingAccessService _access;

        // Constructor injection for required services and physical path resolution.
        public DetailsModel(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            IBackgroundJobClient backgroundJobClient,
            IAudioStorage audioStorage,
            IMeetingAccessService access)
        {
            _context = context;
            _userManager = userManager;
            _backgroundJobClient = backgroundJobClient;
            _audioStorage = audioStorage;
            _access = access;
        }

        // AUTHORIZATION: every handler resolves access through the meeting's workspace, never the session.
        // Unauthorized requests get 404 so outsiders cannot even confirm that a meeting id exists.
        private Task<bool> CanAccessAsync(int meetingId) => _access.CanAccessMeetingAsync(_userManager.GetUserId(User), meetingId);

        // For page visits a dead-end 404 is unfriendly: send the user to the history list with an explanation.
        // The message is identical for "no access" and "does not exist", so outsiders still learn nothing.
        private IActionResult MeetingUnavailable()
        {
            TempData["ErrorMessage"] = "Bu özet seçili ekipte bulunamadı. Ekibinizin geçmiş özetlerini görüyorsunuz.";
            return RedirectToPage("/History");
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
            if (id == null || !await CanAccessAsync(id.Value)) return MeetingUnavailable();

            // Eager load the core meeting entity and its actionable tasks.
            var meeting = await _context.MeetingSummaries
                .Include(m => m.AksiyonMaddeleri)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (meeting == null) return MeetingUnavailable();
            Meeting = meeting;

            // Fetch the members of the meeting's own workspace for the sharing/collaboration module.
            WorkspaceMembers = await _context.WorkspaceUsers
                .Include(wu => wu.User)
                .Where(wu => wu.WorkspaceId == meeting.WorkspaceId)
                .ToListAsync();

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
            if (!await CanAccessAsync(id)) return MeetingUnavailable();
            var meeting = await _context.MeetingSummaries.FindAsync(id);
            if (meeting == null) return NotFound();

            // Recordings are deleted after a successful analysis and after the failed-retry window,
            // so only flip the state machine when there is actually a file to re-process.
            string? absolutePath = string.IsNullOrEmpty(meeting.AudioFilePath) ? null : _audioStorage.GetPath(meeting.AudioFilePath);

            if (absolutePath != null && System.IO.File.Exists(absolutePath))
            {
                meeting.Status = MeetingStatus.Processing;
                await _context.SaveChangesAsync();

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
            if (!await CanAccessAsync(meetingId)) return MeetingUnavailable();
            if (userId == null || string.IsNullOrWhiteSpace(content))
                return RedirectToPage(new { id = meetingId });

            // A reply must belong to the same meeting; otherwise it could be attached to another tenant's thread.
            if (parentCommentId != null &&
                !await _context.MeetingComments.AnyAsync(c => c.Id == parentCommentId && c.MeetingId == meetingId))
                return MeetingUnavailable();

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
            if (!await _access.CanAccessCommentAsync(currentUserId, commentId)) return MeetingUnavailable();

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
            if (!await _access.CanAccessCommentAsync(userId, commentId)) return NotFound();

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
            if (!await CanAccessAsync(meetingId)) return MeetingUnavailable();
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
            if (!await CanAccessAsync(meetingId)) return MeetingUnavailable();
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
            if (!await CanAccessAsync(meetingId)) return MeetingUnavailable();
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

        private static bool IsValidEmail(string value)
        {
            try { return new System.Net.Mail.MailAddress(value).Address == value; }
            catch (FormatException) { return false; }
        }

        // AJAX Handler: Updates the completion status of a task without page reload.
        public async Task<IActionResult> OnPostToggleTaskAsync(int taskId)
        {
            if (!await _access.CanAccessActionItemAsync(_userManager.GetUserId(User), taskId)) return NotFound();
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
        // Addresses outside the team are allowed for sharing (e.g. a client, or a summary from a personal space),
        // but capped per send; together with the hourly share limit this keeps the sender account from being abused.
        public const int MaxExtraRecipients = 3;

        public async Task<IActionResult> OnPostShareAsync(int meetingId, List<string> selectedEmails, string? extraEmails)
        {
            if (!await CanAccessAsync(meetingId)) return MeetingUnavailable();

            var extras = (extraEmails ?? "")
                .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (extras.Count > MaxExtraRecipients)
            {
                TempData["ErrorMessage"] = $"Ekip dışından en fazla {MaxExtraRecipients} adres ekleyebilirsiniz. Rapor gönderilmedi.";
                return RedirectToPage(new { id = meetingId });
            }
            var invalid = extras.Where(e => !IsValidEmail(e)).ToList();
            if (invalid.Count > 0)
            {
                TempData["ErrorMessage"] = $"{string.Join(", ", invalid)} geçerli bir e-posta adresi değil. Düzeltip tekrar deneyin; rapor gönderilmedi.";
                return RedirectToPage(new { id = meetingId });
            }

            // Reports may only go to members of the meeting's workspace, never to arbitrary addresses.
            var memberEmails = await _context.WorkspaceUsers
                .Where(wu => wu.Workspace.Meetings.Any(m => m.Id == meetingId))
                .Select(wu => wu.User.Email)
                .ToListAsync();
            var recipients = selectedEmails
                .Where(e => memberEmails.Contains(e, StringComparer.OrdinalIgnoreCase))
                .Concat(extras)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (recipients.Count == 0)
            {
                TempData["ErrorMessage"] = "Raporu göndermek için en az bir kişi seçin ya da bir e-posta adresi yazın.";
                return RedirectToPage(new { id = meetingId });
            }

            foreach (var email in recipients)
            {
                _backgroundJobClient.Enqueue<IEmailService>(x => x.SendMeetingSummaryAsync(meetingId, email));
            }
            TempData["StatusMessage"] = "Rapor e-postayla gönderiliyor. Birkaç dakika içinde seçtiğiniz kişilerin gelen kutusunda olur.";
            return RedirectToPage(new { id = meetingId });
        }
    }
}