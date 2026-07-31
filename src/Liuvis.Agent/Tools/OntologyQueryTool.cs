using System.Text.Json;
using CJCore.AgentTool.MCPTools;
using CJCore.CoreLog;
using Liuvis.Core.Interfaces;

namespace Liuvis.Agent.Tools;

/// <summary>
/// 阶段四：本体查询工具。
/// 将 IOntologyContextService 暴露为 Agent 可调用的只读工具，
/// 用于 LLM 在生成前查询领域本体定义、元模型结构、规则等信息。
/// </summary>
public class OntologyQueryTool : IToolExecutor
{
    private readonly IOntologyContextService _ontology;

    public OntologyQueryTool(IOntologyContextService ontology)
    {
        _ontology = ontology;
    }

    public string Name => "ontology_query";
    public string Description => "查询设计领域本体信息（对象类型/元模型/规则/知识工件索引）。用于在生成三维设计方案前了解可用领域模型与约束。";
    public bool IsDangerous => false;
    public bool Enabled { get; set; } = true;

    public string ParametersJson => """
    {
      "type": "object",
      "properties": {
        "query_type": {
          "type": "string",
          "enum": ["summary", "snapshot", "metamodel", "rules", "metadata"],
          "description": "查询类型：summary=本体概要，snapshot=本体快照，metamodel=元模型导出，rules=规则集，metadata=知识工件索引"
        },
        "target_type": {
          "type": "string",
          "description": "规则目标类型过滤（仅 query_type=rules 时生效），如 Validation/Derivation/Action"
        },
        "target_code": {
          "type": "string",
          "description": "规则目标对象代码过滤（仅 query_type=rules 时生效）"
        }
      },
      "required": ["query_type"]
    }
    """;

    public async Task<object?> ExecuteAsync(string argumentsJson, ToolExecutionContext ctx, CancellationToken ct = default)
    {
        CJLog.Information($"OntologyQueryTool.ExecuteAsync: args={argumentsJson[..Math.Min(argumentsJson.Length, 200)]}",
            source: "OntologyQueryTool");

        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;
        var queryType = root.GetProperty("query_type").GetString() ?? "summary";

        return queryType switch
        {
            "summary" => await QuerySummaryAsync(ct),
            "snapshot" => await QuerySnapshotAsync(ct),
            "metamodel" => await QueryMetamodelAsync(ct),
            "rules" => await QueryRulesAsync(root, ct),
            "metadata" => await QueryMetadataAsync(ct),
            _ => new { error = $"未知查询类型: {queryType}" }
        };
    }

    private async Task<object> QuerySummaryAsync(CancellationToken ct)
    {
        var result = await _ontology.GetOntologySummaryAsync(ct);
        return new { query_type = "summary", content = result };
    }

    private async Task<object> QuerySnapshotAsync(CancellationToken ct)
    {
        var result = await _ontology.GetSnapshotAsync(ct);
        return new { query_type = "snapshot", content = result };
    }

    private async Task<object> QueryMetamodelAsync(CancellationToken ct)
    {
        var result = await _ontology.GetMetamodelAsync(ct);
        return new { query_type = "metamodel", content = result };
    }

    private async Task<object> QueryRulesAsync(JsonElement root, CancellationToken ct)
    {
        var targetType = root.TryGetProperty("target_type", out var tt) && tt.ValueKind == JsonValueKind.String
            ? tt.GetString() : null;
        var targetCode = root.TryGetProperty("target_code", out var tc) && tc.ValueKind == JsonValueKind.String
            ? tc.GetString() : null;

        var rules = await _ontology.GetRulesAsync(targetType, targetCode, ct);
        return new
        {
            query_type = "rules",
            count = rules.Count,
            rules = rules.Select(r => new
            {
                r.Id, r.Code, r.Name, r.Kind,
                r.TargetType, r.TargetCode, r.Phase,
                r.Severity, r.Status, r.Priority
            })
        };
    }

    private async Task<object> QueryMetadataAsync(CancellationToken ct)
    {
        var items = await _ontology.GetMetadataIndexAsync(ct);
        return new
        {
            query_type = "metadata",
            count = items.Count,
            items = items.Select(m => new
            {
                m.Id, m.Code, m.Name, m.Type,
                m.FolderId, m.FolderName, m.LibraryName, m.Tags
            })
        };
    }
}
