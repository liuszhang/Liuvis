using Serilog;
using Serilog.Events;
using FluentValidation;
using Mapster;
using MudBlazor.Services;
using Liuvis.Web.Extensions;
using Liuvis.Web.Components;

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
    // 3. MediatR
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
    // 4. FluentValidation
    // -------------------------------------------------------------------------
    builder.Services.AddValidatorsFromAssemblyContaining<Liuvis.Core.DTOs.Requests.ChatRequest>();

    // -------------------------------------------------------------------------
    // 5. Mapster
    // -------------------------------------------------------------------------
    TypeAdapterConfig.GlobalSettings.Default.NameMatchingStrategy(NameMatchingStrategy.Flexible);
    builder.Services.AddMapster();

    // -------------------------------------------------------------------------
    // 6. Controllers + Swagger
    // -------------------------------------------------------------------------
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Liuvis API",
            Version = "v1",
            Description = "AI-driven 3D Design Platform"
        });
    });

    // -------------------------------------------------------------------------
    // 7. SignalR
    // -------------------------------------------------------------------------
    builder.Services.AddSignalR();

    // -------------------------------------------------------------------------
    // 8. HttpClient
    // -------------------------------------------------------------------------
    builder.Services.AddHttpClient();

    // -------------------------------------------------------------------------
    // 8. Application Services (via extension method)
    // -------------------------------------------------------------------------
    builder.Services.AddLiuvisApplicationServices(builder.Configuration);

    var app = builder.Build();

    // -------------------------------------------------------------------------
    // Pipeline
    // -------------------------------------------------------------------------
    app.ConfigureLiuvisPipeline();

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
