using Liuvis.Core.Entities;
using Liuvis.Core.ValueObjects;

namespace Liuvis.Core.Interfaces;

/// <summary>Applies modifications to existing 3D models.</summary>
public interface IModificationEngine
{
    Task<Model3D> ApplyModification(Model3D model, ModificationRequest request, CancellationToken cancellationToken = default);
}
