using MeetingWeb.Data;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services
{
    // Single source of truth for "may this user touch this meeting?".
    // A user can access a meeting only if they are a member of the meeting's workspace.
    // The session's ActiveWorkspaceId is deliberately NOT trusted here: it is a UI convenience, not an authorization fact.
    public interface IMeetingAccessService
    {
        Task<bool> CanAccessMeetingAsync(string? userId, int meetingId);
        Task<bool> CanAccessActionItemAsync(string? userId, int actionItemId);
        Task<bool> CanAccessCommentAsync(string? userId, int commentId);
    }

    public class MeetingAccessService : IMeetingAccessService
    {
        private readonly ApplicationDbContext _context;

        public MeetingAccessService(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<bool> CanAccessMeetingAsync(string? userId, int meetingId)
        {
            if (string.IsNullOrEmpty(userId)) return Task.FromResult(false);

            return _context.MeetingSummaries.AnyAsync(m =>
                m.Id == meetingId &&
                m.WorkspaceId != null &&
                _context.WorkspaceUsers.Any(wu => wu.WorkspaceId == m.WorkspaceId && wu.UserId == userId));
        }

        public async Task<bool> CanAccessActionItemAsync(string? userId, int actionItemId)
        {
            var meetingId = await _context.Set<Models.ActionItem>()
                .Where(a => a.Id == actionItemId)
                .Select(a => (int?)a.MeetingSummaryId)
                .FirstOrDefaultAsync();

            return meetingId != null && await CanAccessMeetingAsync(userId, meetingId.Value);
        }

        public async Task<bool> CanAccessCommentAsync(string? userId, int commentId)
        {
            var meetingId = await _context.MeetingComments
                .Where(c => c.Id == commentId)
                .Select(c => (int?)c.MeetingId)
                .FirstOrDefaultAsync();

            return meetingId != null && await CanAccessMeetingAsync(userId, meetingId.Value);
        }
    }
}
