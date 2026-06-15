using Liuvis.Core.Enums;
using Liuvis.Core.ValueObjects;

namespace Liuvis.Core.Entities;

/// <summary>Represents a 3D model with its components and metadata.</summary>
public class Model3D
{
    public Guid ModelId { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ModelFormat Format { get; private set; } = ModelFormat.GLB;
    public string FilePath { get; private set; } = string.Empty;
    public string? ThumbnailPath { get; private set; }
    public List<ModelComponent> Components { get; private set; } = new();
    public Dictionary<string, string> Metadata { get; private set; } = new();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public int Version { get; private set; } = 1;
    public List<string> Tags { get; private set; } = new();

    private Model3D() { }

    public Model3D(string name, string description, ModelFormat format = ModelFormat.GLB)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? string.Empty;
        Format = format;
    }

    public void SetFilePath(string filePath)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public void SetThumbnail(string thumbnailPath)
    {
        ThumbnailPath = thumbnailPath;
    }

    public void AddComponent(ModelComponent component)
    {
        Components.Add(component);
    }

    public void IncrementVersion()
    {
        Version++;
    }

    public void AddTag(string tag)
    {
        if (!Tags.Contains(tag))
            Tags.Add(tag);
    }
}
