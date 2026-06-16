using Liuvis.Core.Entities;
using Liuvis.Core.ValueObjects;

namespace Liuvis.Session.Models;

/// <summary>The current context of a design session for LLM prompting.</summary>
public class SessionContext
{
    public Guid SessionId { get; set; }
    public Model3D? CurrentModel { get; set; }
    public List<SessionMessage> RecentMessages { get; set; } = new();
    public DesignSpec? CurrentSpec { get; set; }

    public string BuildContextSummary()
    {
        var parts = new List<string>();
        if (CurrentModel != null)
            parts.Add($"Current model: {CurrentModel.Name} ({CurrentModel.Components.Count} components)");
        if (CurrentSpec != null)
            parts.Add($"Design strategy: {CurrentSpec.Strategy}, Components: {CurrentSpec.Components.Count}");

        return string.Join(" | ", parts);
    }
}
