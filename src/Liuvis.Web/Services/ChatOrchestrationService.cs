using Liuvis.Core.DTOs.Responses;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;
using Liuvis.Generation.Services;
using Liuvis.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Liuvis.Web.Services;

/// <summary>
/// Orchestrates the Chat �?NLU �?Design �?Generation pipeline.
/// Broadcasts progress and model events via SignalR to all clients in the session.
/// </summary>
public class ChatOrchestrationService
{
    private readonly INluService _nluService;
    private readonly ISessionManager _sessionManager;
    private readonly IKnowledgeBaseService _kbService;
    private readonly IDesignEngine _designEngine;
    private readonly IModelGenerator _modelGenerator;
    private readonly IModificationEngine _modificationEngine;
    private readonly ILlmClient _llmClient;
    private readonly IHubContext<DesignHub> _hub;
    private readonly ILogger<ChatOrchestrationService> _logger;

    public ChatOrchestrationService(
        INluService nluService,
        ISessionManager sessionManager,
        IKnowledgeBaseService kbService,
        IDesignEngine designEngine,
        IModelGenerator modelGenerator,
        IModificationEngine modificationEngine,
        ILlmClient llmClient,
        IHubContext<DesignHub> hub,
        ILogger<ChatOrchestrationService> logger)
    {
        _nluService = nluService;
        _sessionManager = sessionManager;
        _kbService = kbService;
        _designEngine = designEngine;
        _modelGenerator = modelGenerator;
        _modificationEngine = modificationEngine;
        _llmClient = llmClient;
        _hub = hub;
        _logger = logger;
    }

    public async Task<ChatResponse> ProcessMessageAsync(
        Guid sessionId,
        string message,
        Action<string>? onProgress = null,
        Action<string>? onThinkingChunk = null,
        Action<string>? onResponseChunk = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Pipeline: Session={SessionId}, Message={Message}",
            sessionId, message[..Math.Min(message.Length, 80)]);

        var session = await _sessionManager.GetSession(sessionId);
        if (session == null)
            return new ChatResponse { SessionId = sessionId, AssistantMessage = "Session not found.", Type = "error" };

        await _sessionManager.AddMessage(sessionId, MessageRole.User, message);

        await NotifyProgress(sessionId, "Analyzing your request...");

        // Build model context for NLU if a model exists
        string? modelContext = null;
        if (session.CurrentModelId != null)
        {
            try
            {
                var currentModel = await _modelGenerator.GetModel(session.CurrentModelId.Value, cancellationToken);
                if (currentModel != null)
                {
                    modelContext = BuildModelContext(currentModel);
                    _logger.LogInformation("Pipeline: Built model context for NLU, Model={ModelId}, Components={Count}",
                        currentModel.ModelId, currentModel.Components.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pipeline: Failed to load model {ModelId} for NLU context, proceeding without context",
                    session.CurrentModelId);
            }
        }

        var intent = await _nluService.ParseIntent(message, modelContext, onThinkingChunk, cancellationToken);
        await NotifyProgress(sessionId, $"Intent: {FormatIntent(intent.IntentType)} (confidence: {intent.Confidence:P0})");

        if (!string.IsNullOrEmpty(intent.Thinking))
            await NotifyThinking(sessionId, intent.Thinking);

        return intent.IntentType switch
        {
            IntentType.Create => await HandleCreateAsync(sessionId, message, intent, onProgress, cancellationToken),
            IntentType.Modify => await HandleModifyAsync(sessionId, intent, session, onProgress, cancellationToken),
            IntentType.Query => await HandleQueryAsync(sessionId, message, onProgress, onResponseChunk, cancellationToken),
            _ => await HandleUnknownAsync(sessionId, message, onProgress, onResponseChunk, cancellationToken)
        };
    }

