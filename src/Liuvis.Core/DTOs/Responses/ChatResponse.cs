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
    public string Type { get; init; } = "text"; // "text" | "model_ready" | "model_updated" | "component_modify"
    public string? Thinking { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Describes a lightweight component modification action to be executed client-side.
/// Used when the response Type is "component_modify".
/// </summary>
public record ComponentModifyAction
{
    /// <summary>One of: "color", "visibility", "scale", "showAll".</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>1-based component index (for single-component actions).</summary>
    public int? ComponentIndex { get; init; }

    /// <summary>Hex color string for color actions.</summary>
    public string? Color { get; init; }

    /// <summary>Scale factor for scale actions.</summary>
    public double? Scale { get; init; }

    /// <summary>Visibility state for visibility actions.</summary>
    public bool? Visible { get; init; }
}
