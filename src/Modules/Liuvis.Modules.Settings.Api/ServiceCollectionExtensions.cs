using CJCore.Modules.Data;
using Liuvis.Modules.Settings;
using Liuvis.Infrastructure.Services;
using Liuvis.Modules.Settings.Api.Apis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Liuvis.Modules.Settings.Api;

/// <summary>
/// Liuvis Settings 模块的 DI 注册与端点映射扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Settings 后端服务（ISettingsService 实现）和模块数据库配置。
    /// </summary>
    public static IServiceCollection AddLiuvisSettingsApi(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IModuleDbConfig, SettingsModuleDbConfig>();
        return services;
    }

    /// <summary>
    /// 映射 Settings 模块的 Minimal API 端点。
    /// </summary>
    public static IEndpointRouteBuilder MapLiuvisSettingsApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSettingsApi();
        return endpoints;
    }
}
