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
    private readonly ISettingsService _settingsService;
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
        ISettingsService settingsService,
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
        _settingsService = settingsService;
        _hub = hub;
        _logger = logger;
    }

    public async Task<ChatResponse> ProcessMessageAsync(
        Guid sessionId,
        string message,
        ModelFormat format = ModelFormat.GLB,
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
            IntentType.Create => await HandleCreateAsync(sessionId, message, intent, format, onProgress, cancellationToken),
            IntentType.Modify => await HandleModifyAsync(sessionId, intent, session, format, onProgress, cancellationToken),
            IntentType.Query => await HandleQueryAsync(sessionId, message, onProgress, onResponseChunk, cancellationToken),
            _ => await HandleUnknownAsync(sessionId, message, onProgress, onResponseChunk, cancellationToken)
        };
    }

    private async Task<ChatResponse> HandleCreateAsync(
        Guid sessionId, string message, IntentResult intent, ModelFormat format,
        Action<string>? onProgress, CancellationToken ct)
    {
        await NotifyProgress(sessionId, "Searching knowledge base for reusable components...");
        var matches = await _kbService.SearchModels(message, topK: 5, ct);

        await NotifyProgress(sessionId, $"Found {matches.Count} candidate(s). Generating design plan...");
        var plan = await _designEngine.CreateDesignPlan(intent, matches, ct);

        await NotifyProgress(sessionId, "Building design specification...");
        var spec = await _designEngine.GenerateDesignSpec(plan, ct);
        spec = spec with { SessionId = sessionId, Intent = intent, Format = format };

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
        Guid sessionId, IntentResult intent, Core.Entities.Session session, ModelFormat format,
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

        await NotifyProgress(sessionId, "Analyzing modification request...");

        // Check for component-index-based lightweight modifications
        var isComponentIndexModify = TryResolveComponentIndexModify(intent.ParsedParameters, out var compAction);
        if (isComponentIndexModify && compAction != null)
        {
            var compMsg = FormatComponentModifyMessage(compAction);
            var compAssistantMsg = await _sessionManager.AddMessage(sessionId, MessageRole.Assistant, compMsg, ct);

            return new ChatResponse
            {
                SessionId = sessionId,
                MessageId = compAssistantMsg.MessageId,
                AssistantMessage = compMsg,
                IntentType = IntentType.Modify,
                Thinking = intent.Thinking,
                Type = "component_modify",
                Metadata = new Dictionary<string, object>
                {
                    ["componentAction"] = System.Text.Json.JsonSerializer.Serialize(compAction)
                }
            };
        }

        // Fall through to full modification engine for server-side modifications
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
                Format = format,
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
            var prompts = await _settingsService.GetPromptSettingsAsync(ct);
            var thinkSb = new System.Text.StringBuilder();
            var reply = await _llmClient.CompleteWithThinkingAsync(
                message,
                prompts.QueryPrompt,
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
            var prompts = await _settingsService.GetPromptSettingsAsync(ct);
            var thinkSb = new System.Text.StringBuilder();
            var reply = await _llmClient.CompleteWithThinkingAsync(
                message,
                prompts.UnknownPrompt,
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

    /// <summary>
    /// Detects if the NLU-parsed modification is a component-index-based lightweight operation
    /// that can be handled client-side (color, visibility, scale).
    /// Returns a ComponentModifyAction if applicable, null otherwise.
    /// </summary>
    private static bool TryResolveComponentIndexModify(
        Dictionary<string, object> parameters,
        out Liuvis.Core.DTOs.Responses.ComponentModifyAction? action)
    {
        action = null;

        // Must have a changeType
        if (!parameters.TryGetValue("changeType", out var ctVal) || ctVal is not string changeType)
            return false;

        // Check for visibility changes
        if (string.Equals(changeType, "visibility", StringComparison.OrdinalIgnoreCase))
        {
            // "显示所有组件" / "显示全部"
            if (parameters.TryGetValue("showAll", out var saVal) && IsTruthy(saVal))
            {
                action = new Liuvis.Core.DTOs.Responses.ComponentModifyAction { Action = "showAll" };
                return true;
            }

            // "隐藏组件3" / "显示组件1"
            var hasIndex = TryGetTargetIndex(parameters, out var visIndex);
            var hasVisibility = TryGetBoolParam(parameters, "visibility", out var visible);

            if (hasIndex && hasVisibility)
            {
                action = new Liuvis.Core.DTOs.Responses.ComponentModifyAction
                {
                    Action = "visibility",
                    ComponentIndex = visIndex,
                    Visible = visible
                };
                return true;
            }

            return false;
        }

        // Check for color changes by component index
        if (string.Equals(changeType, "color", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetTargetIndex(parameters, out var colorIndex) &&
                parameters.TryGetValue("color", out var colorVal) &&
                TryGetStringValue(colorVal, out var color))
            {
                action = new Liuvis.Core.DTOs.Responses.ComponentModifyAction
                {
                    Action = "color",
                    ComponentIndex = colorIndex,
                    Color = color
                };
                return true;
            }
            return false;
        }

        // Check for size/scale changes by component index
        if (string.Equals(changeType, "size", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetTargetIndex(parameters, out var sizeIndex))
            {
                var scale = 1.0;
                if (parameters.TryGetValue("scaleFactor", out var sfVal))
                    scale = TryGetDoubleValue(sfVal);
                else if (parameters.TryGetValue("scale", out var sVal))
                    scale = TryGetDoubleValue(sVal);

                if (Math.Abs(scale - 1.0) > 0.001)
                {
                    action = new Liuvis.Core.DTOs.Responses.ComponentModifyAction
                    {
                        Action = "scale",
                        ComponentIndex = sizeIndex,
                        Scale = scale
                    };
                    return true;
                }
            }
            return false;
        }

        return false;
    }

    /// <summary>
    /// Extracts target index from parameters. Looks for targetIndex first, then tries targetComponent as numeric string.
    /// Handles JsonElement values from System.Text.Json deserialization.
    /// </summary>
    private static bool TryGetTargetIndex(Dictionary<string, object> parameters, out int index)
    {
        // targetIndex is the primary field
        if (parameters.TryGetValue("targetIndex", out var tiVal))
        {
            if (tiVal is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                index = je.GetInt32();
                return index > 0;
            }
            index = Convert.ToInt32(tiVal);
            return index > 0;
        }

        // Fallback: targetComponent as numeric string
        if (parameters.TryGetValue("targetComponent", out var tcVal))
        {
            if (tcVal is System.Text.Json.JsonElement tje && tje.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = tje.GetString();
                if (int.TryParse(s, out index) && index > 0)
                    return true;
            }
            else if (tcVal is string tcStr && int.TryParse(tcStr, out index) && index > 0)
            {
                return true;
            }
        }

        index = 0;
        return false;
    }

    private static bool TryGetBoolParam(Dictionary<string, object> parameters, string key, out bool value)
    {
        if (parameters.TryGetValue(key, out var val))
        {
            if (val is bool b)
            {
                value = b;
                return true;
            }
            if (val is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.True)
                {
                    value = true;
                    return true;
                }
                if (je.ValueKind == System.Text.Json.JsonValueKind.False)
                {
                    value = false;
                    return true;
                }
                if (je.ValueKind == System.Text.Json.JsonValueKind.String &&
                    bool.TryParse(je.GetString(), out var sb))
                {
                    value = sb;
                    return true;
                }
            }
            if (val is string s && bool.TryParse(s, out var strb))
            {
                value = strb;
                return true;
            }
        }
        value = false;
        return false;
    }

    private static bool TryGetStringValue(object val, out string result)
    {
        if (val is string s)
        {
            result = s;
            return true;
        }
        if (val is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            result = je.GetString() ?? string.Empty;
            return true;
        }
        result = string.Empty;
        return false;
    }

    private static bool IsTruthy(object val)
    {
        if (val is true) return true;
        if (val is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.True) return true;
            if (je.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = je.GetString();
                return s != null && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");
            }
            if (je.ValueKind == System.Text.Json.JsonValueKind.Number)
                return je.GetDouble() != 0;
        }
        if (val is string str && bool.TryParse(str, out var b))
            return b;
        return false;
    }

    private static double TryGetDoubleValue(object val)
    {
        if (val is double d) return d;
        if (val is int i) return i;
        if (val is float f) return f;
        if (val is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
            return je.GetDouble();
        try { return Convert.ToDouble(val); }
        catch { return 1.0; }
    }

    private static string FormatComponentModifyMessage(Liuvis.Core.DTOs.Responses.ComponentModifyAction action)
    {
        return action.Action switch
        {
            "color" => $"Changed component {action.ComponentIndex} color to {action.Color}.",
            "visibility" when action.Visible == true =>
                $"Showing component {action.ComponentIndex}.",
            "visibility" when action.Visible == false =>
                $"Hiding component {action.ComponentIndex}.",
            "scale" => $"Scaled component {action.ComponentIndex} by {action.Scale}x.",
            "showAll" => "Showing all components.",
            _ => $"Applied {action.Action} to component {action.ComponentIndex}."
        };
    }
}
