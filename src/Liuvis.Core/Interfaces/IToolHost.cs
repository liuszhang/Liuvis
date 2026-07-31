namespace Liuvis.Core.Interfaces;

/// <summary>
/// 工具宿主接口，为 Agent 提供工具注册与枚举能力。
/// 在阶段一作为接口占位，后续阶段用于对接 IToolRegistry 和技能清单。
/// </summary>
public interface IToolHost
{
    /// <summary>获取当前可用的工具定义列表（用于组装系统提示词中的工具清单）。</summary>
    Task<IReadOnlyList<ToolHostDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default);
}

/// <summary>工具宿主侧的工具定义（轻量 DTO，不耦合 CJCore 的 ToolDefinition）。</summary>
public record ToolHostDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsDangerous { get; init; }
}
