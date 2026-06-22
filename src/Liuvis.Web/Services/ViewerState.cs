namespace Liuvis.Web.Services;

/// <summary>
/// Global 3D viewer state. Singleton — survives page navigation.
/// Pages set ModelUrl/SceneData; the persistent ModelViewer in MainLayout reads from this.
/// </summary>
public class ViewerState
{
    private string? _modelUrl;
    private string? _sceneData;

    public event Action? OnChanged;

    public string? ModelUrl
    {
        get => _modelUrl;
        set
        {
            if (_modelUrl != value)
            {
                _modelUrl = value;
                OnChanged?.Invoke();
            }
        }
    }

    public string? SceneData
    {
        get => _sceneData;
        set
        {
            if (_sceneData != value)
            {
                _sceneData = value;
                OnChanged?.Invoke();
            }
        }
    }

    public void Clear()
    {
        _modelUrl = null;
        _sceneData = null;
        OnChanged?.Invoke();
    }
}
