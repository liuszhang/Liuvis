using Liuvis.Core.Enums;

namespace Liuvis.Core.ValueObjects;

/// <summary>The design plan that determines strategy and component choices.</summary>
public record DesignPlan
{
    public Guid PlanId { get; init; } = Guid.NewGuid();
    public DesignStrategy Strategy { get; init; } = DesignStrategy.New;
    public List<ModelMatch> ReuseMatches { get; init; } = new();
    public List<ComponentSpec> NewComponents { get; init; } = new();
    public string Description { get; init; } = string.Empty;
}
