using System.Text.Json;
using CJCore.AgentTool.MCPTools;
using CJCore.CoreLog;
using Liuvis.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Liuvis.Agent.Tools;

/// <summary>
/// 阶段四：知识查询工具。
/// 将 IKnowledgeBaseService 暴露为只读工具，供 LLM 查找可复用的知识工件索引
/// （包括模型描述、制度/法规/标准等本体导入的知识条目）。
/// 
/// 生命周期说明：工具池（DefaultToolRegistry）为 Singleton，而 IKnowledgeBaseService
/// 为 Scoped，因此本工具注入 IServiceScopeFactory，在每次执行时创建临时 scope 解析，
/// 避免 Singleton 依赖 Scoped 的 DI 生命周期错误。
/// </summary>
public class KnowledgeQueryTool : IToolExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;

    public KnowledgeQueryTool(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string Name => "knowledge_query";
    public string Description => "搜索知识库中的可复用模型、设计模板、制度法规、标准等知识工件。在生成设计方案前用于查找可参考的现有模型和约束。";
    public bool IsDangerous => false;
    public bool Enabled { get; set; } = true;

    public string ParametersJson => """
    {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "自然语言搜索查询，如 '螺栓连接件'、'压力容器设计标准'"
        },
        "top_k": {
          "type": "integer",
          "description": "返回最相关的 top-K 条结果，默认 5",
          "default": 5
        }
      },
      "required": ["query"]
    }
    """;

    public async Task<object?> ExecuteAsync(string argumentsJson, ToolExecutionContext ctx, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;
        var query = root.GetProperty("query").GetString() ?? "";
        var topK = root.TryGetProperty("top_k", out var tk) && tk.ValueKind == JsonValueKind.Number
            ? tk.GetInt32() : 5;

        CJLog.Information($"KnowledgeQueryTool: query=\"{query}\", topK={topK}", source: "KnowledgeQueryTool");

        var matches = await SearchKnowledge(query, topK, ct);

        return new
        {
            query,
            result_count = matches.Count,
            results = matches.Select(m => new
            {
                m.EntryId,
                m.ModelId,
                m.Category,
                m.Description,
                m.Tags,
                m.OntologyCode,
                m.SourceType
            })
        };
    }

    /// <summary>在临时 scope 中解析 Scoped 的 IKnowledgeBaseService 执行搜索。</summary>
    private async Task<List<KnowledgeMatch>> SearchKnowledge(string query, int topK, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var kbService = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseService>();

        var matches = await kbService.SearchModels(query, topK, ct);
        return matches.Select(m => new KnowledgeMatch
        {
            EntryId = m.EntryId,
            ModelId = m.ModelId,
            Category = string.Join(",", m.MatchedComponents),
            Description = m.Name,
            Tags = m.MatchedComponents,
            OntologyCode = m.OntologyCode,
            SourceType = m.SourceType
        }).ToList();
    }

    private sealed class KnowledgeMatch
    {
        public Guid EntryId { get; init; }
        public Guid ModelId { get; init; }
        public string Category { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public List<string> Tags { get; init; } = new();
        public string? OntologyCode { get; init; }
        public Core.Ontology.KnowledgeSourceType SourceType { get; init; }
    }
}
