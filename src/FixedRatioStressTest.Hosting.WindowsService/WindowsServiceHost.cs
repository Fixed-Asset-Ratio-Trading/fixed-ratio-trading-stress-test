using FixedRatioStressTest.Abstractions;
using Microsoft.Extensions.Hosting;

namespace FixedRatioStressTest.Hosting.WindowsService;

/// <summary>
/// Windows Service-style host which runs the <see cref="IServiceLifecycle"/> engine
/// inside a <see cref="BackgroundService"/>.
/// </summary>
public sealed class WindowsServiceHost : BackgroundService, IServiceHost
{
    private readonly IServiceLifecycle _engine;
    private readonly IEventLogger _eventLogger;

    /// <inheritdoc />
    public string HostType => "WindowsService";

    public WindowsServiceHost(IServiceLifecycle engine, IEventLogger eventLogger)
    {
        _engine = engine;
        _eventLogger = eventLogger;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _eventLogger.LogInformation("Windows Service Host initializing");
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
            _eventLogger.LogCritical("Windows Service Host crashed", ex);
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
        _eventLogger.LogInformation("Windows Service Host shutting down");
        await _engine.StopAsync(cancellationToken);
    }
}


