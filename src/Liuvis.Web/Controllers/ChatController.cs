using System.Text.Json;
using Liuvis.Core.DTOs.Requests;
using Liuvis.Core.DTOs.Responses;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Liuvis.Web.Hubs;
using Liuvis.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Liuvis.Web.Controllers;

/// <summary>Chat API — delegates to ChatOrchestrationService for pipeline processing.</summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly ChatOrchestrationService _orchestration;
    private readonly ISessionManager _sessionManager;
    private readonly IHubContext<DesignHub> _hubContext;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        ChatOrchestrationService orchestration,
        ISessionManager sessionManager,
        IHubContext<DesignHub> hubContext,
        ILogger<ChatController> logger)
    {
        _orchestration = orchestration;
        _sessionManager = sessionManager;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>Streaming SSE endpoint for real-time progress updates.</summary>
    [HttpPost("stream")]
    public async Task StreamChat([FromBody] ChatRequest request)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await SseWriteAsync("error", "Message cannot be empty.");
            return;
        }

        try
        {
            var response = await _orchestration.ProcessMessageAsync(
                request.SessionId,
                request.Message,
                onProgress: status => SseWriteAsync("progress", status).GetAwaiter().GetResult());

            if (!string.IsNullOrEmpty(response.Thinking))
                await SseWriteAsync("thinking", response.Thinking);

            if (response.Type is "model_ready" or "model_updated")
            {
                await _hubContext.Clients.Group(request.SessionId.ToString())
                    .SendAsync("ModelReady", new { response.ModelId, response.ModelName });
            }

            await SseWriteAsync("complete", JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat stream failed");
            await SseWriteAsync("error", ex.Message);
        }
    }

    private async Task SseWriteAsync(string eventType, string data)
    {
        await Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n");
        await Response.Body.FlushAsync();
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ChatResponse>>> SendMessage([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(ApiResponse<ChatResponse>.Error(400, "Message cannot be empty."));

        try
        {
            var response = await _orchestration.ProcessMessageAsync(request.SessionId, request.Message);

            if (response.Type is "model_ready" or "model_updated")
            {
                await _hubContext.Clients.Group(request.SessionId.ToString())
                    .SendAsync("ModelReady", new { response.ModelId, response.ModelName });
            }

            return Ok(ApiResponse<ChatResponse>.Ok(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat processing failed");
            return StatusCode(500, ApiResponse<ChatResponse>.Error(500, "Internal processing error."));
        }
    }
}
