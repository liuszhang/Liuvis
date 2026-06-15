using Liuvis.Core.Enums;

namespace Liuvis.Core.DTOs.Responses;

/// <summary>Response after a chat message is processed.</summary>
public record ChatResponse
{
    public Guid SessionId { get; init; }
    public Guid MessageId { get; init; }
    public string AssistantMessage { get; init; } = string.Empty;
    public IntentType IntentType { get; init; }
    public Guid? ModelId { get; init; }
    public string? ModelName { get; init; }
    public string Type { get; init; } = "text"; // "text" | "model_ready" | "model_updated"
    public string? Thinking { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
