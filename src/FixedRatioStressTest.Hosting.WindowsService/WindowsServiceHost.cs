using FixedRatioStressTest.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.WindowsService;

/// <summary>
/// Windows Service-style host which runs the <see cref="IServiceLifecycle"/> engine
/// inside a <see cref="BackgroundService"/>.
/// </summary>
public sealed class WindowsServiceHost : BackgroundService, IServiceHost
{
    private readonly IServiceLifecycle _engine;
    private readonly ILogger<WindowsServiceHost> _logger;

    /// <inheritdoc />
    public string HostType => "WindowsService";

    public WindowsServiceHost(IServiceLifecycle engine, ILogger<WindowsServiceHost> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Windows Service Host initializing");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken)
    {
        // Delegate to ExecuteAsync in BackgroundService.
        return ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// BackgroundService entry point used by the Generic Host runtime.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _engine.StartAsync(stoppingToken);

            // Keep the service alive until a stop is requested.
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Windows Service Host crashed");
            throw;
        }
        finally
        {
            await _engine.StopAsync();
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Windows Service Host shutting down");
        await _engine.StopAsync(cancellationToken);
    }
}


