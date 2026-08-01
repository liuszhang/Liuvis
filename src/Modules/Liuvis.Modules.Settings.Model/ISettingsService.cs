namespace Liuvis.Modules.Settings;

/// <summary>Settings service backed by the app_settings database table.</summary>
public interface ISettingsService
{
    Task<GenerationSettings> GetGenerationSettingsAsync(CancellationToken ct = default);
    Task SaveGenerationSettingsAsync(GenerationSettings settings, CancellationToken ct = default);

    // Prompt management
    Task<PromptSettings> GetPromptSettingsAsync(CancellationToken ct = default);
    Task SavePromptSettingsAsync(PromptSettings settings, CancellationToken ct = default);
    Task ResetPromptSettingsAsync(CancellationToken ct = default);

    // Tools & Skills management
    Task<ToolsSettings> GetToolsSettingsAsync(CancellationToken ct = default);
    Task SaveToolsSettingsAsync(ToolsSettings settings, CancellationToken ct = default);
}
