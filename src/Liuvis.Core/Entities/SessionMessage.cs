using Liuvis.Core.Enums;

namespace Liuvis.Core.Entities;

/// <summary>A message within a session conversation.</summary>
public class SessionMessage
{
    public Guid MessageId { get; private set; } = Guid.NewGuid();
    public Guid SessionId { get; private set; }
    public MessageRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public DateTime Timestamp { get; private set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; private set; } = new();

    private SessionMessage() { }

    public SessionMessage(Guid sessionId, MessageRole role, string content)
    {
        SessionId = sessionId;
        Role = role;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }
}
