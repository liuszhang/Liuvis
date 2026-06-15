using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;

namespace Liuvis.Design.Strategies;

/// <summary>Strategy for creating brand new 3D model designs from scratch.</summary>
public class NewDesignStrategy
{
    private readonly ILlmClient _llmClient;

    public NewDesignStrategy(ILlmClient llmClient) => _llmClient = llmClient;

    public async Task<DesignPlan> CreatePlanAsync(IntentResult intent, List<ModelMatch> matches, CancellationToken ct)
    {
        // Extract components from intent entities
        var components = intent.Entities
            .Where(e => e.Type == "object_type")
            .Select(e => new ComponentSpec
            {
                Name = e.Value,
                GeometryType = ResolveGeometry(e.Value),
                Material = new MaterialSpec
                {
                    Color = intent.Entities.FirstOrDefault(x => x.Type == "color")?.Value ?? "#00d4ff",
                    Type = Core.Enums.MaterialType.PBR
                }
            })
            .ToList();

        if (components.Count == 0)
        {
            components.Add(new ComponentSpec
            {
                Name = "object",
                GeometryType = "box",
                Material = new MaterialSpec { Color = "#00d4ff", Type = Core.Enums.MaterialType.PBR }
            });
        }

        return new DesignPlan
        {
            Strategy = Core.Enums.DesignStrategy.New,
            NewComponents = components,
            Description = intent.OriginalText
        };
    }

    private static string ResolveGeometry(string? type) => type?.ToLowerInvariant() switch
    {
        "cylinder" => "cylinder",
        "sphere" or "ball" => "sphere",
        "box" or "cube" => "box",
        "gear" => "gear",
        _ => "box"
    };
}
