using Serilog;
using Serilog.Events;
using FluentValidation;
using Mapster;
using MudBlazor.Services;
using Liuvis.Web.Extensions;
using Liuvis.Web.Components;
using CJCore.Framework.Api;
using CJCore.Framework.Abstractions;
using Liuvis.Web.Services;
using Liuvis.Modules.Settings.Api;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/liuvis-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // -------------------------------------------------------------------------
    // 1. Blazor Server (Interactive)
    // -------------------------------------------------------------------------
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // -------------------------------------------------------------------------
    // 2. MudBlazor
    // -------------------------------------------------------------------------
    builder.Services.AddMudServices();

    // -------------------------------------------------------------------------
    // 3. CJCore Framework（主题 + AppBar + 模块发现 + 日志）
    // -------------------------------------------------------------------------
    builder.Services.AddCJCoreFramework(builder.Configuration, options =>
    {
        options.ProductName = "Liuvis Studio";
        options.EnableThemeSwitcher = true;
        options.EnableLogDrawer = true;
        options.BaseUrl = builder.Configuration["BaseUrl"];
        options.Theme = new ThemeOptions
        {
            Light = new PaletteColorOptions
            {
                Primary = "#00d4ff",
                AppbarBackground = "#111827",
                AppbarText = "#e2e8f0",
                Background = "#0a0e1a",
                Surface = "#111827",
            },
            Dark = new PaletteColorOptions
            {
                Primary = "#00d4ff",
                Secondary = "#7c3aed",
                Tertiary = "#00ff88",
                Background = "#0a0e1a",
                Surface = "#111827",
                AppbarBackground = "#111827",
                DrawerBackground = "#111827",
                DrawerText = "#e2e8f0",
                TextPrimary = "#e2e8f0",
                TextSecondary = "#94a3b8",
                ActionDefault = "#00d4ff",
                ActionDisabled = "#4a5568",
                Divider = "rgba(0, 212, 255, 0.15)",
                TableLines = "rgba(0, 212, 255, 0.15)",
                DrawerIcon = "#00d4ff",
                Info = "#00d4ff",
                Success = "#00ff88",
                Warning = "#f59e0b",
                Error = "#ef4444",
            }
        };
    });

    // -------------------------------------------------------------------------
    // 4. MediatR
    // -------------------------------------------------------------------------
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblies(
            typeof(Liuvis.Core.Events.ModelGeneratedEvent).Assembly,
            typeof(Liuvis.NLU.Services.NluService).Assembly,
            typeof(Liuvis.Design.Services.DesignEngine).Assembly,
            typeof(Liuvis.Generation.Services.ModelGenerator).Assembly,
            typeof(Liuvis.Modification.Services.ModificationEngine).Assembly);
    });

    // -------------------------------------------------------------------------
    // 5. FluentValidation
    // -------------------------------------------------------------------------
    builder.Services.AddValidatorsFromAssemblyContaining<Liuvis.Core.DTOs.Requests.ChatRequest>();

    // -------------------------------------------------------------------------
    // 6. Mapster
    // -------------------------------------------------------------------------
    TypeAdapterConfig.GlobalSettings.Default.NameMatchingStrategy(NameMatchingStrategy.Flexible);
    builder.Services.AddMapster();

    // -------------------------------------------------------------------------
    // 7. Controllers + Swagger
    // -------------------------------------------------------------------------
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // -------------------------------------------------------------------------
    // 8. SignalR
    // -------------------------------------------------------------------------
    builder.Services.AddSignalR();

    // -------------------------------------------------------------------------
    // 9. HttpClient
    // -------------------------------------------------------------------------
    builder.Services.AddHttpClient();

    // -------------------------------------------------------------------------
    // 10. Settings 模块（后端 API + 服务注册）
    // -------------------------------------------------------------------------
    builder.Services.AddLiuvisSettingsApi();

    // -------------------------------------------------------------------------
    // 11. Application Services
    // -------------------------------------------------------------------------
    builder.Services.AddLiuvisApplicationServices(builder.Configuration);

    // -------------------------------------------------------------------------
    // 12. Liuvis 模块注册（菜单 + 程序集发现）
    // -------------------------------------------------------------------------
    builder.Services.AddSingleton<IModule, LiuvisModule>();
    builder.Services.AddSingleton<IModule, Liuvis.Modules.Settings.UI.LiuvisSettingsModule>();
    builder.Services.AddSingleton<IMenuService, LiuvisMenuService>();

    var app = builder.Build();

    // -------------------------------------------------------------------------
    // Pipeline
    // -------------------------------------------------------------------------
    app.ConfigureLiuvisPipeline();

    // 映射 Settings 模块 Minimal API 端点
    app.MapLiuvisSettingsApi();

    Log.Information("Liuvis starting up...");
    Log.Information("Listening on {Urls}", string.Join(", ", app.Urls));
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
