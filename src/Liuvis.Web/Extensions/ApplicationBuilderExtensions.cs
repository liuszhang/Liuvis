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
        app.MapStaticAssets();

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
}
