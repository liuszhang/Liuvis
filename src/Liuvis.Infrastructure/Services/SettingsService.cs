using System.Text.Json;
using System.Text.Json.Nodes;
using Liuvis.Core.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Liuvis.Infrastructure.Services;

public class SettingsService : ISettingsService
{
    private readonly IConfiguration _configuration;
    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SettingsService(IConfiguration configuration, IWebHostEnvironment env)
    {
        _configuration = configuration;
        _settingsFilePath = Path.Combine(env.ContentRootPath, "liuvis-settings.json");
    }

    public Task<LlmSettings> GetLlmSettingsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(
            _configuration.GetSection("Liuvis:Llm").Get<LlmSettings>() ?? new LlmSettings());
    }

    public Task SaveLlmSettingsAsync(LlmSettings settings, CancellationToken ct = default)
    {
        return SaveSectionAsync("Llm", settings, ct);
    }

    public Task<GenerationSettings> GetGenerationSettingsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(
            _configuration.GetSection("Liuvis:Generation").Get<GenerationSettings>() ?? new GenerationSettings());
    }

    public Task SaveGenerationSettingsAsync(GenerationSettings settings, CancellationToken ct = default)
    {
        return SaveSectionAsync("Generation", settings, ct);
    }

    private async Task SaveSectionAsync<T>(string section, T value, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            JsonObject root;
            if (File.Exists(_settingsFilePath))
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath, ct);
                root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            root["Liuvis"] ??= new JsonObject();
            root["Liuvis"]![section] = JsonSerializer.SerializeToNode(value,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsFilePath, output, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
