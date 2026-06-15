using Liuvis.Core.Enums;

namespace Liuvis.Core.ValueObjects;

/// <summary>Result of NLU intent parsing.</summary>
public record IntentResult
{
    public IntentType IntentType { get; init; } = IntentType.Unknown;
    public double Confidence { get; init; }
    public List<EntityExtraction> Entities { get; init; } = new();
    public string OriginalText { get; init; } = string.Empty;
    public Dictionary<string, object> ParsedParameters { get; init; } = new();
    /// <summary>LLM reasoning tokens captured during intent classification.</summary>
    public string? Thinking { get; init; }
}
