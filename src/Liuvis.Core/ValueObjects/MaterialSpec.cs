using Liuvis.Core.Enums;

namespace Liuvis.Core.ValueObjects;

/// <summary>Defines a material with PBR or Standard properties.</summary>
public record MaterialSpec
{
    public string Color { get; init; } = "#ffffff";
    public MaterialType Type { get; init; } = MaterialType.PBR;
    public double Roughness { get; init; } = 0.5;
    public double Metalness { get; init; } = 0.1;
    public double Opacity { get; init; } = 1.0;
}
