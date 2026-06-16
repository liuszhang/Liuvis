using System.Text.Json;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;
using Liuvis.NLU.Models;
using Microsoft.Extensions.Logging;

namespace Liuvis.NLU.Services;

/// <summary>NLU service using LLM for intent classification and entity extraction.</summary>
public class NluService : INluService
{
    private readonly ILlmClient _llmClient;
    private readonly ILogger<NluService> _logger;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, IntentType> _intentMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["create"] = IntentType.Create,
        ["modify"] = IntentType.Modify,
        ["query"] = IntentType.Query,
        ["unknown"] = IntentType.Unknown,
    };

    public NluService(ILlmClient llmClient, ILogger<NluService> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<IntentResult> ParseIntent(string text, string? context = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing intent for text: {Text}", text[..Math.Min(text.Length, 100)]);

        try
        {
            var thinkSb = new System.Text.StringBuilder();
            var prompt = BuildIntentPrompt(text, context);
            var rawResponse = await _llmClient.CompleteWithThinkingAsync(prompt, null,
                onThinking: t => thinkSb.Append(t), cancellationToken);
            var json = ExtractJson(rawResponse);

            var classification = JsonSerializer.Deserialize<IntentClassificationResult>(json, _jsonOpts);
            if (classification == null)
            {
                _logger.LogWarning("Failed to deserialize NLU response");
                return new IntentResult { IntentType = IntentType.Unknown, OriginalText = text };
            }

            var intentType = _intentMap.GetValueOrDefault(classification.Intent, IntentType.Unknown);

            var entities = classification.Entities.Select(e => new EntityExtraction
            {
                Type = e.Type,
                Value = e.Value,
                Start = e.Start,
                End = e.End
            }).ToList();

            var result = new IntentResult
            {
                IntentType = intentType,
                Confidence = classification.Confidence,
                Entities = entities,
                OriginalText = text,
                ParsedParameters = classification.Parameters,
                Thinking = thinkSb.Length > 0 ? thinkSb.ToString() : null
            };

            _logger.LogInformation("Intent parsed: {IntentType} with confidence {Confidence}",
                result.IntentType, result.Confidence);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NLU parsing failed for text: {Text}", text);
            return new IntentResult { IntentType = IntentType.Unknown, OriginalText = text };
        }
    }

    private static string BuildIntentPrompt(string text, string? context)
    {
        return GetIntentClassificationTemplate().Replace("{{input}}", text);
    }

    private static string GetIntentClassificationTemplate()
    {
        // Embedded fallback — actual template loaded from .hbs in production
        return @"
You are an NLU engine for a 3D design assistant. Classify the user's intent and extract entities.

Intents: Create (new 3D model), Modify (change existing), Query (ask question), Unknown

Respond with valid JSON only:
{ ""Intent"": ""Create|Modify|Query|Unknown"", ""Confidence"": 0.0-1.0, ""Entities"": [{ ""Type"": ""..."", ""Value"": ""..."", ""Start"": 0, ""End"": 0 }], ""Parameters"": {} }

User input: {{input}}
";
    }

    private static string ExtractJson(string raw)
    {
        raw = raw.Trim();
        // Strip thinking block (SSE reasoning tokens prepended by ILlmClient)
        var thinkEnd = raw.LastIndexOf("</|thinking|>");
        if (thinkEnd >= 0)
            raw = raw[(thinkEnd + "</|thinking|>".Length)..].Trim();

        // Remove LLM preamble (e.g. "Sure, here's the JSON:" before the actual JSON)
        var jsonStart = raw.IndexOf('{');
        if (jsonStart > 0)
        {
            var beforeJson = raw[..jsonStart];
            // Only strip if it's a short preamble, not JSON content
            if (beforeJson.Length < 200 && !beforeJson.Contains('{'))
                raw = raw[jsonStart..];
        }

        // Remove markdown code fences if present
        if (raw.StartsWith("```json"))
            raw = raw[7..];
        if (raw.StartsWith("```"))
            raw = raw[3..];
        if (raw.EndsWith("```"))
            raw = raw[..^3];
        return raw.Trim();
    }
}
