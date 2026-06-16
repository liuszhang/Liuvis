namespace Liuvis.Core.Interfaces;

/// <summary>Result from vector similarity search.</summary>
public record VectorSearchResult(Guid Id, double Similarity, Dictionary<string, string> Metadata);

/// <summary>Vector similarity search service using pgvector.</summary>
public interface IVectorSearchService
{
    Task<List<VectorSearchResult>> Search(float[] embedding, int topK = 5, CancellationToken cancellationToken = default);
    Task Upsert(Guid id, float[] embedding, Dictionary<string, string> metadata, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
}
