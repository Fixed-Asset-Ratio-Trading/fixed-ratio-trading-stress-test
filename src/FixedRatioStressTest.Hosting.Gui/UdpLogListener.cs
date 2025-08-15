using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.Gui;

public sealed class UdpLogListener : IDisposable
{
    private readonly GuiEventLogger _guiLogger;
    private readonly UdpClient _server;
    private readonly CancellationTokenSource _cts = new();

    public UdpLogListener(GuiEventLogger guiLogger, int port = 51999)
    {
        _guiLogger = guiLogger;
        _server = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
    }

    public void Start()
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _server.ReceiveAsync(_cts.Token);
                    var json = System.Text.Encoding.UTF8.GetString(result.Buffer);
                    var dto = JsonSerializer.Deserialize<IncomingLogDto>(json);
                    if (dto != null)
                    {
                        var level = ParseLevel(dto.level);
                        var message = dto.message ?? string.Empty;
                        switch (level)
                        {
                            case LogLevel.Critical: _guiLogger.LogCritical(message); break;
                            case LogLevel.Error: _guiLogger.LogError(message); break;
                            case LogLevel.Warning: _guiLogger.LogWarning(message); break;
                            case LogLevel.Debug: _guiLogger.LogDebug(message); break;
                            default: _guiLogger.LogInformation(message); break;
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
            }
        });
    }

    private static LogLevel ParseLevel(string? level)
    {
        if (Enum.TryParse<LogLevel>(level, out var parsed)) return parsed;
        return LogLevel.Information;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _server.Dispose(); } catch { }
        _cts.Dispose();
    }

    private sealed class IncomingLogDto
    {
        public string? level { get; set; }
        public string? message { get; set; }
        public string? exception { get; set; }
        public DateTime timestamp { get; set; }
        public string? category { get; set; }
    }
}


