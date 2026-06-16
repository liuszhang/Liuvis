namespace Liuvis.Core.DTOs.Requests;

/// <summary>Request to create a model.</summary>
public class ModifyModelRequest
{
    public Guid ModelId { get; set; }
    public Guid SessionId { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string TargetComponent { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}
