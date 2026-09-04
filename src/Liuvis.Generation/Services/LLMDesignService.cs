using System.Text.Json;
using Liuvis.Core.Interfaces;
using Liuvis.Core.Ontology;
using Liuvis.Modules.Settings;
using Microsoft.Extensions.Logging;

namespace Liuvis.Generation.Services;

/// <summary>
/// Uses the LLM to generate structured 3D scene parameters from natural language descriptions.
/// Returns a list of objects with geometry types, sizes, positions, colors, and materials.
/// </summary>
public class LLMDesignService
{
    private readonly ILlmClient _llmClient;
    private readonly ISettingsService _settingsService;
    private readonly IOntologyContextService _ontology;
    private readonly ILogger<LLMDesignService> _logger;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public LLMDesignService(
        ILlmClient llmClient,
        ISettingsService settingsService,
        IOntologyContextService ontology,
        ILogger<LLMDesignService> logger)
    {
        _llmClient = llmClient;
        _settingsService = settingsService;
        _ontology = ontology;
        _logger = logger;
    }

    public async Task<SceneDescription> GenerateSceneFromText(string description, CancellationToken ct = default)
    {
        _logger.LogInformation("LLM generating scene from: {Description}", description[..Math.Min(description.Length, 100)]);

        var promptSettings = await _settingsService.GetPromptSettingsAsync(ct);
        var prompt = await EnrichWithOntologyAsync(promptSettings.SceneGenerationPrompt, ct);
        prompt = prompt.Replace("{{description}}", description);
        var response = await _llmClient.CompleteAsync(prompt, null, ct);
        var json = ExtractJson(response);

        try
        {
            var scene = JsonSerializer.Deserialize<SceneDescription>(json, _jsonOpts);
            if (scene is { Objects.Count: > 0 })
            {
                _logger.LogInformation("LLM generated scene with {Count} objects", scene.Objects.Count);
                return scene;
            }
            _logger.LogWarning("LLM returned empty scene. Raw response: {Raw}", response[..Math.Min(response.Length, 200)]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM scene output. Raw response: {Raw}, Extracted JSON: {Json}",
                response[..Math.Min(response.Length, 300)], json[..Math.Min(json.Length, 300)]);
        }

        // Fallback: create a single default object
        return new SceneDescription
        {
            Objects = new List<SceneObject>
            {
                new()
                {
                    Type = "box",
                    Size = new[] { 1.0, 1.0, 1.0 },
                    Position = new[] { 0.0, 0.0, 0.0 },
                    Color = "#00d4ff"
                }
            }
        };
    }

    /// <summary>
    /// 在生成 prompt 中填充本体上下文（{{ontologyContext}}）与 M3 设计规则（{{designRules}}）。
    /// fail-soft：CJOntology 不可用/异常时静默降级——占位符替换为空串，不抛异常、不中断原生成。
    /// </summary>
    private async Task<string> EnrichWithOntologyAsync(string prompt, CancellationToken ct)
    {
        string ontologyContext = string.Empty;
        string designRules = string.Empty;

        try
        {
            if (await _ontology.IsAvailableAsync(ct))
            {
                ontologyContext = await _ontology.GetOntologySummaryAsync(ct) ?? string.Empty;
                var rules = await _ontology.GetRulesAsync(cancellationToken: ct);
                designRules = FormatRules(rules);
                _logger.LogInformation(
                    "Ontology context injected into scene prompt (ontologyLength={OntologyLength}, rules={RuleCount})",
                    ontologyContext.Length, rules?.Count ?? 0);
            }
            else
            {
                _logger.LogDebug("Ontology service unavailable; scene prompt placeholders left empty (fail-soft)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich scene prompt with ontology context, placeholders left empty (fail-soft)");
        }

        // 无论是否降级，占位符都必须被消费，避免字面 {{...}} 作为噪声传给 LLM
        prompt = prompt.Replace("{{ontologyContext}}", ontologyContext);
        prompt = prompt.Replace("{{designRules}}", designRules);
        return prompt;
    }

    private static string FormatRules(IReadOnlyList<OntologyRuleInfo>? rules)
    {
        if (rules == null || rules.Count == 0)
            return string.Empty;

        return string.Join("\n", rules.Select(r =>
            $"- [{r.Kind}] {r.Name} ({r.Severity}) → {r.TargetType}.{r.TargetCode}"));
    }

    private static string ExtractJson(string raw)
    {
        raw = raw.Trim();

        // 1) Strip markdown code fences
        if (raw.StartsWith("```json")) raw = raw[7..];
        else if (raw.StartsWith("```")) raw = raw[3..];
        if (raw.EndsWith("```")) raw = raw[..^3];

        // 2) Extract the outermost JSON object from mixed text
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return raw[start..(end + 1)].Trim();
        }

        return raw.Trim();
    }
}

public class SceneDescription
{
    public List<SceneObject> Objects { get; init; } = new();
}

public class SceneObject
{
    public string Type { get; init; } = "box";
    public double[] Size { get; init; } = { 1.0, 1.0, 1.0 };
    public double[] Position { get; init; } = { 0.0, 0.0, 0.0 };
    public double[] Rotation { get; init; } = { 0.0, 0.0, 0.0 };
    public string Color { get; init; } = "#00d4ff";
    public MaterialProps? Material { get; init; }
}

public class MaterialProps
{
    public double Metalness { get; init; } = 0.5;
    public double Roughness { get; init; } = 0.3;
}
