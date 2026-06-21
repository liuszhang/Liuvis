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
        // Settings Service — file-based (liuvis-settings.json), no DB dependency
        // -------------------------------------------------------------------------
        services.AddSingleton<ISettingsService, SettingsService>();

        // -------------------------------------------------------------------------
        // LLM Client — provider switching based on IConfiguration (appsettings.json
        // + liuvis-settings.json overrides). Zero DB dependency, zero blocking.
        // -------------------------------------------------------------------------
        services.AddTransient<ILlmClient>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Liuvis.DI.Build");
            var config = sp.GetRequiredService<IConfiguration>();
            var settings = config.GetSection("Liuvis:Llm").Get<LlmSettings>() ?? new LlmSettings();

            // --- 诊断：打印原始配置值，确认 binding 是否生效 ---
            var rawBaseUrl = config["Liuvis:Llm:openAIBaseUrl"]
                           ?? config["Liuvis:Llm:OpenAIBaseUrl"]
                           ?? "(not found in config)";
            var displayModel = settings.Provider == "openai"
                ? (settings.OpenAIModel ?? "unknown")
                : (settings.OllamaModel ?? settings.OpenAIModel ?? "unknown");
            logger.LogInformation("[DI.Build] LLM settings from config: Provider={Provider}, RawBaseUrl={RawBaseUrl}, BoundBaseUrl={BoundBaseUrl}, Model={Model}",
                settings.Provider, rawBaseUrl, settings.OpenAIBaseUrl, displayModel);

            if (settings.Provider == "openai" && !string.IsNullOrWhiteSpace(settings.OpenAIApiKey))
            {
                var model = settings.OpenAIModel ?? "gpt-4o";
                var openAiLogger = sp.GetRequiredService<ILogger<OpenAIClient>>();

                // --- 兜底：如果 ConfigurationBinder 未绑定 BaseUrl，手动读取 ---
                var effectiveBaseUrl = !string.IsNullOrWhiteSpace(settings.OpenAIBaseUrl)
                    && settings.OpenAIBaseUrl != "https://api.openai.com/v1"
                    ? settings.OpenAIBaseUrl
                    : (config["Liuvis:Llm:openAIBaseUrl"] ?? config["Liuvis:Llm:OpenAIBaseUrl"] ?? settings.OpenAIBaseUrl);

                openAiLogger.LogInformation("[DI.Build] Using OpenAI provider: Endpoint={Endpoint}, Model={Model}",
                    effectiveBaseUrl, model);
                return new OpenAIClient(
                    apiKey: settings.OpenAIApiKey,
                    baseUrl: effectiveBaseUrl,
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
        // UI State (scoped — survives route switches within the same circuit)
        // -------------------------------------------------------------------------
        services.AddScoped<DesignStudioState>();

        return services;
    }
}
