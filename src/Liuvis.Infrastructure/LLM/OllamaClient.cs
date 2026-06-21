using System.Runtime.CompilerServices;
using System.Text;
using Liuvis.Core.Interfaces;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace Liuvis.Infrastructure.LLM;

/// <summary>Ollama implementation using OllamaSharp with thinking capture support.</summary>
public class OllamaClient : ILlmClient
{
    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly ILogger<OllamaClient> _logger;

    public OllamaClient(Uri endpoint, string model, ILogger<OllamaClient> logger)
    {
        _endpoint = endpoint;
        _model = model;
        _logger = logger;
    }

    public async Task<string> CompleteAsync(string prompt, string? systemMessage = null,
        CancellationToken cancellationToken = default)
    {
        var chatClient = new OllamaApiClient(_endpoint, _model);
        var chat = new Chat(chatClient);
        return await SendInternal(chat, prompt, systemMessage, onThinking: null, onToken: null, cancellationToken);
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt, string? systemMessage = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatClient = new OllamaApiClient(_endpoint, _model);
        var chat = new Chat(chatClient);
        await foreach (var t in SendStreaming(chat, prompt, systemMessage, null, cancellationToken))
            yield return t;
    }

    /// <summary>Complete with thinking and response token callbacks.</summary>
    public async Task<string> CompleteWithThinkingAsync(string prompt, string? systemMessage,
        Action<string>? onThinking, Action<string>? onToken = null,
        CancellationToken cancellationToken = default)
    {
        var chatClient = new OllamaApiClient(_endpoint, _model);
        var chat = new Chat(chatClient);
        return await SendInternal(chat, prompt, systemMessage, onThinking, onToken, cancellationToken);
    }

    public async Task<float[]> GetEmbeddingAsync(string text,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("GetEmbeddingAsync: returning zero vector (Ollama embeddings not configured)");
        return new float[1536];
    }

    private async Task<string> SendInternal(Chat chat, string prompt, string? systemMessage,
        Action<string>? onThinking, Action<string>? onToken, CancellationToken ct)
    {
        var result = new StringBuilder();
        var thinkSb = new StringBuilder();

        if (onThinking != null)
            chat.OnThink += (_, e) => { thinkSb.Append(e); onThinking(e); };

        var fullPrompt = BuildPrompt(prompt, systemMessage);
        await foreach (var token in chat.SendAsync(fullPrompt, cancellationToken: ct))
        {
            var tokenStr = token.ToString()!;
            result.Append(tokenStr);
            onToken?.Invoke(tokenStr);
        }

        if (thinkSb.Length > 0)
            result.Insert(0, $"<|thinking|>{thinkSb}</|thinking|>\n\n");

        return result.ToString();
    }

    private async IAsyncEnumerable<string> SendStreaming(Chat chat, string prompt, string? systemMessage,
        Action<string>? onThinking, [EnumeratorCancellation] CancellationToken ct)
    {
        var thinkStarted = false;
        if (onThinking != null)
        {
            chat.OnThink += (_, e) =>
            {
                if (!thinkStarted) { onThinking("<|thinking_start|>"); thinkStarted = true; }
                onThinking(e);
            };
        }

        var fullPrompt = BuildPrompt(prompt, systemMessage);
        var hasResponse = false;
        await foreach (var token in chat.SendAsync(fullPrompt, cancellationToken: ct))
        {
            if (!hasResponse && thinkStarted) { yield return "<|response_start|>"; hasResponse = true; }
            yield return token.ToString()!;
        }
    }

    private static string BuildPrompt(string prompt, string? systemMessage)
        => string.IsNullOrWhiteSpace(systemMessage) ? prompt : $"{systemMessage}\n\n{prompt}";
}
