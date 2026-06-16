namespace Liuvis.Core.Events;

public record ModelGeneratedEvent(Guid ModelId, Guid SessionId);
