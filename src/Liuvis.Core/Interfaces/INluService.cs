using Liuvis.Core.ValueObjects;

namespace Liuvis.Core.Interfaces;

/// <summary>Natural Language Understanding service for intent parsing.</summary>
public interface INluService
{
    /// <summary>Parse user input text into structured intent and entities.</summary>
    Task<IntentResult> ParseIntent(string text, string? context = null,
        Action<string>? onThinking = null,
        CancellationToken cancellationToken = default);
}
