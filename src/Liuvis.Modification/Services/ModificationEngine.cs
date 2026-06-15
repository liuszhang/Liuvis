using Liuvis.Core.Entities;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;
using Liuvis.Modification.Operations;
using Microsoft.Extensions.Logging;

namespace Liuvis.Modification.Services;

/// <summary>Applies modifications to existing 3D models by dispatching to specific operations.</summary>
public class ModificationEngine : IModificationEngine
{
    private readonly ILogger<ModificationEngine> _logger;

    public ModificationEngine(ILogger<ModificationEngine> logger)
    {
        _logger = logger;
    }

    public Task<Model3D> ApplyModification(Model3D model, ModificationRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Applying modification: Type={ChangeType}, Target={Target}",
            request.ChangeType, request.TargetComponent);

        var operation = ResolveOperation(request.ChangeType);
        var updated = operation.Execute(model, request);

        updated.IncrementVersion();

        _logger.LogInformation("Modification complete: Model={ModelId}, Version={Version}",
            updated.ModelId, updated.Version);

        return Task.FromResult(updated);
    }

    private static IModifyOperation ResolveOperation(ChangeType changeType) => changeType switch
    {
        ChangeType.Color => new ColorModifyOperation(),
        ChangeType.Material => new MaterialModifyOperation(),
        ChangeType.Size => new SizeModifyOperation(),
        _ => new ColorModifyOperation()
    };
}
