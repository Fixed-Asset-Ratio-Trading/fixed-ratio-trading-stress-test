using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FixedRatioStressTest.Logging.Providers;

namespace FixedRatioStressTest.Logging.Transport;

/// <summary>
/// Logger provider that sends log messages over UDP.
/// </summary>
public class UdpLoggerProvider : BaseLoggerProvider
{
    private readonly UdpLoggerOptions _options;
    private readonly UdpClient? _udpClient;
    private readonly IPEndPoint? _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpLoggerProvider"/> class.
    /// </summary>
    /// <param name="options">The logger options.</param>
    public UdpLoggerProvider(IOptions<UdpLoggerOptions> options) : this(options.Value)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpLoggerProvider"/> class.
    /// </summary>
    /// <param name="options">The logger options.</param>
    public UdpLoggerProvider(UdpLoggerOptions options)
    {
        _options = options;

        try
        {
            var parts = _options.Endpoint.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var port))
            {
                _udpClient = new UdpClient();
                _endpoint = new IPEndPoint(IPAddress.Parse(parts[0]), port);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize UDP logger: {ex.Message}");
        }
    }

    /// <inheritdoc />
    protected override ILogger CreateLoggerImplementation(string categoryName)
    {
        return new UdpLogger(categoryName, _options, _udpClient, _endpoint);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _udpClient?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Configuration options for UDP logger.
/// </summary>
public class UdpLoggerOptions
{
    /// <summary>
    /// Gets or sets the UDP endpoint in format "host:port".
    /// </summary>
    public string Endpoint { get; set; } = "127.0.0.1:12345";

    /// <summary>
    /// Gets or sets the source identifier for log messages.
    /// </summary>
    public string Source { get; set; } = "UdpLogger";

    /// <summary>
    /// Gets or sets the minimum log level.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;
}

/// <summary>
/// Logger implementation that sends messages over UDP.
/// </summary>
internal class UdpLogger : ILogger
{
    private readonly string _categoryName;
    private readonly UdpLoggerOptions _options;
    private readonly UdpClient? _udpClient;
    private readonly IPEndPoint? _endpoint;

    public UdpLogger(string categoryName, UdpLoggerOptions options, UdpClient? udpClient, IPEndPoint? endpoint)
    {
        _categoryName = categoryName;
        _options = options;
        _udpClient = udpClient;
        _endpoint = endpoint;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _options.MinimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel) || _udpClient == null || _endpoint == null)
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
            return;

        try
        {
            var udpMessage = new UdpLogMessage
            {
                Timestamp = DateTime.Now,
                Level = logLevel,
                Category = _categoryName,
                EventId = eventId.Id,
                Message = message,
                Exception = exception?.ToString(),
                Source = _options.Source
            };

            var json = JsonSerializer.Serialize(udpMessage);
            var bytes = Encoding.UTF8.GetBytes(json);
            
            _udpClient.Send(bytes, bytes.Length, _endpoint);
        }
        catch
        {
            // Ignore UDP send failures
        }
    }
}
