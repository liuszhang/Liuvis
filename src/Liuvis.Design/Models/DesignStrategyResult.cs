using Liuvis.Core.Enums;

namespace Liuvis.Design.Models;

/// <summary>Result of a design strategy execution.</summary>
public record DesignStrategyResult
{
    public DesignStrategy Strategy { get; init; }
    public bool Success { get; init; }
    public string Description { get; init; } = string.Empty;
    public int ComponentCount { get; init; }
}
