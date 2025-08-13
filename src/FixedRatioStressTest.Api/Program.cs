using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using FixedRatioStressTest.Core.Threading;
using FixedRatioStressTest.Infrastructure.Services;
using FixedRatioStressTest.Api.Services;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

// Apply Windows performance optimizations for 32-core Threadripper
if (OperatingSystem.IsWindows())
{
    WindowsPerformanceOptimizer.OptimizeForThreadripper();
}

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddControllers();
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
builder.Services.AddSingleton<ISolanaClientService, SolanaClientService>();
builder.Services.AddSingleton<ITransactionBuilderService, TransactionBuilderService>();
builder.Services.AddSingleton<IThreadManager, ThreadManager>();

// Add background services
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

app.MapControllers();

// Map health check endpoints
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});

// Log startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Fixed Ratio Stress Test Service started with 32-core optimization");
logger.LogInformation("Listening on port {Port}", builder.Configuration.GetValue<int>("NetworkConfiguration:HttpPort", 8080));

app.Run();
