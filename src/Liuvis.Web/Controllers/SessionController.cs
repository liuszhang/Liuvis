using Liuvis.Core.DTOs.Responses;
using Liuvis.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Liuvis.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionController : ControllerBase
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionController> _logger;

    public SessionController(ISessionManager sessionManager, ILogger<SessionController> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<SessionResponse>>> CreateSession()
    {
        var userId = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        var session = await _sessionManager.CreateSession(userId);

        var response = new SessionResponse
        {
            SessionId = session.SessionId,
            Status = session.Status,
            CurrentModelId = session.CurrentModelId,
            MessageCount = session.Messages.Count,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt
        };

        _logger.LogInformation("Session created: {SessionId}", session.SessionId);
        return Ok(ApiResponse<SessionResponse>.Ok(response));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<SessionResponse>>> GetSession(Guid id)
    {
        var session = await _sessionManager.GetSession(id);
        if (session == null)
            return NotFound(ApiResponse<SessionResponse>.Error(404, "Session not found."));

        var response = new SessionResponse
        {
            SessionId = session.SessionId,
            Status = session.Status,
            CurrentModelId = session.CurrentModelId,
            MessageCount = session.Messages.Count,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt
        };

        return Ok(ApiResponse<SessionResponse>.Ok(response));
    }
}
