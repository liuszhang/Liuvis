using Microsoft.EntityFrameworkCore;
using Liuvis.Core.Entities;
using Liuvis.Core.Enums;
using Liuvis.Infrastructure.Persistence;

namespace Liuvis.Infrastructure.Repositories;

/// <summary>Repository for Session entity operations.</summary>
public class SessionRepository
{
    private readonly LiuvisDbContext _db;

    public SessionRepository(LiuvisDbContext db) => _db = db;

    public async Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
        => await _db.Sessions
            .Include(s => s.Messages.OrderByDescending(m => m.Timestamp).Take(20))
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

    public async Task<Session> CreateAsync(Session session, CancellationToken ct = default)
    {
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    public async Task AddMessageAsync(SessionMessage message, CancellationToken ct = default)
    {
        _db.SessionMessages.Add(message);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<SessionMessage>> GetRecentMessagesAsync(Guid sessionId, int count = 20, CancellationToken ct = default)
        => await _db.SessionMessages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Timestamp)
            .Take(count)
            .OrderBy(m => m.Timestamp)
            .ToListAsync(ct);

    public async Task TouchSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await _db.Sessions
            .Where(s => s.SessionId == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);
    }

    public async Task UpdateSessionModelRefAsync(Guid sessionId, Guid modelId, CancellationToken ct = default)
    {
        await _db.Sessions
            .Where(s => s.SessionId == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.CurrentModelId, modelId)
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);
    }

    public async Task ArchiveSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await _db.Sessions
            .Where(s => s.SessionId == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, SessionStatus.Archived)
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);
    }

    public async Task UpdateSessionAsync(Session session, CancellationToken ct = default)
    {
        await _db.Sessions
            .Where(s => s.SessionId == session.SessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.UpdatedAt, session.UpdatedAt)
                .SetProperty(s => s.CurrentModelId, session.CurrentModelId)
                .SetProperty(s => s.Status, session.Status),
            cancellationToken: ct);
    }
}
