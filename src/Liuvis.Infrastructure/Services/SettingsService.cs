using System.Text.Json;
using Liuvis.Core.Entities;
using Liuvis.Modules.Settings;
using Liuvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Liuvis.Infrastructure.Services;

public class SettingsService : ISettingsService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private const string GenerationSettingsKey = "generation_settings";

    private static GenerationSettings? _cachedGenerationSettings;
    private static readonly object _cacheLock = new();

    public SettingsService(IServiceProvider serviceProvider, ILogger<SettingsService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public static void PreloadFromDb(IServiceProvider serviceProvider, ILogger logger)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();

            var genEntity = db.AppSettings.Find(GenerationSettingsKey);
            if (genEntity?.Value is not null)
            {
                var settings = JsonSerializer.Deserialize<GenerationSettings>(genEntity.Value,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                lock (_cacheLock) { _cachedGenerationSettings = settings; }
                logger.LogInformation("Preloaded Generation settings from DB");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to preload settings from DB, using defaults");
        }
    }

    public Task<GenerationSettings> GetGenerationSettingsAsync(CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_cachedGenerationSettings is not null)
                return Task.FromResult(_cachedGenerationSettings);
        }
        return LoadFromDbAsync<GenerationSettings>(GenerationSettingsKey, ct);
    }

    public async Task SaveGenerationSettingsAsync(GenerationSettings settings, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await SaveSettingValueAsync(GenerationSettingsKey, "Generation configuration", json, ct);
        lock (_cacheLock) { _cachedGenerationSettings = settings; }
    }

    // --- Prompt management ---

    private const string PromptSettingsKey = "prompt_settings";
    private static PromptSettings? _cachedPromptSettings;

    public Task<PromptSettings> GetPromptSettingsAsync(CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_cachedPromptSettings is not null)
                return Task.FromResult(_cachedPromptSettings);
        }
        return LoadPromptSettingsFromDbAsync(ct);
    }

    private async Task<PromptSettings> LoadPromptSettingsFromDbAsync(CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();
        var entity = await db.AppSettings.FindAsync([PromptSettingsKey], ct);

        if (entity?.Value is null)
        {
            var defaults = new PromptSettings();
            lock (_cacheLock) { _cachedPromptSettings = defaults; }
            return defaults;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<PromptSettings>(entity.Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PromptSettings();
            lock (_cacheLock) { _cachedPromptSettings = settings; }
            return settings;
        }
        catch
        {
            var fallback = new PromptSettings();
            lock (_cacheLock) { _cachedPromptSettings = fallback; }
            return fallback;
        }
    }

    public async Task SavePromptSettingsAsync(PromptSettings settings, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await SaveSettingValueAsync(PromptSettingsKey, "LLM system prompt templates", json, ct);
        lock (_cacheLock) { _cachedPromptSettings = settings; }
    }

    public async Task ResetPromptSettingsAsync(CancellationToken ct = default)
    {
        var defaults = new PromptSettings();
        await SavePromptSettingsAsync(defaults, ct);
    }

    // --- Tools & Skills management ---

    private const string ToolsSettingsKey = "tools_settings";
    private static ToolsSettings? _cachedToolsSettings;

    public Task<ToolsSettings> GetToolsSettingsAsync(CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_cachedToolsSettings is not null)
                return Task.FromResult(_cachedToolsSettings);
        }
        return LoadToolsSettingsFromDbAsync(ct);
    }

    private async Task<ToolsSettings> LoadToolsSettingsFromDbAsync(CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();
        var entity = await db.AppSettings.FindAsync([ToolsSettingsKey], ct);

        if (entity?.Value is null)
        {
            var defaults = new ToolsSettings();
            lock (_cacheLock) { _cachedToolsSettings = defaults; }
            return defaults;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ToolsSettings>(entity.Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ToolsSettings();
            lock (_cacheLock) { _cachedToolsSettings = settings; }
            return settings;
        }
        catch
        {
            var fallback = new ToolsSettings();
            lock (_cacheLock) { _cachedToolsSettings = fallback; }
            return fallback;
        }
    }

    public async Task SaveToolsSettingsAsync(ToolsSettings settings, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await SaveSettingValueAsync(ToolsSettingsKey, "MCP Servers and Skills configuration", json, ct);
        lock (_cacheLock) { _cachedToolsSettings = settings; }
    }

    // --- Legacy helpers for GenerationSettings (still uses app_settings) ---

    private async Task<T> LoadFromDbAsync<T>(string key, CancellationToken ct) where T : new()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();
        var entity = await db.AppSettings.FindAsync([key], ct);

        if (entity?.Value is null)
            return new T();

        try
        {
            var result = JsonSerializer.Deserialize<T>(entity.Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new T();
            lock (_cacheLock)
            {
                _cachedGenerationSettings = (GenerationSettings)(object)result;
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize settings from DB for key {Key}, using defaults", key);
            return new T();
        }
    }

    private async Task SaveSettingValueAsync(string key, string description, string value, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();

            var existing = await db.AppSettings.FindAsync([key], ct);
            if (existing is not null)
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.AppSettings.Add(new AppSetting(key, value, description));
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
