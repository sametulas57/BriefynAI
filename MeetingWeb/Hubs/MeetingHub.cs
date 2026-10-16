using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MeetingWeb.Hubs
{
    // WebSocket endpoint to push real-time meeting processing status to connected clients.
    // S3: only signed-in users may connect; MeetingProcessor targets the meeting's workspace members by user id.
    [Authorize]
    public class MeetingHub : Hub
    {
        // Currently acting as a one-way (Server-to-Client) push notification channel.
        // No client-to-server methods are required for the MVP status updates.
    }
}