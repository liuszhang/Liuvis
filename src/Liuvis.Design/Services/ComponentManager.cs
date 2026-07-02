using Liuvis.Core.Entities;

namespace Liuvis.Design.Services;

/// <summary>
/// Manages component-level state for the active 3D model.
/// Tracks component selection, visibility, and raises events for UI updates.
/// </summary>
public class ComponentManager
{
    private readonly List<ComponentState> _components = new();
    private Guid? _selectedComponentId;

    /// <summary>Raised when component selection, visibility, or the component list changes.</summary>
    public event Action? OnStateChanged;

    /// <summary>All components in the active model.</summary>
    public IReadOnlyList<ComponentState> Components => _components.AsReadOnly();

    /// <summary>Currently selected component ID, or null if none selected.</summary>
    public Guid? SelectedComponentId
    {
        get => _selectedComponentId;
        set
        {
            if (_selectedComponentId != value)
            {
                _selectedComponentId = value;
                OnStateChanged?.Invoke();
            }
        }
    }

    /// <summary>Number of components in the model.</summary>
    public int Count => _components.Count;

    /// <summary>
    /// Load components from a Model3D entity. Replaces any existing component state.
    /// </summary>
    public void LoadFromModel(Model3D model)
    {
        _components.Clear();

        foreach (var component in model.Components)
        {
            string? color = null;
            if (model.Metadata.TryGetValue($"Color_{component.Name}", out var c))
                color = c;

            var state = new ComponentState
            {
                ComponentId = component.ComponentId,
                Name = component.Name,
                GeometryType = component.GeometryType,
                IsVisible = true,
                IsSelected = false,
                TriangleCount = int.TryParse(
                    model.Metadata.GetValueOrDefault($"Component_{_components.Count}_TriangleCount", "0"),
                    out var tc) ? tc : 0,
                BoundingBoxMin = ReadBoundingBox(model.Metadata, _components.Count, "Min"),
                BoundingBoxMax = ReadBoundingBox(model.Metadata, _components.Count, "Max"),
            };

            _components.Add(state);
        }

        _selectedComponentId = null;
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Load components from a flat list (e.g., from API response).
    /// </summary>
    public void LoadFromList(IEnumerable<ComponentState> components)
    {
        _components.Clear();
        _components.AddRange(components);
        _selectedComponentId = null;
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Select a component by ID. Deselects any previously selected component.
    /// </summary>
    public void SelectComponent(Guid componentId)
    {
        foreach (var c in _components)
            c.IsSelected = c.ComponentId == componentId;

        _selectedComponentId = componentId;
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Deselect the currently selected component.
    /// </summary>
    public void DeselectAll()
    {
        foreach (var c in _components)
            c.IsSelected = false;

        _selectedComponentId = null;
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Toggle visibility of a specific component.
    /// </summary>
    public void ToggleVisibility(Guid componentId)
    {
        var component = _components.Find(c => c.ComponentId == componentId);
        if (component != null)
        {
            component.IsVisible = !component.IsVisible;
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Set visibility of a specific component.
    /// </summary>
    public void SetVisibility(Guid componentId, bool visible)
    {
        var component = _components.Find(c => c.ComponentId == componentId);
        if (component != null && component.IsVisible != visible)
        {
            component.IsVisible = visible;
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Get a component by ID.
    /// </summary>
    public ComponentState? GetComponent(Guid componentId)
        => _components.Find(c => c.ComponentId == componentId);

    /// <summary>
    /// Clear all component state.
    /// </summary>
    public void Clear()
    {
        _components.Clear();
        _selectedComponentId = null;
        OnStateChanged?.Invoke();
    }

    private static (float X, float Y, float Z)? ReadBoundingBox(
        Dictionary<string, string> metadata, int index, string prefix)
    {
        var xKey = $"Component_{index}_{prefix}X";
        var yKey = $"Component_{index}_{prefix}Y";
        var zKey = $"Component_{index}_{prefix}Z";

        if (metadata.TryGetValue(xKey, out var xs) &&
            metadata.TryGetValue(yKey, out var ys) &&
            metadata.TryGetValue(zKey, out var zs) &&
            float.TryParse(xs, out var x) &&
            float.TryParse(ys, out var y) &&
            float.TryParse(zs, out var z))
        {
            return (x, y, z);
        }

        return null;
    }
}

/// <summary>
/// Runtime state of a single model component.
/// </summary>
public class ComponentState
{
    public Guid ComponentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GeometryType { get; set; } = "mesh";
    public bool IsVisible { get; set; } = true;
    public bool IsSelected { get; set; }
    public int TriangleCount { get; set; }
    public (float X, float Y, float Z)? BoundingBoxMin { get; set; }
    public (float X, float Y, float Z)? BoundingBoxMax { get; set; }
    public string? Color { get; set; }
}
