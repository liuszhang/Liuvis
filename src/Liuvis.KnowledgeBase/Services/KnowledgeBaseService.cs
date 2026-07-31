using Liuvis.Core.Entities;
using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;
using Liuvis.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Liuvis.KnowledgeBase.Services;

/// <summary>Manages the 3D model knowledge base with embedding-based search.</summary>
public class KnowledgeBaseService : IKnowledgeBaseService
{
    private readonly IVectorSearchService _vectorSearch;
    private readonly ILlmClient _llmClient;
    private readonly ModelRepository _modelRepository;
    private readonly ILogger<KnowledgeBaseService> _logger;

    public KnowledgeBaseService(
        IVectorSearchService vectorSearch,
        ILlmClient llmClient,
        ModelRepository modelRepository,
        ILogger<KnowledgeBaseService> logger)
    {
        _vectorSearch = vectorSearch;
        _llmClient = llmClient;
        _modelRepository = modelRepository;
        _logger = logger;
    }

    /// <summary>
    /// 根据 OntologyCode 生成确定性 Guid（UUID v3 语义：MD5 名称哈希）。
    /// 同一 OntologyCode 始终映射到同一 ModelId。
    /// </summary>
    private static Guid CreateDeterministicGuid(string name)
    {
        var nsBytes = Guid.Empty.ToByteArray();
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var buffer = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, buffer, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, buffer, nsBytes.Length, nameBytes.Length);

        var hash = MD5.HashData(buffer);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3 (MD5 namespace-based)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // IETF variant

        return new Guid(hash);
    }

    public async Task<List<ModelMatch>> SearchModels(string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching knowledge base: Query='{Query}', TopK={TopK}",
            query[..Math.Min(query.Length, 80)], topK);

        try
        {
            var embedding = await _llmClient.GetEmbeddingAsync(query, cancellationToken);
            var results = await _vectorSearch.Search(embedding, topK, cancellationToken);

            var matches = results.Select(r =>
            {
                var ontologyCode = r.Metadata.GetValueOrDefault("OntologyCode", null);
                var sourceTypeStr = r.Metadata.GetValueOrDefault("SourceType", null);

                return new ModelMatch
                {
                    EntryId = r.Id,
                    ModelId = ontologyCode != null
                        ? CreateDeterministicGuid(ontologyCode)
                        : r.Id,
                    Name = r.Metadata.GetValueOrDefault("Description", r.Metadata.GetValueOrDefault("Name", "Unknown")),
                    Similarity = r.Similarity,
                    MatchedComponents = r.Metadata.GetValueOrDefault("Category", string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .ToList(),
                    OntologyCode = ontologyCode,
                    SourceType = sourceTypeStr != null
                        ? (Core.Ontology.KnowledgeSourceType)Enum.Parse(
                            typeof(Core.Ontology.KnowledgeSourceType), sourceTypeStr)
                        : Core.Ontology.KnowledgeSourceType.ModelDescription,
                    Category = r.Metadata.GetValueOrDefault("Category", null),
                    Description = r.Metadata.GetValueOrDefault("Description", null),
                    Tags = r.Metadata.GetValueOrDefault("Tags", null)
                };
            }).ToList();

            _logger.LogInformation("KB search returned {Count} matches", matches.Count);
            return matches;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge base search failed, returning empty results");
            return new List<ModelMatch>();
        }
    }

    public async Task<Guid> SaveModel(Model3D model, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Saving model to KB: {ModelId} ({Name})", model.ModelId, model.Name);

        var description = $"{model.Name}: {model.Description}";
        var embedding = await _llmClient.GetEmbeddingAsync(description, cancellationToken);

        await _vectorSearch.Upsert(model.ModelId, embedding, new Dictionary<string, string>
        {
            ["Category"] = string.Join(",", model.Tags),
            ["Description"] = description,
            ["Tags"] = string.Join(",", model.Tags)
        }, cancellationToken);

        return model.ModelId;
    }

    public async Task<Model3D?> GetModel(Guid modelId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("KB GetModel: {ModelId}", modelId);
        return await _modelRepository.GetByIdAsync(modelId, cancellationToken);
    }

    public async Task AddTags(Guid modelId, List<string> tags, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding tags to model {ModelId}: {Tags}", modelId, string.Join(", ", tags));

        var model = await _modelRepository.GetByIdAsync(modelId, cancellationToken);
        if (model == null)
        {
            _logger.LogWarning("Model {ModelId} not found, cannot add tags", modelId);
            return;
        }

        foreach (var tag in tags)
            model.AddTag(tag);

        await _modelRepository.UpdateAsync(model, cancellationToken);

        var description = $"{model.Name}: {model.Description}";
        var embedding = await _llmClient.GetEmbeddingAsync(description, cancellationToken);

        await _vectorSearch.Upsert(modelId, embedding, new Dictionary<string, string>
        {
            ["Category"] = string.Join(",", model.Tags),
            ["Description"] = description,
            ["Tags"] = string.Join(",", model.Tags)
        }, cancellationToken);
    }
}
