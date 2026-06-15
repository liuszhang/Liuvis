using Liuvis.Core.Entities;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Liuvis.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;

namespace Liuvis.Session.Services;

/// <summary>Session manager with persistence and context management.</summary>
public class SessionManager : ISessionManager
{
    private readonly SessionRepository _repository;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(SessionRepository repository, ILogger<SessionManager> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Core.Entities.Session> CreateSession(string userId, CancellationToken cancellationToken = default)
    {
        var session = new Core.Entities.Session(userId);
        session.CreateMessage(MessageRole.System, "Welcome to Liuvis Studio. Describe what you'd like to design!");
        var created = await _repository.CreateAsync(session, cancellationToken);
        _logger.LogInformation("Session created: {SessionId}", created.SessionId);
        return created;
    }

    public async Task<Core.Entities.Session?> GetSession(Guid sessionId, CancellationToken cancellationToken = default)
        => await _repository.GetByIdAsync(sessionId, cancellationToken);

    public async Task<SessionMessage> AddMessage(Guid sessionId, MessageRole role, string content, CancellationToken cancellationToken = default)
    {
        var message = new SessionMessage(sessionId, role, content);
        await _repository.AddMessageAsync(message, cancellationToken);
        await _repository.TouchSessionAsync(sessionId, cancellationToken);
        return message;
    }

    public async Task UpdateModelRef(Guid sessionId, Guid modelId, CancellationToken cancellationToken = default)
    {
        await _repository.UpdateSessionModelRefAsync(sessionId, modelId, cancellationToken);
    }

    public async Task<List<SessionMessage>> GetRecentMessages(Guid sessionId, int count = 20, CancellationToken cancellationToken = default)
        => await _repository.GetRecentMessagesAsync(sessionId, count, cancellationToken);

    public async Task ArchiveSession(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await _repository.ArchiveSessionAsync(sessionId, cancellationToken);
    }
}
