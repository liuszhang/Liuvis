using System.Text.Json;
using CJCore.AgentTool.MCPTools;
using CJCore.CoreLog;
using Liuvis.Core.Interfaces;

namespace Liuvis.Agent.Tools;

/// <summary>
/// 阶段四：设计规则校验工具。
/// 对已生成的设计方案（SceneDescription）执行 M3 规则求值，
/// 返回违规项列表，供前端展示或在 Modify 链路中触发修正建议。
/// </summary>
public class DesignRuleCheckTool : IToolExecutor
{
    private readonly IOntologyContextService _ontology;

    public DesignRuleCheckTool(IOntologyContextService ontology)
    {
        _ontology = ontology;
    }

    public string Name => "design_rule_check";
    public string Description => "对当前三维设计方案执行 M3 设计规则校验，返回所有违规项及修复建议。用于在模型生成后验证是否符合企业设计规范。";
    public bool IsDangerous => false;
    public bool Enabled { get; set; } = true;

    public string ParametersJson => """
    {
      "type": "object",
      "properties": {
        "scene_json": {
          "type": "object",
          "description": "待校验的场景定义 JSON 对象，即 SceneDescription（含 objects 数组）"
        },
        "instance_type": {
          "type": "string",
          "description": "实例类型标识（对应 CJOntology 中的 MetaObject Code），如 'MechanicalPart'。留空则用默认类型。"
        }
      },
      "required": ["scene_json"]
    }
    """;

    public async Task<object?> ExecuteAsync(string argumentsJson, ToolExecutionContext ctx, CancellationToken ct = default)
    {
        CJLog.Information("DesignRuleCheckTool.ExecuteAsync: starting rule evaluation",
            source: "DesignRuleCheckTool");

        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("scene_json", out var sceneJson))
            return new { error = "缺少 scene_json 参数" };

        var instanceType = root.TryGetProperty("instance_type", out var it)
            && it.ValueKind == JsonValueKind.String ? it.GetString() : null;

        try
        {
            var results = await _ontology.EvaluateRuleAsync(sceneJson, instanceType, parameters: null, ct);

            var violations = results.Where(r => r.Violated).ToList();

            return new
            {
                total_rules_evaluated = results.Count,
                violation_count = violations.Count,
                violations = violations.Select(v => new
                {
                    v.RuleId,
                    v.RuleCode,
                    v.RuleName,
                    v.Message,
                    v.Severity,
                    v.Kind
                }),
                passed = results.Where(r => !r.Violated).Select(r => new
                {
                    r.RuleId,
                    r.RuleCode,
                    r.RuleName
                })
            };
        }
        catch (Exception ex)
        {
            CJLog.Error(ex, "DesignRuleCheckTool: rule evaluation failed", source: "DesignRuleCheckTool");
            return new { error = $"规则校验执行失败: {ex.Message}" };
        }
    }
}
