using Liuvis.Core.Enums;

namespace Liuvis.Core.ValueObjects;

/// <summary>Request to modify an existing 3D model.</summary>
public record ModificationRequest
{
    public Guid ModelId { get; init; }
    public Guid SessionId { get; init; }
    public ChangeType ChangeType { get; init; }
    public string TargetComponent { get; init; } = string.Empty;
    public Dictionary<string, object> Parameters { get; init; } = new();
}
