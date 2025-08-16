using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FixedRatioStressTest.Logging.Models;

namespace FixedRatioStressTest.Logging.Transport;

/// <summary>
/// Background service that listens for UDP log messages and injects them into the local logging system.
/// </summary>
public class UdpLogListenerService : BackgroundService
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly UdpLogListenerOptions _options;
    private readonly ILogger<UdpLogListenerService> _logger;
    private UdpClient? _udpListener;

    /// <summary>
    /// Event raised when a UDP log message is received.
    /// </summary>
    public event EventHandler<LogMessageEventArgs>? LogMessageReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpLogListenerService"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory for creating loggers.</param>
    /// <param name="options">The listener options.</param>
    /// <param name="logger">The logger for this service.</param>
    public UdpLogListenerService(
        ILoggerFactory loggerFactory,
        IOptions<UdpLogListenerOptions> options,
        ILogger<UdpLogListenerService> logger)
    {
        _loggerFactory = loggerFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("UDP log listener is disabled");
            return;
        }

        try
        {
            _udpListener = new UdpClient(_options.Port);
            _logger.LogInformation("UDP log listener started on port {Port}", _options.Port);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var receiveTask = _udpListener.ReceiveAsync();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var completedTask = await Task.WhenAny(receiveTask, Task.Delay(Timeout.Infinite, cts.Token));

                    if (completedTask == receiveTask && receiveTask.IsCompletedSuccessfully)
                    {
                        var result = receiveTask.Result;
                        ProcessMessage(result.Buffer);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Expected when shutting down
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error receiving UDP message");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start UDP listener");
        }
    }

    private void ProcessMessage(byte[] buffer)
    {
        try
        {
            var json = Encoding.UTF8.GetString(buffer);
            var udpMessage = JsonSerializer.Deserialize<UdpLogMessage>(json);
            
            if (udpMessage == null)
                return;

            // Raise event for direct consumers (like GUI)
            var eventArgs = new LogMessageEventArgs
            {
                Timestamp = udpMessage.Timestamp,
                Level = udpMessage.Level,
                Category = udpMessage.Category,
                Message = udpMessage.Message,
                Exception = string.IsNullOrEmpty(udpMessage.Exception) ? null : new Exception(udpMessage.Exception),
                EventId = new EventId(udpMessage.EventId),
                Source = udpMessage.Source
            };

            LogMessageReceived?.Invoke(this, eventArgs);

            // Optionally inject into local logging system if configured
            if (_options.InjectIntoLocalLogging)
            {
                var logger = _loggerFactory.CreateLogger(udpMessage.Category);
                var eventId = new EventId(udpMessage.EventId);

                switch (udpMessage.Level)
                {
                    case LogLevel.Trace:
                        logger.LogTrace(eventId, udpMessage.Message);
                        break;
                    case LogLevel.Debug:
                        logger.LogDebug(eventId, udpMessage.Message);
                        break;
                    case LogLevel.Information:
                        logger.LogInformation(eventId, udpMessage.Message);
                        break;
                    case LogLevel.Warning:
                        logger.LogWarning(eventId, udpMessage.Message);
                        break;
                    case LogLevel.Error:
                        logger.LogError(eventId, udpMessage.Message);
                        break;
                    case LogLevel.Critical:
                        logger.LogCritical(eventId, udpMessage.Message);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing UDP log message");
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _udpListener?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Configuration options for UDP log listener.
/// </summary>
public class UdpLogListenerOptions
{
    /// <summary>
    /// Gets or sets whether the listener is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the port to listen on.
    /// </summary>
    public int Port { get; set; } = 12345;

    /// <summary>
    /// Gets or sets whether to inject received messages into the local logging system.
    /// </summary>
    public bool InjectIntoLocalLogging { get; set; } = false;
}
