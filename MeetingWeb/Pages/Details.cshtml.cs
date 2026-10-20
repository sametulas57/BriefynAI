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
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IAudioStorage _audioStorage;
        private readonly IMeetingAccessService _access;

        // Constructor injection for required services and physical path resolution.
        public DetailsModel(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
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

        public Meeting Meeting { get; set; } = default!;
        public List<WorkspaceMember> WorkspaceMembers { get; set; } = new();
        public List<Comment> Comments { get; set; } = new();

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

            // Eager load the core meeting entity, its summary lines, tasks and transcript.
            var meeting = await _context.Meetings
                .Include(m => m.Points)
                .Include(m => m.ActionItems)
                .Include(m => m.Transcript)
                .AsSplitQuery()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (meeting == null) return MeetingUnavailable();
            Meeting = meeting;

            // Fetch the members of the meeting's own workspace for the sharing/collaboration module.
            WorkspaceMembers = await _context.WorkspaceMembers
                .Include(wu => wu.User)
                .Where(wu => wu.WorkspaceId == meeting.WorkspaceId)
                .ToListAsync();

            // Eager load root comments, nested replies, and associated user reactions.
            Comments = await _context.Comments
                .Include(c => c.Reactions)
                .Include(c => c.Replies).ThenInclude(r => r.Reactions)
                .Where(c => c.MeetingId == id && c.ParentCommentId == null)
                .OrderByDescending(c => c.CreatedAtUtc)
                .AsSplitQuery()
                .ToListAsync();

            // --- BULK METADATA RESOLUTION PIPELINE ---
            // Everyone shown on the page: members, comment authors, authors of manual lines, assignees and completers
            // (who may have left the team since; their names are still shown).
            var allRelevantUserIds = WorkspaceMembers.Select(w => w.UserId)
                .Concat(Comments.Select(c => c.UserId))
                .Concat(Comments.SelectMany(c => c.Replies).Select(r => r.UserId))
                .Concat(Meeting.Points.Select(p => p.CreatedByUserId).OfType<string>())
                .Concat(Meeting.ActionItems.SelectMany(a => new[] { a.CreatedByUserId, a.AssigneeUserId, a.CompletedByUserId }).OfType<string>())
                .Distinct()
                .ToList();

            // A single batched query for names and photos.
            var users = await _context.Users
                .Where(u => allRelevantUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.AvatarFileName })
                .ToListAsync();

            foreach (var u in users)
            {
                var fullName = string.IsNullOrWhiteSpace(u.FullName) ? UserDisplay.UnknownName : u.FullName;
                UserProfiles[u.Id] = new UserProfileData { FullName = fullName, Initials = UserDisplay.Initials(fullName), AvatarUrl = UserDisplay.AvatarUrl(u.AvatarFileName) };
            }

            return Page();
        }

        // --- FAULT TOLERANCE: RETRY MECHANISM ---
        public async Task<IActionResult> OnPostRetryProcessingAsync(int id)
        {
            if (!await CanAccessAsync(id)) return MeetingUnavailable();
            var meeting = await _context.Meetings.Include(m => m.Transcript).FirstOrDefaultAsync(m => m.Id == id);
            if (meeting == null) return NotFound();

            // Recordings are deleted after a successful analysis and after the failed-retry window,
            // so only flip the state machine when there is actually something to re-process.
            string? absolutePath = string.IsNullOrEmpty(meeting.AudioFileName) ? null : _audioStorage.GetPath(meeting.AudioFileName);

            if (absolutePath != null && System.IO.File.Exists(absolutePath))
            {
                meeting.Status = MeetingStatus.Processing;
                await _context.SaveChangesAsync();

                // Re-enqueue the AI pipeline job.
                _backgroundJobClient.Enqueue<IMeetingProcessor>(x => x.ProcessMeetingJob(meeting.Id));
                TempData["StatusMessage"] = "Kayıt yeniden analiz ediliyor. Bittiğinde bu sayfa kendiliğinden yenilenir.";
            }
            else if (!string.IsNullOrWhiteSpace(meeting.Transcript?.Text))
            {
                // Faz 2: the transcript is stored, so only the summary is generated again.
                meeting.Status = MeetingStatus.Processing;
                await _context.SaveChangesAsync();

                _backgroundJobClient.Enqueue<IMeetingProcessor>(x => x.ResummarizeMeetingJob(meeting.Id));
                TempData["StatusMessage"] = "Özet, kayıtlı dökümden yeniden hazırlanıyor. Bittiğinde bu sayfa kendiliğinden yenilenir.";
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
            if (content.Length > Comment.MaxContentLength)
            {
                TempData["ErrorMessage"] = $"Yorum en fazla {Comment.MaxContentLength} karakter olabilir.";
                return RedirectToPage(new { id = meetingId });
            }

            // A reply must belong to the same meeting; otherwise it could be attached to another tenant's thread.
            if (parentCommentId != null &&
                !await _context.Comments.AnyAsync(c => c.Id == parentCommentId && c.MeetingId == meetingId))
                return MeetingUnavailable();

            var newComment = new Comment
            {
                MeetingId = meetingId,
                UserId = userId,
                Content = content,
                ParentCommentId = parentCommentId
            };

            _context.Comments.Add(newComment);
            await _context.SaveChangesAsync();

            // Track the newly created comment ID for UI highlighting.
            TempData["NewCommentId"] = newComment.Id;

            return Redirect($"{Url.Page("", new { id = meetingId })}#comment-{newComment.Id}");
        }

        public async Task<IActionResult> OnPostDeleteCommentAsync(int commentId, int meetingId)
        {
            var currentUserId = _userManager.GetUserId(User);
            if (!await _access.CanAccessCommentAsync(currentUserId, commentId)) return MeetingUnavailable();

            var comment = await _context.Comments
                .Include(c => c.Replies)
                .FirstOrDefaultAsync(c => c.Id == commentId);

            if (comment == null) return NotFound();

            // Authorization: Ensure the requester owns the comment.
            if (comment.UserId != currentUserId)
            {
                TempData["ErrorMessage"] = "Sadece kendi yorumlarınızı silebilirsiniz.";
                return RedirectToPage(new { id = meetingId });
            }

            // Replies are loaded above and removed with the parent (reactions cascade in the database).
            _context.Comments.Remove(comment);
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

        public Task<IActionResult> OnPostAddTopicAsync(int meetingId, string topic) => AddPointAsync(meetingId, MeetingPointKind.Topic, topic);

        public Task<IActionResult> OnPostAddDecisionAsync(int meetingId, string decision) => AddPointAsync(meetingId, MeetingPointKind.Decision, decision);

        // A manual line is its own row, so it never races with a summary being regenerated.
        private async Task<IActionResult> AddPointAsync(int meetingId, MeetingPointKind kind, string text)
        {
            if (!await CanAccessAsync(meetingId)) return MeetingUnavailable();
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(text) || userId == null) return RedirectToPage(new { id = meetingId });

            var nextOrder = await _context.MeetingPoints
                .Where(p => p.MeetingId == meetingId && p.Kind == kind && p.Source == EntrySource.Manual)
                .Select(p => (int?)p.SortOrder).MaxAsync() + 1 ?? 0;
            _context.MeetingPoints.Add(new MeetingPoint
            {
                MeetingId = meetingId, Kind = kind, Text = text, Source = EntrySource.Manual, CreatedByUserId = userId, SortOrder = nextOrder
            });
            await _context.SaveChangesAsync();
            return RedirectToPage(new { id = meetingId });
        }

        public async Task<IActionResult> OnPostAddActionItemAsync(int meetingId, string actionText)
        {
            if (!await CanAccessAsync(meetingId)) return MeetingUnavailable();
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(actionText) || userId == null) return RedirectToPage(new { id = meetingId });

            var nextOrder = await _context.ActionItems
                .Where(a => a.MeetingId == meetingId)
                .Select(a => (int?)a.SortOrder).MaxAsync() + 1 ?? 0;
            _context.ActionItems.Add(new ActionItem
            {
                MeetingId = meetingId, Text = actionText, Source = EntrySource.Manual, CreatedByUserId = userId, SortOrder = nextOrder
            });
            await _context.SaveChangesAsync();
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
            var userId = _userManager.GetUserId(User);
            var task = await _context.ActionItems.FindAsync(taskId);
            if (task != null)
            {
                task.SetDone(!task.IsDone, userId);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Someone changed the task at the same moment; the page reloads to show their change.
                    return new JsonResult(new { success = false, reload = true });
                }

                var completedBy = task.IsDone ? await FullNameAsync(userId) : null;
                return new JsonResult(new { success = true, isDone = task.IsDone, completedBy });
            }
            return new JsonResult(new { success = false });
        }

        private async Task<string> FullNameAsync(string? userId) =>
            await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync() is { Length: > 0 } name ? name : UserDisplay.UnknownName;

        // Assigns a task to a member of the meeting's team, with an optional due date.
        // An empty assignee clears the assignment.
        public async Task<IActionResult> OnPostAssignActionAsync(int taskId, string? assigneeUserId, DateOnly? dueDate)
        {
            if (!await _access.CanAccessActionItemAsync(_userManager.GetUserId(User), taskId)) return MeetingUnavailable();

            var task = await _context.ActionItems
                .Include(a => a.Meeting)
                .FirstAsync(a => a.Id == taskId);
            var meetingId = task.MeetingId;

            if (string.IsNullOrEmpty(assigneeUserId))
            {
                task.AssigneeUserId = null;
                task.DueDate = null;
                if (!await TrySaveTaskAsync()) return RedirectToPage(new { id = meetingId });
                TempData["StatusMessage"] = "Görevin ataması kaldırıldı.";
                return RedirectToPage(new { id = meetingId });
            }

            var isTeammate = await _context.WorkspaceMembers
                .AnyAsync(wu => wu.WorkspaceId == task.Meeting!.WorkspaceId && wu.UserId == assigneeUserId);
            if (!isTeammate)
            {
                TempData["ErrorMessage"] = "Görev yalnız bu toplantının ekibindeki birine atanabilir.";
                return RedirectToPage(new { id = meetingId });
            }

            task.AssigneeUserId = assigneeUserId;
            task.DueDate = dueDate;
            if (!await TrySaveTaskAsync()) return RedirectToPage(new { id = meetingId });

            TempData["StatusMessage"] = $"Görev atandı: {await FullNameAsync(assigneeUserId)}.";
            return RedirectToPage(new { id = meetingId });
        }

        // RowVersion: when someone else changed the task meanwhile, nothing is overwritten and the user is told why.
        private async Task<bool> TrySaveTaskAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["ErrorMessage"] = "Bu görev siz düzenlerken başka biri tarafından değiştirildi. Güncel hâli aşağıda; isterseniz tekrar deneyin.";
                return false;
            }
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
            var memberEmails = await _context.WorkspaceMembers
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
                var recipient = EmailService.ProtectRecipient(email);
                _backgroundJobClient.Enqueue<IEmailService>(x => x.SendMeetingSummaryAsync(meetingId, recipient));
            }
            TempData["StatusMessage"] = "Rapor e-postayla gönderiliyor. Birkaç dakika içinde seçtiğiniz kişilerin gelen kutusunda olur.";
            return RedirectToPage(new { id = meetingId });
        }
    }
}