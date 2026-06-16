using Liuvis.Core.ValueObjects;

namespace Liuvis.Generation.Templates;

/// <summary>Base class for geometry templates.</summary>
public abstract class GeometryTemplate
{
    public abstract string GeometryType { get; }
    public abstract ComponentSpec ToSpec(string name, Dictionary<string, object> parameters);
}

public class CylinderTemplate : GeometryTemplate
{
    public override string GeometryType => "cylinder";

    public override ComponentSpec ToSpec(string name, Dictionary<string, object> parameters)
        => new()
        {
            Name = name,
            GeometryType = "cylinder",
            Parameters = parameters
        };
}

public class BoxTemplate : GeometryTemplate
{
    public override string GeometryType => "box";

    public override ComponentSpec ToSpec(string name, Dictionary<string, object> parameters)
        => new()
        {
            Name = name,
            GeometryType = "box",
            Parameters = parameters
        };
}

public class SphereTemplate : GeometryTemplate
{
    public override string GeometryType => "sphere";

    public override ComponentSpec ToSpec(string name, Dictionary<string, object> parameters)
        => new()
        {
            Name = name,
            GeometryType = "sphere",
            Parameters = parameters
        };
}

public class CompositeTemplate : GeometryTemplate
{
    public override string GeometryType => "composite";

    public override ComponentSpec ToSpec(string name, Dictionary<string, object> parameters)
        => new()
        {
            Name = name,
            GeometryType = "composite",
            Parameters = parameters,
            Children = new List<ComponentSpec>
            {
                new BoxTemplate().ToSpec($"{name}_base", new()),
                new CylinderTemplate().ToSpec($"{name}_body", new())
            }
        };
}
