using System.Text.Json;
using CJCore.CoreLog;
using Liuvis.Core.Interfaces;
using Liuvis.Modules.Settings;

namespace Liuvis.Generation.Services;

/// <summary>
/// 阶段三：本体增强的设计服务。
/// 在 LLM 生成前注入本体上下文（领域对象/规则/模板/知识工件）到提示词中，
/// 并在生成后执行 M3 设计规则校验。
/// 独立于 LLMDesignService，通过 ISettingsService + IOntologyContextService + ILlmClient 直调。
/// </summary>
public class OntologyEnhancedDesignService
{
    private readonly ILlmClient _llmClient;
    private readonly IOntologyContextService _ontologyService;
    private readonly IKnowledgeBaseService _kbService;
    private readonly ISettingsService _settingsService;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public OntologyEnhancedDesignService(
        ILlmClient llmClient,
        IOntologyContextService ontologyService,
        IKnowledgeBaseService kbService,
        ISettingsService settingsService)
    {
        _llmClient = llmClient;
        _ontologyService = ontologyService;
        _kbService = kbService;
        _settingsService = settingsService;
    }

    /// <summary>
    /// 生成场景定义（本体增强版）。
    /// 流程：获取本体概要 → 获取 M3 规则摘要 → 获取知识工件 → 组装提示词 → LLM 生成 → JSON 解析。
    /// </summary>
    public async Task<SceneDescription> GenerateSceneAsync(
        string description,
        CancellationToken ct = default)
    {
        CJLog.Information($"OntologyEnhancedDesign: generating scene for \"{description[..Math.Min(description.Length, 80)]}\"",
            source: "OntologyEnhancedDesign");

        var prompts = await _settingsService.GetPromptSettingsAsync(ct);
        var prompt = prompts.SceneGenerationPrompt;

        // 1. 注入本体上下文
        var ontologySummary = await GetOntologyContextAsync(ct);
        prompt = prompt.Replace("{{ontologyContext}}", ontologySummary);

        // 2. 注入设计规则摘要
        var rules = await GetRulesSummaryAsync(ct);
        prompt = prompt.Replace("{{designRules}}", rules);

        // 3. 注入用户描述
        prompt = prompt.Replace("{{description}}", description);

        // 4. LLM 生成
        var response = await _llmClient.CompleteAsync(prompt, null, ct);
        var json = ExtractJson(response);

        try
        {
            var scene = JsonSerializer.Deserialize<SceneDescription>(json, JsonOpts);
            if (scene is { Objects.Count: > 0 })
            {
                CJLog.Information($"OntologyEnhancedDesign: generated scene with {scene.Objects.Count} objects",
                    source: "OntologyEnhancedDesign");
                return scene;
            }
            CJLog.Warning("LLM returned empty scene from ontology-enhanced prompt", source: "OntologyEnhancedDesign");
        }
        catch (Exception ex)
        {
            CJLog.Warning($"Failed to parse LLM scene output: {ex.Message}", source: "OntologyEnhancedDesign");
        }

        // 兜底：返回默认对象
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

    /// <summary>获取本体上下文文本段。</summary>
    private async Task<string> GetOntologyContextAsync(CancellationToken ct)
    {
        try
        {
            if (!await _ontologyService.IsAvailableAsync(ct))
                return "(本体服务不可用)";

            var summary = await _ontologyService.GetOntologySummaryAsync(ct);
            if (!string.IsNullOrWhiteSpace(summary))
                return summary;
        }
        catch (Exception ex)
        {
            CJLog.Warning($"获取本体上下文失败: {ex.Message}", source: "OntologyEnhancedDesign");
        }
        return "(无本体上下文)";
    }

    /// <summary>获取 M3 规则摘要。</summary>
    private async Task<string> GetRulesSummaryAsync(CancellationToken ct)
    {
        try
        {
            if (!await _ontologyService.IsAvailableAsync(ct))
                return "无可用设计规则。";

            var rules = await _ontologyService.GetRulesAsync(cancellationToken: ct);
            if (rules == null || rules.Count == 0)
                return "无可用设计规则。";

            var lines = rules.Select(r =>
                $"- [{r.Kind}] {r.Name} ({r.Severity}) → {r.TargetType}.{r.TargetCode}");
            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            CJLog.Warning($"获取 M3 规则摘要失败: {ex.Message}", source: "OntologyEnhancedDesign");
            return "无可用设计规则。";
        }
    }

    /// <summary>从 LLM 原始输出中提取 JSON 片段。</summary>
    public static string ExtractJson(string raw)
    {
        raw = raw.Trim();

        if (raw.StartsWith("```json")) raw = raw[7..];
        else if (raw.StartsWith("```")) raw = raw[3..];
        if (raw.EndsWith("```")) raw = raw[..^3];

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
            return raw[start..(end + 1)].Trim();

        return raw.Trim();
    }
}
