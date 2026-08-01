using System.Text.Json;
using CJCore.CoreLog;
using CJCore.Modules.Data;
using Liuvis.Core.Entities;
using Liuvis.Core.Interfaces;
using Liuvis.Modules.Settings;
using Liuvis.Infrastructure.Services;
using Liuvis.Agent;
using Liuvis.KnowledgeBase.Services;
using Liuvis.Web.Hubs;
using Liuvis.Web.Middleware;
using Liuvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Liuvis.Web.Extensions;

/// <summary>
/// Centralized application pipeline configuration for Liuvis.
/// </summary>
public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigureLiuvisPipeline(this WebApplication app)
    {
        // ---------------------------------------------------------------------
        // 1. Global exception handling
        // ---------------------------------------------------------------------
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        // ---------------------------------------------------------------------
        // 2. Rate limiting
        // ---------------------------------------------------------------------
        app.UseMiddleware<RateLimitingMiddleware>();

        // ---------------------------------------------------------------------
        // 3. Standard middleware
        // ---------------------------------------------------------------------
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        else
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

        app.UseAntiforgery();

        // ---------------------------------------------------------------------
        // 4. Database initialization (before Blazor, to avoid race conditions)
        // ---------------------------------------------------------------------
        if (app.Environment.IsDevelopment())
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LiuvisDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("DatabaseInit");
            try
            {
                dbContext.Database.EnsureCreated();

                // Apply schema upgrades for existing databases
                // (EnsureCreated only creates missing tables, doesn't alter existing columns)
                ApplyDatabaseUpgrades(dbContext, logger);

                SeedSettingsFromConfig(dbContext, app.Configuration, logger);
                SettingsService.PreloadFromDb(app.Services, scope.ServiceProvider
                    .GetRequiredService<ILoggerFactory>().CreateLogger("SettingsPreload"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Database EnsureCreated failed — DB may already exist or be unreachable.");
            }
        }

        // ---------------------------------------------------------------------
        // 4.5 CJCore Data (SQLite cjcore_liuvis.db) — create tables + LLM seed.
        // Liuvis LLM 配置已统一到 CJCore ILLMConfigService；主链路 DI 工厂依赖
        // 该库的 LLM_LlmProviders / LLM_LlmModelConfigs 表及种子数据（LLMSeedDataProvider）。
        // ---------------------------------------------------------------------
        try
        {
            app.Services.EnsureDataDbCreatedAsync().GetAwaiter().GetResult();
            app.Services.RunSeedDataAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            var cjLogger = app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("CJCoreDataInit");
            cjLogger.LogWarning(ex, "CJCore data DB ensure/seed failed — LLM 配置可能不可用");
        }

        // ---------------------------------------------------------------------
        // 5. Static files
        // ---------------------------------------------------------------------
        app.UseStaticFiles();

        // Serve model files from data/models at /storage/
        var storageBasePath = Path.GetFullPath(
            app.Configuration.GetValue<string>("Storage:BasePath") ?? "./data/models");
        Directory.CreateDirectory(storageBasePath);
        app.Map("/storage/{**path}", async context =>
        {
            var relPath = context.Request.RouteValues["path"]?.ToString() ?? "";
            var filePath = Path.Combine(storageBasePath, relPath);
            if (File.Exists(filePath))
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".glb" => "model/gltf-binary",
                    ".gltf" => "model/gltf+json",
                    ".stl" => "model/stl",
                    ".step" or ".stp" => "application/step",
                    ".obj" => "text/plain",
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    _ => "application/octet-stream"
                };
                context.Response.ContentType = contentType;
                await context.Response.SendFileAsync(filePath);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        });

        // ---------------------------------------------------------------------
        // 6. Controllers (REST API)
        // ---------------------------------------------------------------------
        app.MapControllers();

        // ---------------------------------------------------------------------
        // 7. Blazor Server (interactive)
        // ---------------------------------------------------------------------
        app.MapRazorComponents<Components.App>()
            .AddInteractiveServerRenderMode();

        // ---------------------------------------------------------------------
        // 8. SignalR Hub
        // ---------------------------------------------------------------------
        app.MapHub<DesignHub>("/ws/design");

        // ---------------------------------------------------------------------
        // 9. Agent 预热 + 本体缓存手动刷新端点
        // ---------------------------------------------------------------------
        using (var warmupScope = app.Services.CreateScope())
        {
            // 强制构造 ModelingAgent，避免 InProcessAgentChannel 的 Subscribe 懒加载
            // 导致首条消息发送时订阅者为空而丢失。
            var agent = warmupScope.ServiceProvider.GetService<ModelingAgent>();
            if (agent is null)
                app.Logger.LogWarning("ModelingAgent 未注册，跳过 Agent 预热");
            else
                app.Logger.LogInformation("ModelingAgent 预热完成");

            // 阶段五：本体知识工件导入（启动预热）。
            // CJOntology 不可达时 ImportAsync 内部快速返回 0，不阻塞启动。
            try
            {
                var importer = warmupScope.ServiceProvider.GetService<OntologyKnowledgeImporter>();
                if (importer is not null)
                {
                    var imported = importer.ImportAsync().GetAwaiter().GetResult();
                    CJLog.Information($"启动预热：本体知识工件导入 {imported} 条", source: "Liuvis.Warmup");
                }
            }
            catch (Exception ex)
            {
                CJLog.Warning($"启动预热本体知识导入失败（可稍后通过 /api/admin/ontology/refresh 重试）: {ex.Message}", source: "Liuvis.Warmup");
            }
        }

        app.MapGet("/api/admin/ontology/refresh",
            async (IOntologyContextService svc, OntologyKnowledgeImporter importer, CancellationToken ct) =>
        {
            var refreshed = await svc.RefreshAsync(ct);

            // 阶段五：refresh 时同步做知识工件增量导入
            var imported = 0;
            try
            {
                imported = await importer.ImportAsync(ct);
            }
            catch (Exception ex)
            {
                CJLog.Error(ex, "refresh 端点本体知识导入失败", source: "OntologyRefresh");
            }

            return Results.Ok(new { refreshed, imported, at = DateTimeOffset.UtcNow });
        });

        return app;
    }

    /// <summary>
    /// Apply idempotent schema upgrades for columns that changed type or were added
    /// after the initial table creation. PostgreSQL-only (uses information_schema).
    /// </summary>
    private static void ApplyDatabaseUpgrades(LiuvisDbContext db, ILogger logger)
    {
        try
        {
            db.Database.ExecuteSqlRaw("""
                DO $$
                BEGIN
                    -- 1. app_settings.Value: varchar(4096) → text (removed MaxLength)
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'app_settings' AND column_name = 'Value'
                          AND data_type = 'character varying' AND character_maximum_length = 4096
                    ) THEN
                        ALTER TABLE app_settings ALTER COLUMN "Value" TYPE text;
                    END IF;

                    -- 2. knowledge_entries.Description: varchar(4096) → text
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'knowledge_entries' AND column_name = 'Description'
                          AND data_type = 'character varying' AND character_maximum_length = 4096
                    ) THEN
                        ALTER TABLE knowledge_entries ALTER COLUMN "Description" TYPE text;
                    END IF;

                    -- 3. models.Description: varchar(4096) → text
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'models' AND column_name = 'Description'
                          AND data_type = 'character varying' AND character_maximum_length = 4096
                    ) THEN
                        ALTER TABLE models ALTER COLUMN "Description" TYPE text;
                    END IF;
                END $$;
                """);
            logger.LogInformation("Database schema upgrades applied successfully");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database schema upgrades skipped (table may not exist yet)");
        }
    }

    private static void SeedSettingsFromConfig(LiuvisDbContext db, IConfiguration config, ILogger logger)
    {
        // Seed Generation settings from appsettings.json
        var genSection = config.GetSection("Liuvis:Generation");
        if (genSection.Exists() && !db.AppSettings.Any(s => s.Key == "generation_settings"))
        {
            var genSettings = genSection.Get<GenerationSettings>();
            if (genSettings is not null)
            {
                var json = JsonSerializer.Serialize(genSettings, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                db.AppSettings.Add(new AppSetting("generation_settings", json, "Generation configuration"));
                db.SaveChanges();
                logger.LogInformation("Seeded generation_settings from appsettings.json into database");
            }
        }
    }
}
