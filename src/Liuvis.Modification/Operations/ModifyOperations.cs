using Liuvis.Core.Entities;
using Liuvis.Core.ValueObjects;

namespace Liuvis.Modification.Operations;

/// <summary>Interface for model modification operations.</summary>
public interface IModifyOperation
{
    Model3D Execute(Model3D model, ModificationRequest request);
}

/// <summary>Changes the color of a component's material.</summary>
public class ColorModifyOperation : IModifyOperation
{
    public Model3D Execute(Model3D model, ModificationRequest request)
    {
        var color = request.Parameters.TryGetValue("color", out var c) ? c?.ToString() ?? "#ff0000" : "#ff0000";

        foreach (var component in model.Components)
        {
            if (request.TargetComponent == "all" ||
                component.Name.Contains(request.TargetComponent, StringComparison.OrdinalIgnoreCase))
            {
                component.SetMaterial(component.Material with { Color = color });
            }

            // Also check children
            UpdateChildrenColor(component, request.TargetComponent, color);
        }

        return model;
    }

    private static void UpdateChildrenColor(ModelComponent parent, string target, string color)
    {
        foreach (var child in parent.Children)
        {
            if (target == "all" || child.Name.Contains(target, StringComparison.OrdinalIgnoreCase))
            {
                child.SetMaterial(child.Material with { Color = color });
            }
            UpdateChildrenColor(child, target, color);
        }
    }
}

/// <summary>Changes the material properties of a component.</summary>
public class MaterialModifyOperation : IModifyOperation
{
    public Model3D Execute(Model3D model, ModificationRequest request)
    {
        var roughness = request.Parameters.TryGetValue("roughness", out var r) ? Convert.ToDouble(r) : 0.5;
        var metalness = request.Parameters.TryGetValue("metalness", out var m) ? Convert.ToDouble(m) : 0.1;

        foreach (var component in model.Components)
        {
            if (request.TargetComponent == "all" ||
                component.Name.Contains(request.TargetComponent, StringComparison.OrdinalIgnoreCase))
            {
                component.SetMaterial(component.Material with
                {
                    Roughness = roughness,
                    Metalness = metalness,
                    Type = Core.Enums.MaterialType.PBR
                });
            }
        }

        return model;
    }
}

/// <summary>Changes the size/scale of a component.</summary>
public class SizeModifyOperation : IModifyOperation
{
    public Model3D Execute(Model3D model, ModificationRequest request)
    {
        var scaleFactor = request.Parameters.TryGetValue("scale", out var s) ? Convert.ToDouble(s) : 1.0;
        var scaleX = request.Parameters.TryGetValue("scaleX", out var sx) ? Convert.ToDouble(sx) : scaleFactor;
        var scaleY = request.Parameters.TryGetValue("scaleY", out var sy) ? Convert.ToDouble(sy) : scaleFactor;
        var scaleZ = request.Parameters.TryGetValue("scaleZ", out var sz) ? Convert.ToDouble(sz) : scaleFactor;

        foreach (var component in model.Components)
        {
            if (request.TargetComponent == "all" ||
                component.Name.Contains(request.TargetComponent, StringComparison.OrdinalIgnoreCase))
            {
                component.SetTransform(new Transform3D
                {
                    Position = component.Transform.Position,
                    Rotation = component.Transform.Rotation,
                    Scale = new Vector3(scaleX, scaleY, scaleZ)
                });
            }
        }

        return model;
    }
}
