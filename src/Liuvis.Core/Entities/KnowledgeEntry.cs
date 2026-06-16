namespace Liuvis.Core.Entities;

/// <summary>An entry in the knowledge base indexing a model with embeddings.</summary>
public class KnowledgeEntry
{
    public Guid EntryId { get; private set; } = Guid.NewGuid();
    public Guid ModelId { get; private set; }
    public float[] Embedding { get; private set; } = Array.Empty<float>();
    public List<string> Tags { get; private set; } = new();
    public string Category { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

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
}
