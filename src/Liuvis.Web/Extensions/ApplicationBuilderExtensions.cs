using System.Text.Json;
using Liuvis.Core.Entities;
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
            try
            {
                dbContext.Database.EnsureCreated();

                // Ensure llm_providers table exists (EnsureCreated won't add tables to an existing DB)
                dbContext.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS llm_providers (
                        "Id" SERIAL PRIMARY KEY,
                        "Name" VARCHAR(128) NOT NULL,
                        "Provider" VARCHAR(32) NOT NULL,
                        "ApiKey" VARCHAR(512),
                        "BaseUrl" VARCHAR(512),
                        "Model" VARCHAR(128),
                        "OllamaUrl" VARCHAR(512),
                        "OllamaModel" VARCHAR(128),
                        "MaxTokens" INTEGER NOT NULL DEFAULT 2000,
                        "Temperature" DOUBLE PRECISION NOT NULL DEFAULT 0.3,
                        "IsActive" BOOLEAN NOT NULL DEFAULT FALSE,
                        "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
                    );
                    """);

                SeedSettingsFromConfig(dbContext, app.Configuration, scope.ServiceProvider
                    .GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInit"));
                SettingsService.PreloadFromDb(app.Services, scope.ServiceProvider
                    .GetRequiredService<ILoggerFactory>().CreateLogger("SettingsPreload"));
            }
            catch (Exception ex)
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("DatabaseInit");
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

    private static void SeedSettingsFromConfig(LiuvisDbContext db, IConfiguration config, ILogger logger)
    {
        // Seed Generation settings from appsettings.json
        var genSection = config.GetSection("Liuvis:Generation");
        if (genSection.Exists() && !db.AppSettings.Any(s => s.Key == "generation_settings"))
        {
            var genSettings = genSection.Get<Core.Interfaces.GenerationSettings>();
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
        if (!db.LlmProviders.Any())
        {
            // Try to migrate from legacy app_settings
            var legacyEntity = db.AppSettings.Find("llm_settings");
            if (legacyEntity?.Value is not null)
            {
                try
                {
                    var legacy = JsonSerializer.Deserialize<Core.Interfaces.LlmSettings>(legacyEntity.Value,
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
                            MaxTokens = legacy.MaxTokens,
                            Temperature = legacy.Temperature,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        db.LlmProviders.Add(provider);
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
            if (!db.LlmProviders.Any())
            {
                var llmSection = config.GetSection("Liuvis:Llm");
                if (llmSection.Exists())
                {
                    var llmSettings = llmSection.Get<Core.Interfaces.LlmSettings>();
                    if (llmSettings is not null)
                    {
                        db.LlmProviders.Add(new LlmProvider
                        {
                            Name = "Default",
                            Provider = llmSettings.Provider,
                            ApiKey = llmSettings.OpenAIApiKey,
                            BaseUrl = llmSettings.OpenAIBaseUrl,
                            Model = llmSettings.OpenAIModel,
                            OllamaUrl = llmSettings.OllamaUrl,
                            OllamaModel = llmSettings.OllamaModel,
                            MaxTokens = llmSettings.MaxTokens,
                            Temperature = llmSettings.Temperature,
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
