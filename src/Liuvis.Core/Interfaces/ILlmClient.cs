namespace Liuvis.Core.Interfaces;

/// <summary>LLM client abstraction for text completion and embeddings.</summary>
public interface ILlmClient
{
    /// <summary>Complete a prompt and return the full response.</summary>
    Task<string> CompleteAsync(string prompt, string? systemMessage = null, CancellationToken cancellationToken = default);

    /// <summary>Complete a prompt with streaming response chunks.</summary>
    IAsyncEnumerable<string> CompleteStreamingAsync(string prompt, string? systemMessage = null, CancellationToken cancellationToken = default);

    /// <summary>Complete with real-time thinking and response token callbacks.</summary>
    Task<string> CompleteWithThinkingAsync(string prompt, string? systemMessage,
        Action<string>? onThinking,
        Action<string>? onToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>Generate an embedding vector for the given text.</summary>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
