using System.Text.Json;
using CJCore.AgentTool.MCPTools;
using CJCore.CoreLog;
using Liuvis.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Liuvis.Agent.Tools;

/// <summary>
/// 阶段四：模板查询工具。
/// 查询知识库中已索引的模型模板（SourceType = ModelTemplate / DocumentTemplate），
/// 供 LLM 在生成设计方案时参考可复用的模板结构。
/// 
/// 生命周期说明：工具池（DefaultToolRegistry）为 Singleton，而 IKnowledgeBaseService
/// 为 Scoped，因此本工具注入 IServiceScopeFactory，在每次执行时创建临时 scope 解析。
/// </summary>
public class TemplateLookupTool : IToolExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TemplateLookupTool(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string Name => "template_lookup";
    public string Description => "查找可复用的三维模型模板或文档模板。用于生成前获取可参考的模板结构，提升生成效率与规范性。";
    public bool IsDangerous => false;
    public bool Enabled { get; set; } = true;

    public string ParametersJson => """
    {
      "type": "object",
      "properties": {
        "category": {
          "type": "string",
          "description": "模板分类关键词，如 '机械零件'、'建筑构件'、'管道连接件'。留空返回所有模板。"
        },
        "top_k": {
          "type": "integer",
          "description": "返回最相关的 top-K 条模板，默认 3",
          "default": 3
        }
      },
      "required": []
    }
    """;

    public async Task<object?> ExecuteAsync(string argumentsJson, ToolExecutionContext ctx, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;
        var category = root.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.String
            ? cat.GetString() : null;
        var topK = root.TryGetProperty("top_k", out var tk) && tk.ValueKind == JsonValueKind.Number
            ? tk.GetInt32() : 3;

        var query = category ?? "模型模板";
        CJLog.Information($"TemplateLookupTool: category=\"{category}\", topK={topK}", source: "TemplateLookupTool");

        List<Core.ValueObjects.ModelMatch> matches;
        using (var scope = _scopeFactory.CreateScope())
        {
            var kbService = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseService>();
            matches = await kbService.SearchModels(query, topK, ct);
        }

        // 过滤模板类型条目
        var templates = matches
            .Where(m => m.SourceType == Core.Ontology.KnowledgeSourceType.ModelTemplate
                     || m.SourceType == Core.Ontology.KnowledgeSourceType.DocumentTemplate)
            .Take(topK)
            .ToList();

        return new
        {
            query = category ?? "(全部模板)",
            template_count = templates.Count,
            templates = templates.Select(t => new
            {
                t.EntryId,
                t.ModelId,
                t.Category,
                t.Description,
                t.Tags,
                t.SourceType,
                t.OntologyCode
            })
        };
    }
}
