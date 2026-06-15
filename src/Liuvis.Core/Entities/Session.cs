using Liuvis.Core.Enums;

namespace Liuvis.Core.Entities;

/// <summary>Represents a user design session.</summary>
public class Session
{
    public Guid SessionId { get; private set; } = Guid.NewGuid();
    public string UserId { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public SessionStatus Status { get; private set; } = SessionStatus.Active;
    public Guid? CurrentModelId { get; private set; }
    public List<SessionMessage> Messages { get; private set; } = new();

    private Session() { }

    public Session(string userId)
    {
        UserId = userId ?? throw new ArgumentNullException(nameof(userId));
    }

    public SessionMessage CreateMessage(MessageRole role, string content)
    {
        var message = new SessionMessage(SessionId, role, content);
        Messages.Add(message);
        UpdatedAt = DateTime.UtcNow;
        return message;
    }

    public void UpdateModelRef(Guid modelId)
    {
        CurrentModelId = modelId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }

    public void Archive()
    {
        Status = SessionStatus.Archived;
        UpdatedAt = DateTime.UtcNow;
    }
}
