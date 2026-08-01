using Liuvis.Modules.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Liuvis.Modules.Settings.Api.Apis;

/// <summary>
/// Settings Minimal API endpoints — 将 ISettingsService 的后端操作暴露为 HTTP API。
/// </summary>
public static class SettingsApi
{
    public static IEndpointRouteBuilder MapSettingsApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/settings").WithTags("Settings");

        // ---- Generation Settings ----
        api.MapGet("/generation", async (
            ISettingsService service,
            CancellationToken ct) =>
            await service.GetGenerationSettingsAsync(ct))
        .WithName("GetGenerationSettings")
        .WithDescription("获取模型生成设置");

        api.MapPost("/generation", async (
            [FromBody] GenerationSettings settings,
            ISettingsService service,
            CancellationToken ct) =>
        {
            await service.SaveGenerationSettingsAsync(settings, ct);
            return Results.Ok();
        })
        .WithName("SaveGenerationSettings")
        .WithDescription("保存模型生成设置");

        // ---- Prompt Management ----
        api.MapGet("/prompts", async (
            ISettingsService service,
            CancellationToken ct) =>
            await service.GetPromptSettingsAsync(ct))
        .WithName("GetPromptSettings")
        .WithDescription("获取 Prompt 设置");

        api.MapPost("/prompts", async (
            [FromBody] PromptSettings settings,
            ISettingsService service,
            CancellationToken ct) =>
        {
            await service.SavePromptSettingsAsync(settings, ct);
            return Results.Ok();
        })
        .WithName("SavePromptSettings")
        .WithDescription("保存 Prompt 设置");

        api.MapPost("/prompts/reset", async (
            ISettingsService service,
            CancellationToken ct) =>
        {
            await service.ResetPromptSettingsAsync(ct);
            return Results.Ok();
        })
        .WithName("ResetPromptSettings")
        .WithDescription("重置 Prompt 为默认值");

        // ---- Tools & Skills Management ----
        api.MapGet("/tools", async (
            ISettingsService service,
            CancellationToken ct) =>
            await service.GetToolsSettingsAsync(ct))
        .WithName("GetToolsSettings")
        .WithDescription("获取工具与技能设置");

        api.MapPost("/tools", async (
            [FromBody] ToolsSettings settings,
            ISettingsService service,
            CancellationToken ct) =>
        {
            await service.SaveToolsSettingsAsync(settings, ct);
            return Results.Ok();
        })
        .WithName("SaveToolsSettings")
        .WithDescription("保存工具与技能设置");

        return app;
    }
}
