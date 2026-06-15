namespace Liuvis.Core.ValueObjects;

/// <summary>Defines the position, rotation, and scale of a 3D object.</summary>
public record Transform3D
{
    public Vector3 Position { get; init; } = Vector3.Zero;
    public Vector3 Rotation { get; init; } = Vector3.Zero;
    public Vector3 Scale { get; init; } = Vector3.One;
}
