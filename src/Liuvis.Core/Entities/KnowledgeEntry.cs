namespace Liuvis.Core.Entities;

/// <summary>An entry in the knowledge base indexing a model or ontology artifact with embeddings.</summary>
public class KnowledgeEntry
{
    public Guid EntryId { get; private set; } = Guid.NewGuid();
    public Guid ModelId { get; private set; }
    public float[] Embedding { get; private set; } = Array.Empty<float>();
    public List<string> Tags { get; private set; } = new();
    public string Category { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>本体对象 Code 关联（阶段五：从 CJOntology MetaData 导入知识工件时赋值）。</summary>
    public string? OntologyCode { get; private set; }

    /// <summary>知识来源类型（阶段五：区分模型描述/制度/法规/标准/模板等）。</summary>
    public Ontology.KnowledgeSourceType SourceType { get; private set; } = Ontology.KnowledgeSourceType.ModelDescription;

    private KnowledgeEntry() { }

    public KnowledgeEntry(Guid modelId, float[] embedding, string category, string description)
    {
        ModelId = modelId;
        Embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Description = description ?? string.Empty;
    }

    public void AddTag(string tag)
    {
        if (!Tags.Contains(tag))
            Tags.Add(tag);
    }

    /// <summary>设置本体关联信息（阶段五：导入知识工件时调用）。</summary>
    public void SetOntologySource(string ontologyCode, Ontology.KnowledgeSourceType sourceType)
    {
        OntologyCode = ontologyCode;
        SourceType = sourceType;
    }
}
