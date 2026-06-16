using Liuvis.Core.Entities;
using Liuvis.Core.Enums;

namespace Liuvis.Core.Interfaces;

/// <summary>Manages design sessions and their conversation context.</summary>
public interface ISessionManager
{
    Task<Session> CreateSession(string userId, CancellationToken cancellationToken = default);
    Task<Session?> GetSession(Guid sessionId, CancellationToken cancellationToken = default);
    Task<SessionMessage> AddMessage(Guid sessionId, MessageRole role, string content, CancellationToken cancellationToken = default);
    Task UpdateModelRef(Guid sessionId, Guid modelId, CancellationToken cancellationToken = default);
    Task<List<SessionMessage>> GetRecentMessages(Guid sessionId, int count = 20, CancellationToken cancellationToken = default);
    Task ArchiveSession(Guid sessionId, CancellationToken cancellationToken = default);
}
