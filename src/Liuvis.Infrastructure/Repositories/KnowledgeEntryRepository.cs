using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Liuvis.Core.Entities;
using Liuvis.Core.Interfaces;
using Liuvis.Infrastructure.Persistence;

namespace Liuvis.Infrastructure.Repositories;

public class KnowledgeEntryRepository
{
    private readonly LiuvisDbContext _db;
    private readonly ILogger<KnowledgeEntryRepository> _logger;

    public KnowledgeEntryRepository(LiuvisDbContext db, ILogger<KnowledgeEntryRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public virtual async Task<KnowledgeEntry?> GetByIdAsync(Guid entryId, CancellationToken ct = default)
        => await _db.KnowledgeEntries.FirstOrDefaultAsync(e => e.EntryId == entryId, ct);

    public virtual async Task<KnowledgeEntry> CreateAsync(KnowledgeEntry entry, CancellationToken ct = default)
    {
        _db.KnowledgeEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public virtual async Task<List<VectorSearchResult>> SearchByEmbeddingAsync(float[] embedding, int topK = 5, CancellationToken ct = default)
    {
        _logger.LogDebug("In-memory vector search: topK={TopK}, dimensions={Dim}", topK, embedding.Length);

        var allEntries = await _db.KnowledgeEntries.ToListAsync(ct);

        if (allEntries.Count == 0)
            return new List<VectorSearchResult>();

        var scored = allEntries
            .Where(e => e.Embedding.Length == embedding.Length)
            .Select(e => new
            {
                Entry = e,
                Similarity = CosineSimilarity(embedding, e.Embedding)
            })
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .ToList();

        return scored.Select(r => new VectorSearchResult(
            r.Entry.ModelId,
            r.Similarity,
            new Dictionary<string, string>
            {
                ["Category"] = r.Entry.Category,
                ["Description"] = r.Entry.Description
            }
        )).ToList();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Embedding dimensions must match");

        double dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0;

        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    public virtual async Task UpsertAsync(KnowledgeEntry entry, CancellationToken ct = default)
    {
        var existing = await _db.KnowledgeEntries
            .FirstOrDefaultAsync(e => e.ModelId == entry.ModelId, ct);

        if (existing != null)
        {
            // Avoid SetValues — it tries to set EntryId (PK) which EF Core forbids.
            // The new entry built by PgvectorService always has a fresh Guid EntryId.
            _db.Entry(existing).Property(e => e.Embedding).CurrentValue = entry.Embedding;
            _db.Entry(existing).Property(e => e.Category).CurrentValue = entry.Category;
            _db.Entry(existing).Property(e => e.Description).CurrentValue = entry.Description;
            _db.Entry(existing).Property(e => e.Tags).CurrentValue = entry.Tags;
        }
        else
        {
            _db.KnowledgeEntries.Add(entry);
        }
        await _db.SaveChangesAsync(ct);
    }

    public virtual async Task DeleteByModelIdAsync(Guid modelId, CancellationToken ct = default)
    {
        var entries = await _db.KnowledgeEntries
            .Where(e => e.ModelId == modelId)
            .ToListAsync(ct);
        _db.KnowledgeEntries.RemoveRange(entries);
        await _db.SaveChangesAsync(ct);
    }
}
