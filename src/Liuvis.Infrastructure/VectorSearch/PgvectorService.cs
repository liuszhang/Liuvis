using Liuvis.Core.Interfaces;
using Liuvis.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;

namespace Liuvis.Infrastructure.VectorSearch;

/// <summary>pgvector-based vector search service implementation.</summary>
public class PgvectorService : IVectorSearchService
{
    private readonly KnowledgeEntryRepository _repository;
    private readonly ILogger<PgvectorService> _logger;

    public PgvectorService(KnowledgeEntryRepository repository, ILogger<PgvectorService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<VectorSearchResult>> Search(float[] embedding, int topK = 5, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Vector search: topK={TopK}, dimensions={Dim}", topK, embedding.Length);
        var results = await _repository.SearchByEmbeddingAsync(embedding, topK, cancellationToken);
        _logger.LogDebug("Vector search returned {Count} results", results.Count);
        return results;
    }

    public async Task Upsert(Guid id, float[] embedding, Dictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Vector upsert: id={Id}", id);

        var entry = new Liuvis.Core.Entities.KnowledgeEntry(
            id, embedding,
            metadata.GetValueOrDefault("Category", "general"),
            metadata.GetValueOrDefault("Description", string.Empty));

        foreach (var tag in metadata.GetValueOrDefault("Tags", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            entry.AddTag(tag.Trim());

        await _repository.UpsertAsync(entry, cancellationToken);
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Vector delete: id={Id}", id);
        await _repository.DeleteByModelIdAsync(id, cancellationToken);
    }
}
