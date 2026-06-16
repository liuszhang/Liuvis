using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;

namespace Liuvis.Design.Strategies;

/// <summary>Strategy that reuses components from existing models in the knowledge base.</summary>
public class ReuseDesignStrategy
{
    private readonly ILlmClient _llmClient;

    public ReuseDesignStrategy(ILlmClient llmClient) => _llmClient = llmClient;

    public async Task<DesignPlan> CreatePlanAsync(IntentResult intent, List<ModelMatch> matches, CancellationToken ct)
    {
        // Select components from matched models that exceed similarity threshold
        var reuseMatches = matches.Where(m => m.Similarity > 0.7).ToList();

        var plan = new DesignPlan
        {
            Strategy = Core.Enums.DesignStrategy.Reuse,
            ReuseMatches = reuseMatches,
            Description = $"Reuse plan based on {reuseMatches.Count} similar models."
        };

        return plan;
    }
}
