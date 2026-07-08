using System.Text.Json;
using Liuvis.Core.Entities;
using Liuvis.Modules.Settings;
using Liuvis.Infrastructure.Services;
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
                    -- 1. llm_providers: add SystemPrompt column (new since entity refactor)
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'llm_providers' AND column_name = 'SystemPrompt'
                    ) THEN
                        ALTER TABLE llm_providers ADD COLUMN "SystemPrompt" text;
                    END IF;

                    -- 2. app_settings.Value: varchar(4096) → text (removed MaxLength)
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'app_settings' AND column_name = 'Value'
                          AND data_type = 'character varying' AND character_maximum_length = 4096
                    ) THEN
                        ALTER TABLE app_settings ALTER COLUMN "Value" TYPE text;
                    END IF;

                    -- 3. knowledge_entries.Description: varchar(4096) → text
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'knowledge_entries' AND column_name = 'Description'
                          AND data_type = 'character varying' AND character_maximum_length = 4096
                    ) THEN
                        ALTER TABLE knowledge_entries ALTER COLUMN "Description" TYPE text;
                    END IF;

                    -- 4. models.Description: varchar(4096) → text
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

        // Migrate existing llm_settings from app_settings into llm_providers table
        if (!db.Set<LlmProvider>().Any())
        {
            // Try to migrate from legacy app_settings
            var legacyEntity = db.AppSettings.Find("llm_settings");
            if (legacyEntity?.Value is not null)
            {
                try
                {
                    var legacy = JsonSerializer.Deserialize<LlmSettings>(legacyEntity.Value,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (legacy is not null)
                    {
                        var provider = new LlmProvider
                        {
                            Name = "Migrated",
                            Provider = legacy.Provider,
                            ApiKey = legacy.OpenAIApiKey,
                            BaseUrl = legacy.OpenAIBaseUrl,
                            Model = legacy.OpenAIModel,
                            OllamaUrl = legacy.OllamaUrl,
                            OllamaModel = legacy.OllamaModel,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        db.Set<LlmProvider>().Add(provider);
                        db.SaveChanges();
                        logger.LogInformation("Migrated llm_settings from app_settings into llm_providers");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to migrate llm_settings, seeding from appsettings.json");
                }
            }

            // Fallback: seed from appsettings.json if migration didn't produce a provider
            if (!db.Set<LlmProvider>().Any())
            {
                var llmSection = config.GetSection("Liuvis:Llm");
                if (llmSection.Exists())
                {
                    var llmSettings = llmSection.Get<LlmSettings>();
                    if (llmSettings is not null)
                    {
                        db.Set<LlmProvider>().Add(new LlmProvider
                        {
                            Name = "Default",
                            Provider = llmSettings.Provider,
                            ApiKey = llmSettings.OpenAIApiKey,
                            BaseUrl = llmSettings.OpenAIBaseUrl,
                            Model = llmSettings.OpenAIModel,
                            OllamaUrl = llmSettings.OllamaUrl,
                            OllamaModel = llmSettings.OllamaModel,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        });
                        db.SaveChanges();
                        logger.LogInformation("Seeded default LLM provider from appsettings.json");
                    }
                }
            }
        }
    }
}
