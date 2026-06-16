using Liuvis.Core.ValueObjects;

namespace Liuvis.Core.Entities;

/// <summary>A snapshot of a design at a point in time.</summary>
public class DesignSnapshot
{
    public Guid SnapshotId { get; private set; } = Guid.NewGuid();
    public Guid SessionId { get; private set; }
    public Guid ModelId { get; private set; }
    public DesignSpec Spec { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public string Description { get; private set; } = string.Empty;

    private DesignSnapshot() { Spec = null!; }

    public DesignSnapshot(Guid sessionId, Guid modelId, DesignSpec spec, string description = "")
    {
        SessionId = sessionId;
        ModelId = modelId;
        Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        Description = description ?? string.Empty;
    }
}
