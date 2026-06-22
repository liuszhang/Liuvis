using System.Text.Json;
using Liuvis.Core.Entities;
using Liuvis.Core.Interfaces;
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

    private static LlmSettings? _cachedLlmSettings;
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

            var activeProvider = db.LlmProviders.FirstOrDefault(p => p.IsActive);
            if (activeProvider is not null)
            {
                var settings = MapToLlmSettings(activeProvider);
                lock (_cacheLock) { _cachedLlmSettings = settings; }
                logger.LogInformation("Preloaded LLM settings from active provider: {Name}", activeProvider.Name);
            }

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

    public static LlmSettings GetCachedLlmSettings()
    {
        lock (_cacheLock)
        {
            return _cachedLlmSettings ?? new LlmSettings();
        }
    }

    public async Task<LlmSettings> GetLlmSettingsAsync(CancellationToken ct = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();
        var activeProvider = await db.LlmProviders.FirstOrDefaultAsync(p => p.IsActive, ct);

        if (activeProvider is null)
            return new LlmSettings();

        var settings = MapToLlmSettings(activeProvider);
        lock (_cacheLock) { _cachedLlmSettings = settings; }
        return settings;
    }

    public async Task SaveLlmSettingsAsync(LlmSettings settings, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();

            var activeProvider = await db.LlmProviders.FirstOrDefaultAsync(p => p.IsActive, ct);
            if (activeProvider is not null)
            {
                MapFromLlmSettings(settings, activeProvider);
            }
            else
            {
                activeProvider = new LlmProvider
                {
                    Name = "Default",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                MapFromLlmSettings(settings, activeProvider);
                db.LlmProviders.Add(activeProvider);
            }

            await db.SaveChangesAsync(ct);
            lock (_cacheLock) { _cachedLlmSettings = settings; }
        }
        finally
        {
            _writeLock.Release();
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

    // --- Multi-provider management ---

    public async Task<List<LlmProvider>> GetProvidersAsync(CancellationToken ct = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();
        return await db.LlmProviders.OrderBy(p => p.Name).ToListAsync(ct);
    }

    public async Task<LlmProvider?> GetActiveProviderAsync(CancellationToken ct = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();
        return await db.LlmProviders.FirstOrDefaultAsync(p => p.IsActive, ct);
    }

    public async Task<LlmProvider> AddProviderAsync(LlmProvider provider, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();

            provider.CreatedAt = DateTime.UtcNow;

            // If this is the first provider, make it active
            if (!await db.LlmProviders.AnyAsync(ct))
                provider.IsActive = true;

            db.LlmProviders.Add(provider);
            await db.SaveChangesAsync(ct);

            if (provider.IsActive)
                RefreshCache(provider);

            return provider;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateProviderAsync(LlmProvider provider, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();

            var existing = await db.LlmProviders.FindAsync([provider.Id], ct)
                ?? throw new InvalidOperationException($"Provider {provider.Id} not found");

            existing.Name = provider.Name;
            existing.Provider = provider.Provider;
            existing.ApiKey = provider.ApiKey;
            existing.BaseUrl = provider.BaseUrl;
            existing.Model = provider.Model;
            existing.OllamaUrl = provider.OllamaUrl;
            existing.OllamaModel = provider.OllamaModel;
            existing.MaxTokens = provider.MaxTokens;
            existing.Temperature = provider.Temperature;

            await db.SaveChangesAsync(ct);

            if (existing.IsActive)
                RefreshCache(existing);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteProviderAsync(int id, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();

            var provider = await db.LlmProviders.FindAsync([id], ct)
                ?? throw new InvalidOperationException($"Provider {id} not found");

            bool wasActive = provider.IsActive;
            db.LlmProviders.Remove(provider);
            await db.SaveChangesAsync(ct);

            // If deleted provider was active, activate the first remaining one
            if (wasActive)
            {
                var next = await db.LlmProviders.FirstOrDefaultAsync(ct);
                if (next is not null)
                {
                    next.IsActive = true;
                    await db.SaveChangesAsync(ct);
                    RefreshCache(next);
                }
                else
                {
                    lock (_cacheLock) { _cachedLlmSettings = null; }
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ActivateProviderAsync(int id, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();

            var provider = await db.LlmProviders.FindAsync([id], ct)
                ?? throw new InvalidOperationException($"Provider {id} not found");

            // Deactivate all others
            var all = await db.LlmProviders.ToListAsync(ct);
            foreach (var p in all)
                p.IsActive = (p.Id == id);

            await db.SaveChangesAsync(ct);
            RefreshCache(provider);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static LlmSettings MapToLlmSettings(LlmProvider p)
    {
        return new LlmSettings
        {
            Provider = p.Provider,
            OpenAIApiKey = p.ApiKey,
            OpenAIBaseUrl = p.BaseUrl ?? "https://api.deepseek.com",
            OpenAIModel = p.Model,
            OllamaUrl = p.OllamaUrl ?? "http://localhost:11434",
            OllamaModel = p.OllamaModel ?? "qwen3:4b",
            MaxTokens = p.MaxTokens,
            Temperature = p.Temperature
        };
    }

    private static void MapFromLlmSettings(LlmSettings s, LlmProvider p)
    {
        p.Provider = s.Provider;
        p.ApiKey = s.OpenAIApiKey;
        p.BaseUrl = s.OpenAIBaseUrl;
        p.Model = s.OpenAIModel;
        p.OllamaUrl = s.OllamaUrl;
        p.OllamaModel = s.OllamaModel;
        p.MaxTokens = s.MaxTokens;
        p.Temperature = s.Temperature;
    }

    private static void RefreshCache(LlmProvider provider)
    {
        var settings = MapToLlmSettings(provider);
        lock (_cacheLock) { _cachedLlmSettings = settings; }
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
                if (typeof(T) == typeof(LlmSettings))
                    _cachedLlmSettings = (LlmSettings)(object)result;
                else
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
