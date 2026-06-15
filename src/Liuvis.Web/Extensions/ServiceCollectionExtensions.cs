using Liuvis.Core.Interfaces;
using Liuvis.Infrastructure.LLM;
using Liuvis.Infrastructure.Persistence;
using Liuvis.Infrastructure.Repositories;
using Liuvis.Infrastructure.VectorSearch;
using Liuvis.Infrastructure.ObjectStorage;
using Liuvis.Infrastructure.Configuration;
using Liuvis.Infrastructure.Services;
using Liuvis.NLU.Services;
using Liuvis.Session.Services;
using Liuvis.Design.Services;
using Liuvis.Generation.Services;
using Liuvis.Generation.Geometry;
using Liuvis.Modification.Services;
using Liuvis.KnowledgeBase.Services;
using Microsoft.EntityFrameworkCore;

namespace Liuvis.Web.Extensions;

/// <summary>
/// Centralized service registration for Liuvis application.
/// All module implementations are registered here following the DI switching convention.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLiuvisApplicationServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ---------------------------------------------------------------------
        // Database — EF Core + PostgreSQL
        // ---------------------------------------------------------------------
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<LiuvisDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(LiuvisDbContext).Assembly.FullName);
            });
        });

        // ---------------------------------------------------------------------
        // Repositories
        // ---------------------------------------------------------------------
        services.AddScoped<SessionRepository>();
        services.AddScoped<ModelRepository>();
        services.AddScoped<KnowledgeEntryRepository>();

        // ---------------------------------------------------------------------
        // Settings Service
        // ---------------------------------------------------------------------
        services.AddScoped<ISettingsService, SettingsService>();

        // ---------------------------------------------------------------------
        // LLM Client — provider switching based on persisted settings
        // ---------------------------------------------------------------------
        services.AddScoped<ILlmClient>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>()
                .GetLlmSettingsAsync().GetAwaiter().GetResult();

            if (settings.Provider == "openai" && !string.IsNullOrWhiteSpace(settings.OpenAIApiKey))
            {
                var model = settings.OpenAIModel ?? "gpt-4o";
                var logger = sp.GetRequiredService<ILogger<OpenAIClient>>();
                logger.LogInformation("Using OpenAI provider: {Endpoint}, model: {Model}",
                    settings.OpenAIBaseUrl, model);
                return new OpenAIClient(
                    apiKey: settings.OpenAIApiKey,
                    baseUrl: settings.OpenAIBaseUrl,
                    model: model,
                    embeddingModel: "text-embedding-3-small",
                    maxTokens: settings.MaxTokens,
                    temperature: settings.Temperature,
                    logger: logger);
            }

            if (settings.Provider == "openai")
            {
                var logger = sp.GetRequiredService<ILogger<OllamaClient>>();
                logger.LogWarning("OpenAI selected but no API key configured, falling back to Ollama");
            }

            var endpoint = new Uri(settings.OllamaUrl);
            return new OllamaClient(endpoint, settings.OllamaModel,
                sp.GetRequiredService<ILogger<OllamaClient>>());
        });

        // ---------------------------------------------------------------------
        // Vector Search
        // ---------------------------------------------------------------------
        services.AddScoped<IVectorSearchService, PgvectorService>();

        // ---------------------------------------------------------------------
        // Object Storage — Provider switching convention
        // ---------------------------------------------------------------------
        var storageProvider = configuration.GetValue<string>("Storage:Provider") ?? "Local";

        switch (storageProvider.ToLowerInvariant())
        {
            case "minio":
                services.Configure<MinioOptions>(configuration.GetSection("Storage:MinIO"));
                services.AddScoped<IObjectStorageService, MinioStorageService>();
                break;
            case "local":
            default:
                services.Configure<LocalStorageOptions>(configuration.GetSection("Storage"));
                services.AddScoped<IObjectStorageService, LocalStorageService>();
                break;
        }

        // ---------------------------------------------------------------------
        // Generation Services (LLM-driven procedural geometry)
        // ---------------------------------------------------------------------
        services.AddScoped<LLMDesignService>();
        services.AddScoped<ProceduralGeometryBuilder>();

        // ---------------------------------------------------------------------
        // Business Services (core module implementations)
        // ---------------------------------------------------------------------
        services.AddScoped<INluService, NluService>();
        services.AddScoped<ISessionManager, SessionManager>();
        services.AddScoped<IDesignEngine, DesignEngine>();
        services.AddScoped<IModelGenerator, ModelGenerator>();
        services.AddScoped<IModificationEngine, ModificationEngine>();
        services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();

        // ---------------------------------------------------------------------
        // Configuration
        // ---------------------------------------------------------------------
        services.Configure<AppSettings>(configuration);

        return services;
    }
}
