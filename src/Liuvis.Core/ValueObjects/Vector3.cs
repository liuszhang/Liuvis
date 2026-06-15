namespace Liuvis.Core.ValueObjects;

/// <summary>Represents a 3D vector with X, Y, Z components.</summary>
public record Vector3(double X = 0, double Y = 0, double Z = 0)
{
    public static Vector3 Zero => new(0, 0, 0);
    public static Vector3 One => new(1, 1, 1);
    public static Vector3 Up => new(0, 1, 0);

    public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
}
