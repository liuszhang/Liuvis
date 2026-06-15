using System.ClientModel;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Liuvis.Core.Interfaces;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using OpenAI.Embeddings;
using OpenAIClientOptions = global::OpenAI.OpenAIClientOptions;

namespace Liuvis.Infrastructure.LLM;

/// <summary>OpenAI / OpenAI-compatible API implementation of ILlmClient using the official OpenAI .NET SDK.</summary>
public class OpenAIClient : ILlmClient
{
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _embeddingModel;
    private readonly int _maxTokens;
    private readonly double _temperature;
    private readonly ILogger<OpenAIClient> _logger;
    private global::OpenAI.OpenAIClient? _openAiClient;

    public OpenAIClient(
        string apiKey,
        string baseUrl,
        string model,
        string embeddingModel,
        int maxTokens,
        double temperature,
        ILogger<OpenAIClient> logger)
    {
        _apiKey = apiKey;
        _baseUrl = NormalizeEndpoint(baseUrl);
        _model = model;
        _embeddingModel = embeddingModel;
        _maxTokens = maxTokens;
        _temperature = temperature;
        _logger = logger;
    }

    private global::OpenAI.OpenAIClient Client
    {
        get
        {
            if (_openAiClient != null) return _openAiClient;

            var apiKey = Environment.GetEnvironmentVariable("LIUVIS_OPENAI_APIKEY") ?? _apiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("OpenAI API key is not configured — LLM calls will fail until configured");
                apiKey = "sk-placeholder";
            }

            var isDefaultEndpoint = string.IsNullOrWhiteSpace(_baseUrl)
                || _baseUrl == "https://api.openai.com/v1";

            if (!isDefaultEndpoint)
            {
                _logger.LogInformation("OpenAI client connecting to custom endpoint: {Endpoint}", _baseUrl);
                var options = new OpenAIClientOptions { Endpoint = new Uri(_baseUrl) };
                _openAiClient = new global::OpenAI.OpenAIClient(new ApiKeyCredential(apiKey), options);
            }
            else
            {
                _openAiClient = new global::OpenAI.OpenAIClient(new ApiKeyCredential(apiKey));
            }

            return _openAiClient;
        }
    }

    public async Task<string> CompleteAsync(string prompt, string? systemMessage = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OpenAI CompleteAsync: model={Model}, prompt len={Len}", _model, prompt.Length);

        var messages = BuildMessages(prompt, systemMessage);
        var chatClient = Client.GetChatClient(_model);
        var response = await chatClient.CompleteChatAsync(
            messages,
            new ChatCompletionOptions
            {
                MaxOutputTokenCount = _maxTokens,
                Temperature = (float)_temperature,
            },
            cancellationToken);

        var content = response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;
        return content;
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt, string? systemMessage = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OpenAI CompleteStreamingAsync: model={Model}", _model);

        var messages = BuildMessages(prompt, systemMessage);
        var chatClient = Client.GetChatClient(_model);
        var updates = chatClient.CompleteChatStreamingAsync(
            messages,
            new ChatCompletionOptions
            {
                MaxOutputTokenCount = _maxTokens,
                Temperature = (float)_temperature,
            },
            cancellationToken);

        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                    yield return part.Text;
            }
        }
    }

    public async Task<float[]> GetEmbeddingAsync(string text,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OpenAI GetEmbeddingAsync: model={Model}, text len={Len}", _embeddingModel, text.Length);

        try
        {
            var embeddingClient = Client.GetEmbeddingClient(_embeddingModel);
            var response = await embeddingClient.GenerateEmbeddingAsync(text,
                cancellationToken: cancellationToken);
            var vector = response.Value.ToFloats().ToArray();
            return vector;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Embedding endpoint not available for {Endpoint} (model: {Model}), returning zero vector",
                _baseUrl, _embeddingModel);
            return new float[1536];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding generation failed, returning zero vector");
            return new float[1536];
        }
    }

    public async Task<string> CompleteWithThinkingAsync(string prompt, string? systemMessage,
        Action<string>? onThinking, CancellationToken cancellationToken = default)
    {
        // Use raw SSE streaming for OpenAI-compatible APIs to capture reasoning_content.
        // This matches the proven approach in DeepSeekService.StreamChatCompletionAsync.
        if (onThinking != null)
        {
            return await CompleteWithThinkingSseAsync(prompt, systemMessage, onThinking, cancellationToken);
        }
        return await CompleteAsync(prompt, systemMessage, cancellationToken);
    }

    /// <summary>
    /// Raw HTTP SSE streaming for OpenAI-compatible providers (DeepSeek, Groq, etc.)
    /// that support reasoning_content in delta messages.
    /// </summary>
    private async Task<string> CompleteWithThinkingSseAsync(string prompt, string? systemMessage,
        Action<string> onThinking, CancellationToken ct)
    {
        var endpoint = $"{_baseUrl}/chat/completions";
        _logger.LogDebug("OpenAI SSE streaming: endpoint={Endpoint}, model={Model}", endpoint, _model);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer",
                Environment.GetEnvironmentVariable("LIUVIS_OPENAI_APIKEY") ?? _apiKey);

        var messages = new List<object>
        {
            new { role = "system", content = systemMessage ?? "" },
            new { role = "user", content = prompt }
        };

        var payload = new
        {
            model = _model,
            messages,
            stream = true,
            max_tokens = _maxTokens,
            temperature = _temperature
        };

        var json = JsonSerializer.Serialize(payload);
        using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = httpContent };
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("SSE request failed {Status}: {Error}", (int)response.StatusCode, errorBody[..Math.Min(200, errorBody.Length)]);
            throw new HttpRequestException($"Chat API returned {(int)response.StatusCode}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var result = new StringBuilder();
        var thinkSb = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                {
                    foreach (var choice in choices.EnumerateArray())
                    {
                        if (choice.TryGetProperty("delta", out var delta))
                        {
                            // Capture reasoning/thinking content (DeepSeek-R1, etc.)
                            if (delta.TryGetProperty("reasoning_content", out var rc)
                                && rc.ValueKind == JsonValueKind.String)
                            {
                                var token = rc.GetString();
                                if (!string.IsNullOrEmpty(token))
                                {
                                    thinkSb.Append(token);
                                    onThinking(token);
                                }
                            }

                            // Capture main content
                            if (delta.TryGetProperty("content", out var c)
                                && c.ValueKind == JsonValueKind.String)
                            {
                                var token = c.GetString();
                                if (!string.IsNullOrEmpty(token))
                                    result.Append(token);
                            }
                        }
                    }
                }
            }
            catch (JsonException) { /* skip malformed SSE lines */ }
        }

        if (thinkSb.Length > 0)
            result.Insert(0, $"<|thinking|>{thinkSb}</|thinking|>\n\n");

        _logger.LogDebug("SSE complete: {CharCount} chars, {ThinkChars} thinking chars",
            result.Length, thinkSb.Length);
        return result.ToString();
    }

    private static ChatMessage[] BuildMessages(string prompt, string? systemMessage)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemMessage))
            messages.Add(new SystemChatMessage(systemMessage));
        messages.Add(new UserChatMessage(prompt));
        return messages.ToArray();
    }

    /// <summary>
    /// Normalizes a custom endpoint URL to end with /v1 for OpenAI SDK 2.x compatibility.
    /// DeepSeek, Groq, and other providers expect /v1/chat/completions rather than /anthropic or other paths.
    /// </summary>
    private static string NormalizeEndpoint(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        // Already an OpenAI endpoint — no normalization needed
        if (url.Contains("api.openai.com")) return url.TrimEnd('/');

        var uri = new Uri(url.TrimEnd('/'));

        // Already ends with /v1
        if (uri.AbsolutePath == "/v1" || uri.AbsolutePath.EndsWith("/v1"))
            return uri.ToString().TrimEnd('/');

        // Strip custom path, use /v1 as base
        return $"{uri.Scheme}://{uri.Authority}/v1";
    }
}
