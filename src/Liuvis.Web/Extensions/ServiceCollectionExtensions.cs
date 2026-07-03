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
using Liuvis.Web.Services;
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
        // -------------------------------------------------------------------------
        // Database — EF Core + PostgreSQL
        // Connection string includes Timeout=2;Command Timeout=2 so Npgsql fails
        // fast when PostgreSQL is not running, instead of blocking for 93s.
        // -------------------------------------------------------------------------
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<LiuvisDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(LiuvisDbContext).Assembly.FullName);
            });
        });

        // -------------------------------------------------------------------------
        // Repositories
        // -------------------------------------------------------------------------
        services.AddScoped<SessionRepository>();
        services.AddScoped<ModelRepository>();
        services.AddScoped<KnowledgeEntryRepository>();

        // -------------------------------------------------------------------------
        // Settings Service — DB-backed (app_settings table)
        // -------------------------------------------------------------------------
        services.AddSingleton<ISettingsService, SettingsService>();

        // -------------------------------------------------------------------------
        // LLM Client — provider switching based on settings cached from DB.
        // SettingsService.PreloadFromDb must be called at startup before first resolution.
        // -------------------------------------------------------------------------
        services.AddTransient<ILlmClient>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Liuvis.DI.Build");
            var settings = SettingsService.GetCachedLlmSettings();

            var displayModel = settings.Provider == "openai"
                ? (settings.OpenAIModel ?? "unknown")
                : (settings.OllamaModel ?? settings.OpenAIModel ?? "unknown");
            logger.LogInformation("[DI.Build] LLM settings from DB: Provider={Provider}, BaseUrl={BaseUrl}, Model={Model}",
                settings.Provider, settings.OpenAIBaseUrl, displayModel);

            if (settings.Provider == "openai" && !string.IsNullOrWhiteSpace(settings.OpenAIApiKey))
            {
                var model = settings.OpenAIModel ?? "gpt-4o";
                var openAiLogger = sp.GetRequiredService<ILogger<OpenAIClient>>();

                openAiLogger.LogInformation("[DI.Build] Using OpenAI provider: Endpoint={Endpoint}, Model={Model}",
                    settings.OpenAIBaseUrl, model);
                return new OpenAIClient(
                    apiKey: settings.OpenAIApiKey,
                    baseUrl: settings.OpenAIBaseUrl,
                    model: model,
                    embeddingModel: "text-embedding-3-small",
                    maxTokens: settings.MaxTokens,
                    temperature: settings.Temperature,
                    logger: openAiLogger);
            }

            if (settings.Provider == "openai")
            {
                var ollamaLogger = sp.GetRequiredService<ILogger<OllamaClient>>();
                ollamaLogger.LogWarning("[DI.Build] OpenAI selected but no API key configured, falling back to Ollama");
            }

            var endpoint = new Uri(settings.OllamaUrl);
            logger.LogInformation("[DI.Build] Using Ollama provider: {Url}, model: {Model}", settings.OllamaUrl, settings.OllamaModel);
            return new OllamaClient(endpoint, settings.OllamaModel ?? "qwen3:4b",
                sp.GetRequiredService<ILogger<OllamaClient>>());
        });

        // -------------------------------------------------------------------------
        // Vector Search
        // -------------------------------------------------------------------------
        services.AddScoped<IVectorSearchService, PgvectorService>();

        // -------------------------------------------------------------------------
        // Object Storage — Provider switching convention
        // -------------------------------------------------------------------------
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

        // -------------------------------------------------------------------------
        // Generation Services (LLM-driven procedural geometry)
        // -------------------------------------------------------------------------
        services.AddScoped<LLMDesignService>();
        services.AddScoped<ProceduralGeometryBuilder>();
        services.AddScoped<StepExporter>();
        services.AddScoped<Liuvis.Generation.Importers.StlImporter>();

        // -------------------------------------------------------------------------
        // Business Services (core module implementations)
        // -------------------------------------------------------------------------
        services.AddScoped<INluService, NluService>();
        services.AddScoped<ISessionManager, SessionManager>();
        services.AddScoped<IDesignEngine, DesignEngine>();
        services.AddScoped<IModelGenerator, ModelGenerator>();
        services.AddScoped<IModificationEngine, ModificationEngine>();
        services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();

        // -------------------------------------------------------------------------
        // Orchestration Services (Blazor-friendly pipeline wrappers)
        // -------------------------------------------------------------------------
        services.AddScoped<ChatOrchestrationService>();

        // -------------------------------------------------------------------------
        // Component Management
        // -------------------------------------------------------------------------
        services.AddScoped<Liuvis.Design.Services.ComponentManager>();

        // -------------------------------------------------------------------------
        // UI State (scoped — survives route switches within the same circuit)
        // -------------------------------------------------------------------------
        services.AddScoped<DesignStudioState>();

        return services;
    }
}
