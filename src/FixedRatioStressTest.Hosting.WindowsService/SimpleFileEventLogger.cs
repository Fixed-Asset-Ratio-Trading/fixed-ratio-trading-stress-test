using FixedRatioStressTest.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FixedRatioStressTest.Hosting.WindowsService;

public sealed class SimpleFileEventLogger : IEventLogger, IDisposable
{
    private readonly string _filePath;
    private readonly object _sync = new object();
    private readonly StreamWriter _writer;

    public event EventHandler<LogEventArgs>? LogEntryCreated;

    public SimpleFileEventLogger(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _writer = new StreamWriter(new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
            NewLine = Environment.NewLine
        };
    }

    public void LogInformation(string message, params object[] args) => Write(LogLevel.Information, message, null, args);
    public void LogWarning(string message, params object[] args) => Write(LogLevel.Warning, message, null, args);
    public void LogDebug(string message, params object[] args) => Write(LogLevel.Debug, message, null, args);
    public void LogError(string message, Exception? exception = null, params object[] args) => Write(LogLevel.Error, message, exception, args);
    public void LogCritical(string message, Exception? exception = null, params object[] args) => Write(LogLevel.Critical, message, exception, args);

    private void Write(LogLevel level, string message, Exception? ex, params object[] args)
    {
        string line = FormatLine(level, message, ex, args);
        lock (_sync)
        {
            _writer.WriteLine(line);
        }
        LogEntryCreated?.Invoke(this, new LogEventArgs
        {
            Level = level,
            Message = line,
            Exception = ex,
            Timestamp = DateTime.UtcNow,
            Category = "File"
        });
    }

    private static string FormatLine(LogLevel level, string message, Exception? ex, object[] args)
    {
        string rendered;
        try { rendered = args is { Length: > 0 } ? string.Format(message, args) : message; }
        catch { rendered = message + (args.Length > 0 ? (" " + string.Join(' ', args)) : string.Empty); }

        var sb = new StringBuilder();
        sb.Append(DateTime.UtcNow.ToString("o"));
        sb.Append('\t').Append(level.ToString());
        sb.Append('\t').Append(rendered);
        if (ex != null)
        {
            sb.Append("\n").Append(ex);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Dispose();
        }
    }
}


