namespace Liuvis.Core.ValueObjects;
using Liuvis.Core.Ontology;

/// <summary>Represents a match result from knowledge base search.</summary>
public record ModelMatch
{
    public Guid ModelId { get; init; }
    public string Name { get; init; } = string.Empty;
    public double Similarity { get; init; }
    public List<string> MatchedComponents { get; init; } = new();

    // 阶段五：本体关联字段
    public string? OntologyCode { get; init; }
    public KnowledgeSourceType SourceType { get; init; } = KnowledgeSourceType.ModelDescription;
    public Guid EntryId { get; init; }

    // 阶段五：知识工件元数据（模板/制度/法规/标准检索展示用）
    public string? Category { get; init; }
    public string? Description { get; init; }
    public string? Tags { get; init; }
}
