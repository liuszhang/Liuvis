using Liuvis.Core.Enums;

namespace Liuvis.Core.ValueObjects;

/// <summary>Complete design specification generated from user intent.</summary>
public record DesignSpec
{
    public Guid SpecId { get; init; } = Guid.NewGuid();
    public Guid SessionId { get; init; }
    public IntentResult Intent { get; init; } = new();
    public DesignStrategy Strategy { get; init; } = DesignStrategy.New;
    public ModelFormat Format { get; init; } = ModelFormat.GLB;
    public List<ComponentSpec> Components { get; init; } = new();
    public List<string> Constraints { get; init; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
