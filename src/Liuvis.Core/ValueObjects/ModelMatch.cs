namespace Liuvis.Core.ValueObjects;

/// <summary>Represents a match result from knowledge base search.</summary>
public record ModelMatch
{
    public Guid ModelId { get; init; }
    public string Name { get; init; } = string.Empty;
    public double Similarity { get; init; }
    public List<string> MatchedComponents { get; init; } = new();
}
