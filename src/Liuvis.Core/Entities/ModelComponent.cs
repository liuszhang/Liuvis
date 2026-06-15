using Liuvis.Core.ValueObjects;

namespace Liuvis.Core.Entities;

/// <summary>A component/part within a 3D model assembly.</summary>
public class ModelComponent
{
    public Guid ComponentId { get; private set; } = Guid.NewGuid();
    public Guid ModelId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid? ParentId { get; private set; }
    public string GeometryType { get; private set; } = "box";
    public Transform3D Transform { get; private set; } = new();
    public MaterialSpec Material { get; private set; } = new();
    public List<ModelComponent> Children { get; private set; } = new();

    private ModelComponent() { }

    public ModelComponent(Guid modelId, string name, string geometryType = "box", Guid? parentId = null)
    {
        ModelId = modelId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        GeometryType = geometryType;
        ParentId = parentId;
    }

    public void SetTransform(Transform3D transform)
    {
        Transform = transform ?? throw new ArgumentNullException(nameof(transform));
    }

    public void SetMaterial(MaterialSpec material)
    {
        Material = material ?? throw new ArgumentNullException(nameof(material));
    }

    public void AddChild(ModelComponent child)
    {
        Children.Add(child);
    }
}
