using System.Text.Json;
using Liuvis.Core.Interfaces;
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
    private readonly ILogger<LLMDesignService> _logger;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public LLMDesignService(ILlmClient llmClient, ISettingsService settingsService, ILogger<LLMDesignService> logger)
    {
        _llmClient = llmClient;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<SceneDescription> GenerateSceneFromText(string description, CancellationToken ct = default)
    {
        _logger.LogInformation("LLM generating scene from: {Description}", description[..Math.Min(description.Length, 100)]);

        var promptSettings = await _settingsService.GetPromptSettingsAsync(ct);
        var prompt = promptSettings.SceneGenerationPrompt.Replace("{{description}}", description);
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
