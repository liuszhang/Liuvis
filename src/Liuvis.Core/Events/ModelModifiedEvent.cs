namespace Liuvis.Core.Events;

public record ModelModifiedEvent(Guid ModelId, Guid SessionId, string ChangeDescription);
