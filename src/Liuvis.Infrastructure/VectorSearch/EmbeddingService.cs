using Liuvis.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Liuvis.Infrastructure.VectorSearch;

/// <summary>Service for generating embeddings via ILlmClient.</summary>
public class EmbeddingService
{
    private readonly ILlmClient _llmClient;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(ILlmClient llmClient, ILogger<EmbeddingService> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    /// <summary>Generate embedding for a text query.</summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        _logger.LogDebug("Generating embedding for text of length {Length}", text.Length);
        var embedding = await _llmClient.GetEmbeddingAsync(text, ct);
        _logger.LogDebug("Generated embedding with {Dim} dimensions", embedding.Length);
        return embedding;
    }

    /// <summary>Compute cosine similarity between two embeddings.</summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Embeddings must have the same dimensionality");

        double dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
