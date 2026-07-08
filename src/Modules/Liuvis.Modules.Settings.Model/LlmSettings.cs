namespace Liuvis.Modules.Settings;

public class LlmSettings
{
    public string Provider { get; set; } = "openai";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen3:4b";
    public string? OpenAIApiKey { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("openAIBaseUrl")]
    public string OpenAIBaseUrl { get; set; } = "https://api.deepseek.com";

    public string? OpenAIModel { get; set; } = "deepseek-v4-pro";
}
