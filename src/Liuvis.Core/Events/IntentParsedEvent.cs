using Liuvis.Core.ValueObjects;

namespace Liuvis.Core.Events;

public record IntentParsedEvent(Guid SessionId, IntentResult Intent);
