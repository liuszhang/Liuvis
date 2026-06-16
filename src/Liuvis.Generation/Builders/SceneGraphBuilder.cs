using Liuvis.Core.Entities;
using Liuvis.Core.ValueObjects;

namespace Liuvis.Generation.Builders;

/// <summary>Builds a scene graph from component specifications.</summary>
public class SceneGraphBuilder
{
    private readonly Dictionary<string, ModelComponent> _components = new();

    public ModelComponent BuildRoot(ComponentSpec spec, Guid modelId)
    {
        var root = CreateComponent(spec, modelId);
        BuildChildren(spec, root, modelId);
        return root;
    }

    private ModelComponent CreateComponent(ComponentSpec spec, Guid modelId, Guid? parentId = null)
    {
        var component = new ModelComponent(modelId, spec.Name, spec.GeometryType, parentId);
        component.SetMaterial(spec.Material);

        var key = spec.Name;
        if (!_components.ContainsKey(key))
            _components[key] = component;

        return component;
    }

    private void BuildChildren(ComponentSpec parent, ModelComponent parentComponent, Guid modelId)
    {
        foreach (var child in parent.Children)
        {
            var childComponent = CreateComponent(child, modelId, parentComponent.ComponentId);
            parentComponent.AddChild(childComponent);
            BuildChildren(child, childComponent, modelId);
        }
    }
}
