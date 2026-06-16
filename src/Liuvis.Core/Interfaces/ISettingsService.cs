namespace Liuvis.Core.Interfaces;

/// <summary>Settings service for persistent application configuration.</summary>
public interface ISettingsService
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, string? description = null, CancellationToken ct = default);
    Task<LlmSettings> GetLlmSettingsAsync(CancellationToken ct = default);
    Task SaveLlmSettingsAsync(LlmSettings settings, CancellationToken ct = default);
}

public class LlmSettings
{
    public string Provider { get; set; } = "ollama";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen3:4b";
    public string? OpenAIApiKey { get; set; }
    public string OpenAIBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string? OpenAIModel { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 2000;
    public double Temperature { get; set; } = 0.3;

    [System.Text.Json.Serialization.JsonIgnore]
    public double TemperatureValue
    {
        get => Temperature;
        set => Temperature = Math.Round(value, 1);
    }
}
