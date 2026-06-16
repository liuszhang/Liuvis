using System.Text.Json;
using Liuvis.Core.Entities;
using Liuvis.Core.Interfaces;
using Liuvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Liuvis.Infrastructure.Services;

public class SettingsService : ISettingsService
{
    private readonly LiuvisDbContext _db;

    public SettingsService(LiuvisDbContext db) => _db = db;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FindAsync([key], ct);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value, string? description = null, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FindAsync([key], ct);
        if (setting == null)
        {
            _db.AppSettings.Add(new AppSetting(key, value, description));
        }
        else
        {
            setting.Value = value;
            setting.Description = description;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<LlmSettings> GetLlmSettingsAsync(CancellationToken ct = default)
    {
        var json = await GetAsync("llm_settings", ct);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<LlmSettings>(json) ?? LlmDefaults();
                // Safety: if OpenAI selected but no API key configured, revert to defaults
                if (settings.Provider == "openai" && string.IsNullOrWhiteSpace(settings.OpenAIApiKey))
                {
                    return LlmDefaults();
                }
                return settings;
            }
            catch { }
        }
        return LlmDefaults();
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FindAsync([key], ct);
        if (setting != null)
        {
            _db.AppSettings.Remove(setting);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task SaveLlmSettingsAsync(LlmSettings settings, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(settings);
        await SetAsync("llm_settings", json, "LLM provider configuration", ct);
    }

    private static LlmSettings LlmDefaults() => new();
}
