using Microsoft.AspNetCore.SignalR;

namespace MeetingWeb.Hubs
{
    // WebSocket endpoint to broadcast real-time meeting processing status to connected clients.
    public class MeetingHub : Hub
    {
        // Currently acting as a one-way (Server-to-Client) push notification channel.
        // No client-to-server methods are required for the MVP status updates.
    }
}