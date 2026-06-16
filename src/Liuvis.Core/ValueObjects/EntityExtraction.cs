namespace Liuvis.Core.ValueObjects;

/// <summary>Represents a single entity extracted from user input.</summary>
public record EntityExtraction
{
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public int Start { get; init; }
    public int End { get; init; }
}
