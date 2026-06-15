using System.Text.Json;
using Liuvis.Core.DTOs.Requests;
using Liuvis.Core.DTOs.Responses;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;
using Liuvis.Web.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Liuvis.Web.Controllers;

/// <summary>Chat API — processes user messages and orchestrates the design pipeline.</summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly INluService _nluService;
    private readonly ISessionManager _sessionManager;
    private readonly IKnowledgeBaseService _kbService;
    private readonly IDesignEngine _designEngine;
    private readonly IModelGenerator _modelGenerator;
    private readonly IModificationEngine _modificationEngine;
    private readonly ILlmClient _llmClient;
    private readonly IHubContext<DesignHub> _hubContext;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        INluService nluService,
        ISessionManager sessionManager,
        IKnowledgeBaseService kbService,
        IDesignEngine designEngine,
        IModelGenerator modelGenerator,
        IModificationEngine modificationEngine,
        ILlmClient llmClient,
        IHubContext<DesignHub> hubContext,
        ILogger<ChatController> logger)
    {
        _nluService = nluService;
        _sessionManager = sessionManager;
        _kbService = kbService;
        _designEngine = designEngine;
        _modelGenerator = modelGenerator;
        _modificationEngine = modificationEngine;
        _llmClient = llmClient;
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
            _logger.LogInformation("Chat stream: Session={SessionId}, Message={Message}",
                request.SessionId, request.Message[..Math.Min(request.Message.Length, 50)]);

            var session = await _sessionManager.GetSession(request.SessionId);
            if (session == null)
            {
                await SseWriteAsync("error", "Session not found.");
                return;
            }

            await _sessionManager.AddMessage(request.SessionId, MessageRole.User, request.Message);

            // ---- Phase 1: NLU ----
            await SseWriteAsync("progress", "Parsing your request...");
            var thinkSb = new System.Text.StringBuilder();
            var intent = await _nluService.ParseIntent(request.Message);
            if (!string.IsNullOrEmpty(intent.Thinking))
                await SseWriteAsync("thinking", intent.Thinking);
            await SseWriteAsync("progress", $"Intent: {intent.IntentType} (confidence: {intent.Confidence:P0})");

            // ---- Phase 2: Design ----
            ChatResponse response;
            switch (intent.IntentType)
            {
                case IntentType.Create:
                    await SseWriteAsync("progress", "Searching knowledge base...");
                    var matches = await _kbService.SearchModels(request.Message, topK: 5);
                    await SseWriteAsync("progress", $"Found {matches.Count} reusable component(s). Designing...");

                    var plan = await _designEngine.CreateDesignPlan(intent, matches);
                    await SseWriteAsync("progress", "Generating geometry with AI...");

                    var spec = await _designEngine.GenerateDesignSpec(plan);
                    var model = await _modelGenerator.GenerateModel(spec);
                    await _sessionManager.UpdateModelRef(request.SessionId, model.ModelId);

                    await _hubContext.Clients.Group(request.SessionId.ToString())
                        .SendAsync("ModelReady", new { model.ModelId, model.Name, model.FilePath });

                    var assistantMsg = await _sessionManager.AddMessage(request.SessionId, MessageRole.Assistant,
                        $"Created model: **{model.Name}** with {model.Components.Count} components.");

                    response = new ChatResponse
                    {
                        SessionId = request.SessionId,
                        MessageId = assistantMsg.MessageId,
                        AssistantMessage = $"Created model: {model.Name}",
                        IntentType = intent.IntentType,
                        ModelId = model.ModelId,
                        ModelName = model.Name,
                        Thinking = intent.Thinking,
                        Type = "model_ready"
                    };
                    break;

                case IntentType.Modify:
                    response = await HandleModifyIntent(request, intent, session);
                    break;

                case IntentType.Query:
                    response = await HandleQueryIntent(request, intent);
                    break;

                default:
                    await SseWriteAsync("progress", "Asking AI...");
                    var (think, reply) = await GetThinkingResponse(request.Message,
                        "I'm not sure what you want to do. Could you describe the 3D model you'd like to create or modify?");
                    if (!string.IsNullOrEmpty(think))
                        await SseWriteAsync("thinking", think);
                    response = new ChatResponse
                    {
                        SessionId = request.SessionId,
                        IntentType = IntentType.Unknown,
                        AssistantMessage = reply,
                        Thinking = think,
                        Type = "text"
                    };
                    break;
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
            _logger.LogInformation("Chat request: Session={SessionId}, Message={Message}",
                request.SessionId, request.Message[..Math.Min(request.Message.Length, 50)]);

            // 1. Ensure session exists
            var session = await _sessionManager.GetSession(request.SessionId);
            if (session == null)
                return NotFound(ApiResponse<ChatResponse>.Error(404, "Session not found."));

            // 2. Store user message
            await _sessionManager.AddMessage(request.SessionId, MessageRole.User, request.Message);

            // 3. Parse intent
            var intent = await _nluService.ParseIntent(request.Message);
            await _hubContext.Clients.Group(request.SessionId.ToString())
                .SendAsync("IntentParsed", intent);

            // 4. Route by intent type
            ChatResponse response;

            switch (intent.IntentType)
            {
                case IntentType.Create:
                    response = await HandleCreateIntent(request, intent);
                    break;

                case IntentType.Modify:
                    response = await HandleModifyIntent(request, intent, session);
                    break;

                case IntentType.Query:
                    response = await HandleQueryIntent(request, intent);
                    break;

                default:
                    var (think, reply) = await GetThinkingResponse(request.Message,
                        "I'm not sure what you want to do. Could you describe the 3D model you'd like to create or modify?");
                    response = new ChatResponse
                    {
                        SessionId = request.SessionId,
                        IntentType = IntentType.Unknown,
                        AssistantMessage = reply,
                        Thinking = think,
                        Type = "text"
                    };
                    break;
            }

            return Ok(ApiResponse<ChatResponse>.Ok(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat processing failed");
            return StatusCode(500, ApiResponse<ChatResponse>.Error(500, "Internal processing error."));
        }
    }

    private async Task<ChatResponse> HandleCreateIntent(ChatRequest request, IntentResult intent)
    {
        // Search knowledge base for reusable components
        var embedding = await _llmClient.GetEmbeddingAsync(request.Message);
        var matches = await _kbService.SearchModels(request.Message, topK: 5);

        // Generate design plan
        var plan = await _designEngine.CreateDesignPlan(intent, matches);

        // Generate design spec
        var spec = await _designEngine.GenerateDesignSpec(plan);

        // Generate 3D model
        var model = await _modelGenerator.GenerateModel(spec);

        // Update session with new model
        await _sessionManager.UpdateModelRef(request.SessionId, model.ModelId);

        // Notify via SignalR
        await _hubContext.Clients.Group(request.SessionId.ToString())
            .SendAsync("ModelReady", new { model.ModelId, model.Name, model.FilePath });

        // Add assistant message
        var assistantMsg = await _sessionManager.AddMessage(request.SessionId, MessageRole.Assistant,
            $"I've created a 3D model: **{model.Name}** with {model.Components.Count} components.");

        return new ChatResponse
        {
            SessionId = request.SessionId,
            MessageId = assistantMsg.MessageId,
            AssistantMessage = $"Created model: {model.Name}",
            IntentType = intent.IntentType,
            ModelId = model.ModelId,
            ModelName = model.Name,
            Thinking = intent.Thinking,
            Type = "model_ready"
        };
    }

    private async Task<ChatResponse> HandleModifyIntent(ChatRequest request, IntentResult intent, Core.Entities.Session session)
    {
        if (session.CurrentModelId == null)
        {
            return new ChatResponse
            {
                SessionId = request.SessionId,
                IntentType = intent.IntentType,
                AssistantMessage = "There's no active model to modify. Create one first!",
                Type = "text"
            };
        }

        var model = await _modelGenerator.GetModel(session.CurrentModelId.Value);
        if (model == null)
        {
            return new ChatResponse
            {
                SessionId = request.SessionId,
                IntentType = intent.IntentType,
                AssistantMessage = "The current model could not be found.",
                Type = "text"
            };
        }

        // Build modification request from intent
        var changeType = intent.ParsedParameters.TryGetValue("changeType", out var ct) && ct?.ToString() == "material"
            ? ChangeType.Material : ChangeType.Color;
        var targetComponent = intent.Entities.FirstOrDefault()?.Value ?? "all";

        var modRequest = new Liuvis.Core.ValueObjects.ModificationRequest
        {
            ModelId = model.ModelId,
            SessionId = request.SessionId,
            ChangeType = changeType,
            TargetComponent = targetComponent,
            Parameters = intent.ParsedParameters
        };

        var updatedModel = await _modificationEngine.ApplyModification(model, modRequest);

        await _sessionManager.UpdateModelRef(request.SessionId, updatedModel.ModelId);

        await _hubContext.Clients.Group(request.SessionId.ToString())
            .SendAsync("ModelUpdated", new { updatedModel.ModelId, updatedModel.Name });

        var assistantMsg = await _sessionManager.AddMessage(request.SessionId, MessageRole.Assistant,
            $"Modified **{targetComponent}**: applied {changeType} change.");

        return new ChatResponse
        {
            SessionId = request.SessionId,
            MessageId = assistantMsg.MessageId,
            AssistantMessage = $"Modified {targetComponent}",
            IntentType = intent.IntentType,
            ModelId = updatedModel.ModelId,
            ModelName = updatedModel.Name,
            Type = "model_updated"
        };
    }

    private async Task<ChatResponse> HandleQueryIntent(ChatRequest request, IntentResult intent)
    {
        var assistantMsg = await _sessionManager.AddMessage(request.SessionId, MessageRole.Assistant,
            "I can help you design 3D models. Try saying something like \"Create a blue metal cylinder\" or \"Make it red\"!");

        return new ChatResponse
        {
            SessionId = request.SessionId,
            MessageId = assistantMsg.MessageId,
            AssistantMessage = "Try: \"Create a blue metal cylinder\" or \"Make it red\"",
            IntentType = intent.IntentType,
            Type = "text"
        };
    }

    private async Task<(string? Thinking, string Response)> GetThinkingResponse(string message, string fallback)
    {
        try
        {
            var thinkSb = new System.Text.StringBuilder();
            var reply = await _llmClient.CompleteWithThinkingAsync(message,
                "You are Liuvis AI, a 3D design assistant. Help the user design 3D models.",
                onThinking: t => thinkSb.Append(t));
            return (thinkSb.ToString(), reply);
        }
        catch
        {
            return (null, fallback);
        }
    }
}
