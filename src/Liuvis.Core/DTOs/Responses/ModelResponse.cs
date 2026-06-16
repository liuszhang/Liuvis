using Liuvis.Core.Enums;

namespace Liuvis.Core.DTOs.Responses;

/// <summary>Response with model details.</summary>
public record ModelResponse
{
    public Guid ModelId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ModelFormat Format { get; init; }
    public string FileUrl { get; init; } = string.Empty;
    public string? ThumbnailUrl { get; init; }
    public int ComponentCount { get; init; }
    public int Version { get; init; }
    public List<string> Tags { get; init; } = new();
    public DateTime CreatedAt { get; init; }
}
