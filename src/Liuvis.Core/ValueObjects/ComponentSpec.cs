namespace Liuvis.Core.ValueObjects;

/// <summary>Specification for a single model component.</summary>
public record ComponentSpec
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string GeometryType { get; init; } = "box";
    public Dictionary<string, object> Parameters { get; init; } = new();
    public MaterialSpec Material { get; init; } = new();
    public List<ComponentSpec> Children { get; init; } = new();
}
