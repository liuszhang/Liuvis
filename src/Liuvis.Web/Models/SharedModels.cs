namespace Liuvis.Web.Models;

/// <summary>View model for component tree display with hierarchical support.</summary>
public class ComponentVm
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GeometryType { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int TriangleCount { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsSelected { get; set; }
    public List<ComponentVm>? Children { get; set; }
}

/// <summary>Arguments for model ready event with component list.</summary>
public record ModelReadyArgs(string ModelUrl, List<ComponentVm> Components);

/// <summary>Arguments for component selected event.</summary>
public record ComponentSelectedArgs(string ComponentId, string ComponentName);

/// <summary>Arguments for component visibility toggle.</summary>
public record ComponentVisibilityArgs(string ComponentId, bool IsVisible);

/// <summary>Arguments for property changed event.</summary>
public record PropertyChangedArgs(string? NewColor, string? NewMaterial);

/// <summary>
/// Lightweight component info passed from server to JS for mesh separation.
/// </summary>
public class ComponentMeshInfo
{
    public string Name { get; set; } = string.Empty;
    public int TriangleStart { get; set; }
    public int TriangleCount { get; set; }
}
