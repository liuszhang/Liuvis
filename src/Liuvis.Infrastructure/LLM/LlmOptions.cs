namespace Liuvis.Infrastructure.LLM;

public class LlmOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int MaxTokens { get; set; } = 2000;
    public double Temperature { get; set; } = 0.3;
}
