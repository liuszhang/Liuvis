namespace Liuvis.Modules.Settings;

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

    // Tools & Skills management
    Task<ToolsSettings> GetToolsSettingsAsync(CancellationToken ct = default);
    Task SaveToolsSettingsAsync(ToolsSettings settings, CancellationToken ct = default);
}
