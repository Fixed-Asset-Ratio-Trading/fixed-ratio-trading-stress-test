using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using FixedRatioStressTest.Abstractions;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.WindowsService;

public sealed class UdpEventLogger : IEventLogger, IDisposable
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _endpoint;

    public event EventHandler<LogEventArgs>? LogEntryCreated;

    public UdpEventLogger(string host = "127.0.0.1", int port = 51999)
    {
        _client = new UdpClient();
        _endpoint = new IPEndPoint(IPAddress.Parse(host), port);
    }

    public void LogInformation(string message, params object[] args) => Send(LogLevel.Information, message, null, args);
    public void LogWarning(string message, params object[] args) => Send(LogLevel.Warning, message, null, args);
    public void LogDebug(string message, params object[] args) => Send(LogLevel.Debug, message, null, args);
    public void LogError(string message, Exception? exception = null, params object[] args) => Send(LogLevel.Error, message, exception, args);
    public void LogCritical(string message, Exception? exception = null, params object[] args) => Send(LogLevel.Critical, message, exception, args);

    private void Send(LogLevel level, string message, Exception? ex, params object[] args)
    {
        try
        {
            string rendered;
            try { rendered = args is { Length: > 0 } ? string.Format(message, args) : message; }
            catch { rendered = message + (args.Length > 0 ? (" " + string.Join(' ', args)) : string.Empty); }

            var payload = new
            {
                level = level.ToString(),
                message = rendered,
                exception = ex?.ToString(),
                timestamp = DateTime.UtcNow,
                category = "API"
            };
            var json = JsonSerializer.Serialize(payload);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            _client.Send(bytes, bytes.Length, _endpoint);

            // Also raise locally in case anything is subscribed in-process
            LogEntryCreated?.Invoke(this, new LogEventArgs
            {
                Level = level,
                Message = rendered,
                Exception = ex,
                Timestamp = DateTime.UtcNow,
                Category = "API"
            });
        }
        catch
        {
            // Never throw from logging path
        }
    }

    public void Dispose()
    {
        try { _client?.Dispose(); } catch { }
    }
}


