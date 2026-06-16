using Liuvis.Core.Enums;

namespace Liuvis.Core.DTOs.Responses;

/// <summary>Response with session details.</summary>
public record SessionResponse
{
    public Guid SessionId { get; init; }
    public SessionStatus Status { get; init; }
    public Guid? CurrentModelId { get; init; }
    public int MessageCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
