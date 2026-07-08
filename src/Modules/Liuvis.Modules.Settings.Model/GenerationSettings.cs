namespace Liuvis.Modules.Settings;

public class GenerationSettings
{
    public string Mode { get; set; } = "llm";

    // MCP Tool settings
    public string? McpServerUrl { get; set; } = "http://localhost:8080";
    public string? McpToolName { get; set; }
    public string? McpApiKey { get; set; }

    // Fake model settings
    public string FakeModelType { get; set; } = "box";
    public int FakeModelCount { get; set; } = 3;
}