    private async Task<ChatResponse> HandleCreateAsync(
        Guid sessionId, string message, IntentResult intent,
        Action<string>? onProgress, CancellationToken ct)
    {
        await NotifyProgress(sessionId, "Searching knowledge base for reusable components...");
        var matches = await _kbService.SearchModels(message, topK: 5, ct);

        await NotifyProgress(sessionId, $"Found {matches.Count} candidate(s). Generating design plan...");
        var plan = await _designEngine.CreateDesignPlan(intent, matches, ct);

        await NotifyProgress(sessionId, "Building design specification...");
        var spec = await _designEngine.GenerateDesignSpec(plan, ct);
        spec = spec with { SessionId = sessionId, Intent = intent };

        await NotifyProgress(sessionId, "Generating 3D geometry with AI...");
        var model = await _modelGenerator.GenerateModel(spec, ct);

        await NotifyProgress(sessionId, "Indexing model in knowledge base...");
        try
        {
            await _kbService.SaveModel(model, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to index model in KB, continuing");
        }

        await _sessionManager.UpdateModelRef(sessionId, model.ModelId, ct);

        var assistantMsg = await _sessionManager.AddMessage(sessionId, MessageRole.Assistant,
            $"Created model: {model.Name} ({model.Components.Count} components)", ct);

        await NotifyModelReady(sessionId, model);

        return new ChatResponse
        {
            SessionId = sessionId,
            MessageId = assistantMsg.MessageId,
            AssistantMessage = $"Created 3D model: **{model.Name}** with {model.Components.Count} component(s).",
            IntentType = intent.IntentType,
            ModelId = model.ModelId,
            ModelName = model.Name,
            Thinking = intent.Thinking,
            Type = "model_ready",
            Metadata = model.Metadata.Count > 0
                ? model.Metadata.ToDictionary(kv => kv.Key, kv => (object)kv.Value)
                : null
        };
    }

    private async Task<ChatResponse> HandleModifyAsync(
        Guid sessionId, IntentResult intent, Core.Entities.Session session,
        Action<string>? onProgress, CancellationToken ct)
    {
        if (session.CurrentModelId == null)
        {
            return new ChatResponse
            {
                SessionId = sessionId,
                IntentType = IntentType.Modify,
                AssistantMessage = "No active model to modify. Create one first!",
                Type = "text"
            };
        }

        await NotifyProgress(sessionId, "Loading current model...");
        var model = await _modelGenerator.GetModel(session.CurrentModelId.Value, ct);
        if (model == null)
        {
            return new ChatResponse
            {
                SessionId = sessionId,
                IntentType = IntentType.Modify,
                AssistantMessage = "Current model not found.",
                Type = "text"
            };
        }

        await NotifyProgress(sessionId, "Applying modification...");
        var changeType = ResolveChangeType(intent.ParsedParameters);
        // Prefer Parameters over Entities: NLU may return color entities
        // which would be incorrectly picked as targetComponent.
        var targetComponent = "all";
        if (intent.ParsedParameters.TryGetValue("targetComponent", out var tcVal)
            && tcVal is string tcStr && !string.IsNullOrWhiteSpace(tcStr))
        {
            targetComponent = tcStr;
        }

        var modRequest = new ModificationRequest
        {
            ModelId = model.ModelId,
            SessionId = sessionId,
            ChangeType = changeType,
            TargetComponent = targetComponent,
            Parameters = intent.ParsedParameters
        };

        var updatedModel = await _modificationEngine.ApplyModification(model, modRequest, ct);

        await NotifyProgress(sessionId, "Updating scene description...");
        UpdateSceneFromComponents(updatedModel);

        await NotifyProgress(sessionId, "Regenerating 3D model...");
        var newModel = await _modelGenerator.GenerateModel(
            new DesignSpec
            {
                SessionId = sessionId,
                Intent = intent,
                Components = updatedModel.Components.Select(c => new ComponentSpec
                {
                    Name = c.Name,
                    Description = c.Name,
                    GeometryType = c.GeometryType,
                    Material = c.Material
                }).ToList()
            }, ct);

        await _sessionManager.UpdateModelRef(sessionId, newModel.ModelId, ct);

        try { await _kbService.SaveModel(newModel, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to re-index modified model in KB"); }

        var description = FormatModificationDescription(changeType, targetComponent, intent.ParsedParameters);
        var assistantMsg = await _sessionManager.AddMessage(sessionId, MessageRole.Assistant, description, ct);

        await _hub.Clients.Group(sessionId.ToString())
            .SendAsync("ModelUpdated", new
            {
                newModel.ModelId,
                newModel.Name,
                ChangeType = changeType.ToString(),
                TargetComponent = targetComponent
            });

        return new ChatResponse
        {
            SessionId = sessionId,
            MessageId = assistantMsg.MessageId,
            AssistantMessage = description,
            IntentType = intent.IntentType,
            ModelId = newModel.ModelId,
            ModelName = newModel.Name,
            Type = "model_updated",
            Metadata = newModel.Metadata.Count > 0
                ? newModel.Metadata.ToDictionary(kv => kv.Key, kv => (object)kv.Value)
                : null
        };
    }

    private async Task<ChatResponse> HandleQueryAsync(
        Guid sessionId, string message,
        Action<string>? onProgress, Action<string>? onResponseChunk, CancellationToken ct)
    {
        await NotifyProgress(sessionId, "Thinking...");
        try
        {
            var thinkSb = new System.Text.StringBuilder();
            var reply = await _llmClient.CompleteWithThinkingAsync(
                message,
                "You are Liuvis AI, a 3D design assistant. Help the user design 3D models. " +
                "You can create, modify, and query 3D models. Be concise and helpful.",
                onThinking: t => thinkSb.Append(t),
                onToken: onResponseChunk,
                cancellationToken: ct);

            var assistantMsg = await _sessionManager.AddMessage(sessionId, MessageRole.Assistant, reply, ct);

            return new ChatResponse
            {
                SessionId = sessionId,
                MessageId = assistantMsg.MessageId,
                AssistantMessage = reply,
                IntentType = IntentType.Query,
                Thinking = thinkSb.Length > 0 ? thinkSb.ToString() : null,
                Type = "text"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query LLM failed, using fallback");
            var fallback = "I can help you design 3D models. Try: \"Create a blue metal cube\" or \"Make it red\".";
            var assistantMsg = await _sessionManager.AddMessage(sessionId, MessageRole.Assistant, fallback, ct);

            return new ChatResponse
            {
                SessionId = sessionId,
                MessageId = assistantMsg.MessageId,
                AssistantMessage = fallback,
                IntentType = IntentType.Query,
                Type = "text"
            };
        }
    }

    private async Task<ChatResponse> HandleUnknownAsync(
        Guid sessionId, string message,
        Action<string>? onProgress, Action<string>? onResponseChunk, CancellationToken ct)
    {
        await NotifyProgress(sessionId, "I'm not sure what you mean...");
        try
        {
            var thinkSb = new System.Text.StringBuilder();
            var reply = await _llmClient.CompleteWithThinkingAsync(
                message,
                "You are Liuvis AI, a 3D design assistant. The user's intent was unclear. " +
                "Ask them to clarify whether they want to create, modify, or query a 3D model.",
                onThinking: t => thinkSb.Append(t),
                onToken: onResponseChunk,
                cancellationToken: ct);

            var assistantMsg = await _sessionManager.AddMessage(sessionId, MessageRole.Assistant, reply, ct);

            return new ChatResponse
            {
                SessionId = sessionId,
                MessageId = assistantMsg.MessageId,
                AssistantMessage = reply,
                IntentType = IntentType.Unknown,
                Thinking = thinkSb.Length > 0 ? thinkSb.ToString() : null,
                Type = "text"
            };
        }
        catch
        {
            var fallback = "I'm not sure what you'd like to do. Try: \"Create a blue cube\" or \"Make the sphere red\".";
            var assistantMsg = await _sessionManager.AddMessage(sessionId, MessageRole.Assistant, fallback, ct);

            return new ChatResponse
            {
                SessionId = sessionId,
                MessageId = assistantMsg.MessageId,
                AssistantMessage = fallback,
                IntentType = IntentType.Unknown,
                Type = "text"
            };
        }
    }

    private static ChangeType ResolveChangeType(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("changeType", out var ctVal))
            return ChangeType.Color;

        return ctVal?.ToString()?.ToLowerInvariant() switch
        {
            "material" => ChangeType.Material,
            "size" or "scale" => ChangeType.Size,
            "transform" or "position" or "rotate" => ChangeType.Transform,
            _ => ChangeType.Color
        };
    }

    private static void UpdateSceneFromComponents(Core.Entities.Model3D model)
    {
        if (!model.Metadata.TryGetValue("_scene", out var sceneJson) || string.IsNullOrEmpty(sceneJson))
            return;

        try
        {
            var scene = System.Text.Json.JsonSerializer.Deserialize<SceneDescription>(sceneJson);
            if (scene == null) return;

            foreach (var component in model.Components)
            {
                var sceneObj = scene.Objects.FirstOrDefault(o =>
                    string.Equals(o.Type, component.GeometryType, StringComparison.OrdinalIgnoreCase));
                if (sceneObj == null) continue;

                if (component.Material?.Color != null)
                {
                    var idx = scene.Objects.IndexOf(sceneObj);
                    scene.Objects[idx] = new SceneObject
                    {
                        Type = sceneObj.Type,
                        Size = sceneObj.Size,
                        Position = sceneObj.Position,
                        Rotation = sceneObj.Rotation,
                        Color = component.Material.Color,
                        Material = sceneObj.Material
                    };
                }
            }

            var updatedJson = System.Text.Json.JsonSerializer.Serialize(scene,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            model.Metadata["_scene"] = updatedJson;
        }
        catch (Exception)
        {
            // Scene update is best-effort
        }
    }

    private static string FormatModificationDescription(ChangeType changeType, string target, Dictionary<string, object> parameters)
    {
        var targetDesc = target == "all" ? "the model" : $"**{target}**";

        return changeType switch
        {
            ChangeType.Color when parameters.TryGetValue("color", out var color) =>
                $"Changed {targetDesc} color to {color}.",
            ChangeType.Material =>
                $"Updated {targetDesc} material properties.",
            ChangeType.Size when parameters.TryGetValue("scale", out var scale) =>
                $"Scaled {targetDesc} by {scale}x.",
            ChangeType.Size =>
                $"Resized {targetDesc}.",
            ChangeType.Transform =>
                $"Transformed {targetDesc}.",
            _ => $"Modified {targetDesc}."
        };
    }

    private async Task NotifyProgress(Guid sessionId, string message)
    {
        _logger.LogDebug("Progress [{SessionId}]: {Message}", sessionId, message);
        await _hub.Clients.Group(sessionId.ToString()).SendAsync("Progress", new
        {
            Message = message,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task NotifyThinking(Guid sessionId, string thinking)
    {
        await _hub.Clients.Group(sessionId.ToString()).SendAsync("Thinking", new
        {
            Content = thinking,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task NotifyModelReady(Guid sessionId, Core.Entities.Model3D model)
    {
        await _hub.Clients.Group(sessionId.ToString()).SendAsync("ModelReady", new
        {
            model.ModelId,
            model.Name,
            model.Description,
            ComponentCount = model.Components.Count,
            model.FilePath,
            model.Tags,
            Metadata = model.Metadata.Count > 0 ? model.Metadata : null,
            Timestamp = DateTime.UtcNow
        });
    }

    private static string FormatIntent(IntentType type) => type switch
    {
        IntentType.Create => "Create",
        IntentType.Modify => "Modify",
        IntentType.Query => "Query",
        _ => "Unknown"
    };

    private static string BuildModelContext(Core.Entities.Model3D model)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Active model: {model.Name} (version {model.Version})");
        if (model.Components.Count > 0)
        {
            sb.AppendLine("Components:");
            foreach (var c in model.Components)
            {
                var mat = c.Material;
                var color = mat?.Color ?? "default";
                var matType = mat?.Type.ToString() ?? "standard";
                sb.AppendLine($"- {c.Name}: geometry={c.GeometryType}, color={color}, material={matType}");
            }
        }
        else
        {
            sb.AppendLine("No components.");
        }
        return sb.ToString().TrimEnd();
    }
}
