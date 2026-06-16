using Liuvis.Core.ValueObjects;

namespace Liuvis.Core.Interfaces;

/// <summary>AI design engine that transforms intent into design plans and specs.</summary>
public interface IDesignEngine
{
    Task<DesignPlan> CreateDesignPlan(IntentResult intent, List<ModelMatch> matches, CancellationToken cancellationToken = default);
    Task<DesignSpec> GenerateDesignSpec(DesignPlan plan, CancellationToken cancellationToken = default);
}
