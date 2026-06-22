using Liuvis.Core.Entities;

namespace Liuvis.Core.Interfaces;

/// <summary>Settings service backed by the app_settings and llm_providers database tables.</summary>
public interface ISettingsService
{
    Task<LlmSettings> GetLlmSettingsAsync(CancellationToken ct = default);
    Task SaveLlmSettingsAsync(LlmSettings settings, CancellationToken ct = default);
    Task<GenerationSettings> GetGenerationSettingsAsync(CancellationToken ct = default);
    Task SaveGenerationSettingsAsync(GenerationSettings settings, CancellationToken ct = default);

    // Multi-provider management
    Task<List<LlmProvider>> GetProvidersAsync(CancellationToken ct = default);
    Task<LlmProvider?> GetActiveProviderAsync(CancellationToken ct = default);
    Task<LlmProvider> AddProviderAsync(LlmProvider provider, CancellationToken ct = default);
    Task UpdateProviderAsync(LlmProvider provider, CancellationToken ct = default);
    Task DeleteProviderAsync(int id, CancellationToken ct = default);
    Task ActivateProviderAsync(int id, CancellationToken ct = default);

    // Prompt management
    Task<PromptSettings> GetPromptSettingsAsync(CancellationToken ct = default);
    Task SavePromptSettingsAsync(PromptSettings settings, CancellationToken ct = default);
    Task ResetPromptSettingsAsync(CancellationToken ct = default);
}

public class LlmSettings
{
    public string Provider { get; set; } = "openai";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen3:4b";
    public string? OpenAIApiKey { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("openAIBaseUrl")]
    public string OpenAIBaseUrl { get; set; } = "https://api.deepseek.com";

    public string? OpenAIModel { get; set; } = "deepseek-v4-pro";
    public int MaxTokens { get; set; } = 2000;
    public double Temperature { get; set; } = 0.3;

    [System.Text.Json.Serialization.JsonIgnore]
    public double TemperatureValue
    {
        get => Temperature;
        set => Temperature = Math.Round(value, 1);
    }
}

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
