namespace Liuvis.Core.Interfaces;

/// <summary>Configuration for an MCP Server endpoint.</summary>
public class McpServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string ServerUrl { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Configuration for a Skill (with optional file attachment).</summary>
public class SkillConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Container for all tools & skills settings.</summary>
public class ToolsSettings
{
    public List<McpServerConfig> McpServers { get; set; } = new();
    public List<SkillConfig> Skills { get; set; } = new();
}
