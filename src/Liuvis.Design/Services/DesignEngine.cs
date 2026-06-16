using System.Text.Json;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;
using Liuvis.Design.Strategies;
using Microsoft.Extensions.Logging;

namespace Liuvis.Design.Services;

/// <summary>AI design engine that orchestrates design strategy and specification generation.</summary>
public class DesignEngine : IDesignEngine
{
    private readonly ILlmClient _llmClient;
    private readonly ILogger<DesignEngine> _logger;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public DesignEngine(ILlmClient llmClient, ILogger<DesignEngine> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<DesignPlan> CreateDesignPlan(IntentResult intent, List<ModelMatch> matches, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating design plan: Intent={IntentType}, Matches={MatchCount}",
            intent.IntentType, matches.Count);

        var hasReusable = matches.Any(m => m.Similarity > 0.7);

        if (hasReusable)
        {
            var strategy = new ReuseDesignStrategy(_llmClient);
            return await strategy.CreatePlanAsync(intent, matches, cancellationToken);
        }
        else
        {
            var strategy = new NewDesignStrategy(_llmClient);
            return await strategy.CreatePlanAsync(intent, matches, cancellationToken);
        }
    }

    public async Task<DesignSpec> GenerateDesignSpec(DesignPlan plan, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating design spec: Strategy={Strategy}", plan.Strategy);

        // For MVP: generate components directly from plan
        var components = new List<ComponentSpec>();

        if (plan.NewComponents.Count > 0)
        {
            components.AddRange(plan.NewComponents);
        }
        else
        {
            // Default: generate a single component from the plan description
            components.Add(new ComponentSpec
            {
                Name = "main_body",
                Description = plan.Description,
                GeometryType = ResolveGeometryType(plan.Description),
                Material = new MaterialSpec
                {
                    Color = "#00d4ff",
                    Type = MaterialType.PBR,
                    Roughness = 0.3,
                    Metalness = 0.7
                }
            });
        }

        var spec = new DesignSpec
        {
            SessionId = Guid.Empty, // Will be set by caller
            Strategy = plan.Strategy,
            Components = components,
            Constraints = plan.ReuseMatches.SelectMany(m => m.MatchedComponents).Distinct().ToList()
        };

        return spec;
    }

    private static string ResolveGeometryType(string description)
    {
        var lower = description.ToLowerInvariant();
        if (lower.Contains("cylinder")) return "cylinder";
        if (lower.Contains("sphere") || lower.Contains("ball")) return "sphere";
        if (lower.Contains("box") || lower.Contains("cube") || lower.Contains("square")) return "box";
        if (lower.Contains("gear")) return "gear";
        return "box";
    }
}
