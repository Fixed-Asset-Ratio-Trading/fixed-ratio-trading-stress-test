using FixedRatioStressTest.Abstractions;
using FixedRatioStressTest.Core;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using FixedRatioStressTest.Core.Threading;
using FixedRatioStressTest.Infrastructure.Services;
using FixedRatioStressTest.Web.Services;
using FixedRatioStressTest.Logging;
using FixedRatioStressTest.Logging.WindowsService;
using FixedRatioStressTest.Logging.Transport;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using FixedRatioStressTest.Web.Middleware;
using System.Reflection;

// Apply Windows performance optimizations for 32-core Threadripper
if (OperatingSystem.IsWindows())
{
    WindowsPerformanceOptimizer.OptimizeForThreadripper();
}

var builder = WebApplication.CreateBuilder(args);

// Configure logging with new unified providers
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

// TEMP (dev): Force storage to use repo root .\\data regardless of working dir
// NOTE: This override will be removed for production.
try
{
    var rootData = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../data"));
    if (!Directory.Exists(rootData))
    {
        rootData = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "data"));
    }
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
    {
        ["Storage:DataDirectory"] = rootData
    });
}
catch { }

// Add Windows Service logger provider (Event Log + File + UDP)
builder.Logging.AddWindowsServiceLogger(options =>
{
    options.EventLogSource = "FixedRatioStressTest";
    options.EnableFileLogging = true;
    options.FileLogging.Path = "logs/api.log";
    options.FileLogging.MaxSizeKB = 10240;
    options.EnableUdpTransport = true;
    options.UdpTransport.Endpoint = "127.0.0.1:12345";
    options.UdpTransport.Source = "API";
});

// Optionally add console logger for debugging
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddConsole();
}

// Configure Kestrel for network accessibility
builder.WebHost.ConfigureKestrel(options =>
{
    var bindAddress = builder.Configuration.GetValue<string>("NetworkConfiguration:BindAddress") ?? "0.0.0.0";
    var httpPort = builder.Configuration.GetValue<int>("NetworkConfiguration:HttpPort", 8080);
    
    options.ListenAnyIP(httpPort);
    options.Limits.MaxConcurrentConnections = 200;
    options.Limits.MaxConcurrentUpgradedConnections = 200;
});

// Add services to the container.
builder.Services.AddControllers()
    .AddApplicationPart(Assembly.Load("FixedRatioStressTest.Web"));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS for network accessibility
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add response compression for better network performance
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Optimal;
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<ThreadHealthCheckService>("threads", tags: new[] { "ready" });

// Register Phase 4 services - Threading and Performance
builder.Services.AddSingleton<ThreadripperOptimizedThreadPool>();
builder.Services.AddSingleton(typeof(HighPerformanceObjectPool<>));

// Register core application services
builder.Services.AddSingleton<IStorageService, JsonFileStorageService>();
builder.Services.AddSingleton<IComputeUnitManager, ComputeUnitManager>();
builder.Services.AddSingleton<ISolanaClientService, SolanaClientService>();
builder.Services.AddSingleton<ITransactionBuilderService, TransactionBuilderService>();
builder.Services.AddSingleton<IContractVersionService, RawRpcContractVersionService>(); // Raw RPC validation (depends on SolanaClientService)
builder.Services.AddSingleton<IThreadManager>(sp => new ThreadManager(
    sp.GetRequiredService<IStorageService>(),
    sp.GetRequiredService<ISolanaClientService>(),
    sp.GetRequiredService<ITransactionBuilderService>(),
    sp.GetRequiredService<IContractErrorHandler>(),
    sp.GetRequiredService<ISystemStateService>(),
    sp.GetRequiredService<ILogger<ThreadManager>>()));
builder.Services.AddSingleton<IEmptyCommandHandler, EmptyCommandHandler>();

// Remove legacy IEventLogger adapter (fully migrated to ILogger<T>)

// Register engine with new lifecycle management
builder.Services.AddSingleton<IServiceLifecycle, ServiceLifecycleManager>();
builder.Services.AddSingleton<ISystemStateService, SystemStateService>();

// Add background services - orchestrated by the engine instead of the ASP.NET host
// NOTE: Performance monitor remains as a host background service if desired
builder.Services.AddHostedService<PerformanceMonitorService>();

// Configure Windows Service support (if needed)
if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "Fixed Ratio Stress Test Service";
    });
}

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Add custom request logging middleware
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapControllers();

// Map health check endpoints
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // Basic liveness check, no dependencies
});

// Log startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Fixed Ratio Stress Test Service started with 32-core optimization");
logger.LogInformation("Listening on port {Port}", builder.Configuration.GetValue<int>("NetworkConfiguration:HttpPort", 8080));

app.Run();

// All logging now goes through ILogger<T> and custom providers