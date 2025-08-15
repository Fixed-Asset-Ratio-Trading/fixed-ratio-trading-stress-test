using System.Text;
using FixedRatioStressTest.Abstractions;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.Gui;

/// <summary>
/// GUI-side logger that raises log events for a ListView without any platform logging side-effects.
/// </summary>
public sealed class GuiEventLogger : IEventLogger, IDisposable
{
    private readonly GuiLoggerOptions _options;
    private readonly object _fileLock = new();
    private StreamWriter? _writer;

    public GuiEventLogger(GuiLoggerOptions options)
    {
        _options = options;
        if (_options.EnableFileLogging)
        {
            EnsureLogDirectory();
            _writer = new StreamWriter(new FileStream(_options.FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true,
                NewLine = "\n"
            };
        }
    }
    /// <inheritdoc />
    public event EventHandler<LogEventArgs>? LogEntryCreated;

    public void LogInformation(string message, params object[] args)
        => Raise(LogLevel.Information, message, args);

    public void LogWarning(string message, params object[] args)
        => Raise(LogLevel.Warning, message, args);

    public void LogError(string message, Exception? exception = null, params object[] args)
        => Raise(LogLevel.Error, WithException(message, exception), args, exception);

    public void LogCritical(string message, Exception? exception = null, params object[] args)
        => Raise(LogLevel.Critical, WithException(message, exception), args, exception);

    public void LogDebug(string message, params object[] args)
        => Raise(LogLevel.Debug, message, args);

    private static string Format(string message, object[] args)
    {
        if (args is { Length: > 0 })
        {
            try { return string.Format(message, args); }
            catch { return message + " " + string.Join(" ", args.Select(a => a?.ToString())); }
        }
        return message;
    }

    private static string WithException(string message, Exception? ex)
        => ex == null ? message : message + " - " + ex.Message;

    private void Raise(LogLevel level, string message, object[] args, Exception? exception = null)
    {
        if (level < _options.MinimumLevel)
        {
            return;
        }

        var rendered = Format(message, args);

        if (_options.EnableFileLogging)
        {
            TryWriteToFile(level, rendered, exception);
        }

        if (_options.EnableUiLogging)
        {
            LogEntryCreated?.Invoke(this, new LogEventArgs
            {
                Level = level,
                Message = rendered,
                Exception = exception,
                Timestamp = DateTime.UtcNow,
                Category = "GUI"
            });
        }
    }

    private void TryWriteToFile(LogLevel level, string message, Exception? exception)
    {
        try
        {
            if (_writer == null) return;
            lock (_fileLock)
            {
                RotateIfNeeded();
                _writer.WriteLine($"{DateTime.UtcNow:O}\t{level}\t{message}");
                if (exception != null)
                {
                    _writer.WriteLine(exception.ToString());
                }
            }
        }
        catch
        {
            // Never throw from logging path
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(_options.FilePath);
            if (!fi.Exists) return;
            if (fi.Length <= _options.MaxFileSizeKB * 1024) return;

            var archive = _options.FilePath + ".1";
            if (File.Exists(archive)) File.Delete(archive);
            _writer?.Flush();
            _writer?.Dispose();
            File.Move(_options.FilePath, archive);
            _writer = new StreamWriter(new FileStream(_options.FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true,
                NewLine = "\n"
            };
        }
        catch
        {
            // best-effort rotation
        }
    }

    private void EnsureLogDirectory()
    {
        var dir = Path.GetDirectoryName(_options.FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public void Dispose()
    {
        try { _writer?.Dispose(); } catch { }
    }
}


