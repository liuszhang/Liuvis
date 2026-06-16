using Microsoft.AspNetCore.SignalR;

namespace Liuvis.Web.Hubs;

/// <summary>
/// SignalR hub for real-time design collaboration.
/// Clients join a session room to receive progress, thinking, and model events.
/// </summary>
public class DesignHub : Hub
{
    private readonly ILogger<DesignHub> _logger;

    public DesignHub(ILogger<DesignHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinDesignRoom(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("Client {ConnectionId} joined room {SessionId}", Context.ConnectionId, sessionId);

        await Clients.Caller.SendAsync("Joined", new
        {
            SessionId = sessionId,
            ConnectionId = Context.ConnectionId,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LeaveDesignRoom(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("Client {ConnectionId} left room {SessionId}", Context.ConnectionId, sessionId);

        await Clients.Caller.SendAsync("Left", new
        {
            SessionId = sessionId,
            ConnectionId = Context.ConnectionId,
            Timestamp = DateTime.UtcNow
        });
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Client connected: {ConnectionId}", Context.ConnectionId);
        await Clients.Caller.SendAsync("Connected", new
        {
            ConnectionId = Context.ConnectionId,
            Timestamp = DateTime.UtcNow
        });
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
            _logger.LogWarning(exception, "Client disconnected with error: {ConnectionId}", Context.ConnectionId);
        else
            _logger.LogDebug("Client disconnected: {ConnectionId}", Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }
}
