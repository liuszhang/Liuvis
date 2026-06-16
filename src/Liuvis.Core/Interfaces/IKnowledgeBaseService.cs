using Liuvis.Core.Entities;
using Liuvis.Core.ValueObjects;

namespace Liuvis.Core.Interfaces;

/// <summary>Manages the 3D model knowledge base with vector search.</summary>
public interface IKnowledgeBaseService
{
    Task<List<ModelMatch>> SearchModels(string query, int topK = 5, CancellationToken cancellationToken = default);
    Task<Guid> SaveModel(Model3D model, CancellationToken cancellationToken = default);
    Task<Model3D?> GetModel(Guid modelId, CancellationToken cancellationToken = default);
    Task AddTags(Guid modelId, List<string> tags, CancellationToken cancellationToken = default);
}
