using Liuvis.Core.Interfaces;
using Liuvis.Infrastructure.LLM;
using Liuvis.Infrastructure.Persistence;
using Liuvis.Infrastructure.Repositories;
using Liuvis.Infrastructure.VectorSearch;
using Liuvis.Infrastructure.ObjectStorage;
using Liuvis.Infrastructure.Configuration;
using Liuvis.Infrastructure.Services;
using Liuvis.Infrastructure.Ontology;
using Liuvis.NLU.Services;
using Liuvis.Session.Services;
using Liuvis.Design.Services;
using Liuvis.Generation.Services;
using Liuvis.Generation.Geometry;
using Liuvis.Modification.Services;
using Liuvis.KnowledgeBase.Services;
using Liuvis.Web.Services;
using Liuvis.Agent;
using Liuvis.Agent.Tools;
using CJCore.Agent.Abstractions;
using CJCore.LLM.Abstractions;
using CJCore.Modules.LLM;
using CJCore.Modules.Data;
using CJCore.Modules.LLM.Api.Services;
using CJCore.Modules.LLM.Model;
using CJCore.AgentTool.MCPTools;
using CJCore.AgentTool.Skills;
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
        // LLM Client — provider/model resolved from CJCore ILLMConfigService
        // (SQLite cjcore_liuvis.db, managed via LLMConfigPage / api/llm/*).
        // Liuvis ILlmClient interface and business services stay unchanged.
        // -------------------------------------------------------------------------
        services.AddTransient<ILlmClient>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Liuvis.DI.Build");
            var configService = sp.GetRequiredService<ILLMConfigService>();
            var (provider, model) = configService.GetDefaultModelInfoAsync().GetAwaiter().GetResult();

            // Embeddings can be disabled globally (Liuvis:Embeddings:Enabled = false)
            // when no embedding provider is available — callers then get a zero vector
            // without any network request.
            var enableEmbeddings = configuration.GetValue<bool>("Liuvis:Embeddings:Enabled");

            if (provider is null || model is null)
            {
                logger.LogWarning("[DI.Build] No default LLM provider/model configured in CJCore, falling back to local Ollama");
                return new OllamaClient(new Uri("http://localhost:11434"), "qwen3:4b",
                    sp.GetRequiredService<ILogger<OllamaClient>>(), enableEmbeddings);
            }

            var modelName = model.ModelName;
            var providerName = provider.Name;

            if (string.Equals(providerName, "Ollama", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("[DI.Build] Using Ollama provider: {Url}, model: {Model}", provider.ApiBaseUrl, modelName);
                return new OllamaClient(new Uri(provider.ApiBaseUrl), modelName,
                    sp.GetRequiredService<ILogger<OllamaClient>>(), enableEmbeddings);
            }

            // OpenAI / AzureOpenAI / DeepSeek / OmniRoute / Custom all speak the OpenAI-compatible protocol.
            if (string.IsNullOrWhiteSpace(provider.ApiKey))
            {
                logger.LogWarning("[DI.Build] Provider {Provider} has no API key configured, falling back to local Ollama", providerName);
                return new OllamaClient(new Uri("http://localhost:11434"), "qwen3:4b",
                    sp.GetRequiredService<ILogger<OllamaClient>>(), enableEmbeddings);
            }

            var openAiLogger = sp.GetRequiredService<ILogger<OpenAIClient>>();
            openAiLogger.LogInformation("[DI.Build] Using {Provider} provider: Endpoint={Endpoint}, Model={Model}",
                providerName, provider.ApiBaseUrl, modelName);

            // Embedding model is configurable (appsettings "OpenAI:EmbeddingModel");
            // defaults to OpenAI text-embedding-3-small. It is sent to the same
            // provider endpoint as chat — if that provider cannot serve the chosen
            // embedding model, embeddings fail (see OmniRoute "no credentials" case).
            var embeddingModel = configuration.GetValue<string>("OpenAI:EmbeddingModel")
                ?? "text-embedding-3-small";

            return new OpenAIClient(
                apiKey: provider.ApiKey,
                baseUrl: provider.ApiBaseUrl,
                model: modelName,
                embeddingModel: embeddingModel,
                logger: openAiLogger,
                enableEmbeddings: enableEmbeddings);
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

        // -------------------------------------------------------------------------
        // Agent — CJCore LLM + Skill 系统 + ModelingAgent（阶段一接入）
        // -------------------------------------------------------------------------
        RegisterAgentServices(services, configuration);

        return services;
    }

    /// <summary>
    /// CJCore Agent 框架接入：
    /// 1) AddCJCoreLLM —— 依据 Liuvis:Llm 配置注册 CJCore LLM 客户端（OpenAI 或 Ollama）
    /// 2) AddFileSkillSystem —— 注册文件式技能提供器 + 共享 SkillRegistry + load_skill 工具
    /// 3) IToolRegistry —— 框架默认工具注册表（阶段四前仅含 load_skill 等静态工具）
    /// 4) ModelingAgent —— Scoped 注册（IAgent + 具体类型，供预热与显式解析）
    /// 5) OntologyContextService —— 阶段二本体上下文 REST 客户端（单例 + 定时刷新）
    /// </summary>
    private static void RegisterAgentServices(IServiceCollection services, IConfiguration configuration)
    {
        // ---- CJCore LLM ----
        services.AddCJCoreLLM(
            options =>
            {
                var llm = configuration.GetSection("Liuvis:Llm");
                var provider = (llm["Provider"] ?? "ollama").ToLowerInvariant();
                var temperature = llm.GetValue<double?>("Temperature") ?? 0.3;
                var maxTokens = llm.GetValue<int?>("MaxTokens") ?? 4096;

                // LLM 配置管理 ApiClient 的 baseUrl：指向本进程 /api/llm/* 端点（配置于 Liuvis:Llm:ApiBaseUrl），
                // 否则 AddCJCoreLLM 内部不会注册 ILlmConfigApiClient，LLMConfigPage 加载供应商会抛
                // "无法解析 IModuleApiClient 实现 'CJCore.Modules.LLM.ApiClient.ILlmConfigApiClient'"。
                options.ApiBaseUrl = llm["ApiBaseUrl"];

                if (provider == "openai")
                {
                    options.UseOpenAI = true;
                    options.OpenAIConfig = new LLMConfig
                    {
                        Name = "Liuvis OpenAI",
                        Provider = LLMProviderType.OpenAI,
                        Endpoint = llm["OpenAIBaseUrl"] ?? "https://api.deepseek.com",
                        ApiKey = llm["OpenAIApiKey"],
                        Model = llm["OpenAIModel"] ?? "gpt-4o",
                        Temperature = temperature,
                        MaxTokens = maxTokens,
                        TimeoutSeconds = 120,
                    };
                }
                else
                {
                    options.UseOllama = true;
                    options.OllamaConfig = new LLMConfig
                    {
                        Name = "Liuvis Ollama",
                        Provider = LLMProviderType.Ollama,
                        Endpoint = llm["OllamaUrl"] ?? "http://localhost:11434",
                        Model = llm["OllamaModel"] ?? "qwen3:4b",
                        Temperature = temperature,
                        MaxTokens = maxTokens,
                        TimeoutSeconds = 120,
                    };
                }
            },
            dataOptions =>
            {
                // CJCore LLM 内部使用 EF 存储 LLM 配置，独立于 Liuvis 主库
                dataOptions.Provider = DataProvider.Sqlite;
                dataOptions.ConnectionString = "Data Source=cjcore_liuvis.db";
            });

        // ---- Skill 系统（文件式技能 + 共享注册表 + load_skill 工具） ----
        var skillsRoot = configuration.GetValue<string>("Liuvis:Agent:SkillsRoot")
            ?? Path.Combine(AppContext.BaseDirectory, "skills");
        services.AddFileSkillSystem(skillsRoot);

        // ---- 阶段四：自定义工具（静态工具池，必须在 IToolRegistry 之前注册） ----
        // 说明：DefaultToolRegistry 构造时注入 IEnumerable<IToolExecutor> 形成静态池，
        // 因此工具必须在此处（AddSingleton<IToolRegistry> 之前）注册。
        // 依赖 Scoped 服务（IKnowledgeBaseService）的工具注入 IServiceScopeFactory，
        // 执行时创建临时 scope 解析，避免 Singleton 依赖 Scoped 的生命周期错误。
        services.AddSingleton<IToolExecutor, OntologyQueryTool>();
        services.AddSingleton<IToolExecutor, KnowledgeQueryTool>();
        services.AddSingleton<IToolExecutor, TemplateLookupTool>();
        services.AddSingleton<IToolExecutor, DesignRuleCheckTool>();
        services.AddSingleton<IToolExecutor, ModelExportTool>();

        // ---- IToolRegistry（框架默认实现，静态工具池来自已注册 IToolExecutor） ----
        services.AddSingleton<IToolRegistry, DefaultToolRegistry>();

        // ---- 阶段五：本体知识工件导入器（启动预热 + refresh 端点增量同步） ----
        services.AddScoped<OntologyKnowledgeImporter>();

        // ---- ModelingAgent ----
        services.AddScoped<ModelingAgentConfig>(_ =>
        {
            var config = new ModelingAgentConfig();
            var ontology = configuration.GetSection("Ontology");
            if (ontology.Exists())
            {
                config.CJOntologyBaseUrl = ontology["BaseUrl"] ?? config.CJOntologyBaseUrl;
                config.OntologyId = ontology["OntologyId"] ?? config.OntologyId;
                config.RefreshIntervalSeconds = ontology.GetValue("RefreshIntervalSeconds", config.RefreshIntervalSeconds);
            }
            return config;
        });
        services.AddScoped<ModelingAgent>();
        services.AddScoped<IAgent>(sp => sp.GetRequiredService<ModelingAgent>());

        // ---- 阶段二：本体上下文服务（REST + 进程内缓存 + 定时刷新） ----
        services.Configure<OntologyOptions>(configuration.GetSection("Ontology"));
        services.AddHttpClient("CJOntology", client =>
        {
            var baseUrl = configuration.GetValue<string>("Ontology:BaseUrl") ?? "http://localhost:5007";
            client.BaseAddress = new Uri(baseUrl);
        });
        services.AddSingleton<IOntologyContextService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("CJOntology");
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OntologyOptions>>();
            var logger = sp.GetRequiredService<ILogger<OntologyContextService>>();
            return new OntologyContextService(httpClient, options, logger);
        });
    }
}
