using Liuvis.Core.Entities;
using Liuvis.Core.ValueObjects;

namespace Liuvis.Core.Interfaces;

/// <summary>Generates 3D models from design specifications.</summary>
public interface IModelGenerator
{
    Task<Model3D> GenerateModel(DesignSpec spec, CancellationToken cancellationToken = default);
    Task<Model3D?> GetModel(Guid modelId, CancellationToken cancellationToken = default);
    Task<Stream> GetModelFile(Guid modelId, CancellationToken cancellationToken = default);
}
