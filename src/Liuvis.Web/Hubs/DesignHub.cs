using Microsoft.AspNetCore.SignalR;

namespace Liuvis.Web.Hubs;

public class DesignHub : Hub
{
    public async Task JoinDesignRoom(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    public async Task LeaveDesignRoom(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }
}
