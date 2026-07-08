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

        // ---- LLM Settings ----
        api.MapGet("/llm", async (
            ISettingsService service,
            CancellationToken ct) =>
            await service.GetLlmSettingsAsync(ct))
        .WithName("GetLlmSettings")
        .WithDescription("获取 LLM 设置");

        api.MapPost("/llm", async (
            [FromBody] LlmSettings settings,
            ISettingsService service,
            CancellationToken ct) =>
        {
            await service.SaveLlmSettingsAsync(settings, ct);
            return Results.Ok();
        })
        .WithName("SaveLlmSettings")
        .WithDescription("保存 LLM 设置");

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

        // ---- Multi-Provider Management ----
        api.MapGet("/providers", async (
            ISettingsService service,
            CancellationToken ct) =>
            await service.GetProvidersAsync(ct))
        .WithName("GetProviders")
        .WithDescription("获取所有 LLM 供应商");

        api.MapGet("/providers/active", async (
            ISettingsService service,
            CancellationToken ct) =>
        {
            var result = await service.GetActiveProviderAsync(ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetActiveProvider")
        .WithDescription("获取当前激活的供应商");

        api.MapPost("/providers", async (
            [FromBody] LlmProvider provider,
            ISettingsService service,
            CancellationToken ct) =>
        {
            var result = await service.AddProviderAsync(provider, ct);
            return Results.Created($"/api/settings/providers/{result.Id}", result);
        })
        .WithName("AddProvider")
        .WithDescription("添加 LLM 供应商");

        api.MapPut("/providers", async (
            [FromBody] LlmProvider provider,
            ISettingsService service,
            CancellationToken ct) =>
        {
            await service.UpdateProviderAsync(provider, ct);
            return Results.Ok();
        })
        .WithName("UpdateProvider")
        .WithDescription("更新 LLM 供应商");

        api.MapDelete("/providers/{id}", async (
            int id,
            ISettingsService service,
            CancellationToken ct) =>
        {
            await service.DeleteProviderAsync(id, ct);
            return Results.Ok();
        })
        .WithName("DeleteProvider")
        .WithDescription("删除 LLM 供应商");

        api.MapPost("/providers/{id}/activate", async (
            int id,
            ISettingsService service,
            CancellationToken ct) =>
        {
            await service.ActivateProviderAsync(id, ct);
            return Results.Ok();
        })
        .WithName("ActivateProvider")
        .WithDescription("激活指定供应商");

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
