using System.Diagnostics;
using FixedRatioStressTest.Abstractions;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.Gui;

public sealed class EventLogListener : IDisposable
{
    private readonly GuiEventLogger _guiLogger;
    private readonly string _sourceName;
    private EventLog? _eventLog;

    public EventLogListener(GuiEventLogger guiLogger, string sourceName = "Fixed Ratio Stress Test")
    {
        _guiLogger = guiLogger;
        _sourceName = sourceName;
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            _eventLog = new EventLog("Application");
            _eventLog.EnableRaisingEvents = true;
            _eventLog.EntryWritten += OnEntryWritten;
        }
        catch
        {
            // ignore listener failures
        }
    }

    private void OnEntryWritten(object? sender, EntryWrittenEventArgs e)
    {
        try
        {
            var entry = e.Entry;
            if (entry == null) return;
            if (!string.Equals(entry.Source, _sourceName, StringComparison.OrdinalIgnoreCase)) return;

            // Map EventLogEntryType to LogLevel
            var level = entry.EntryType switch
            {
                EventLogEntryType.Error => LogLevel.Error,
                EventLogEntryType.FailureAudit => LogLevel.Error,
                EventLogEntryType.Warning => LogLevel.Warning,
                EventLogEntryType.Information => LogLevel.Information,
                EventLogEntryType.SuccessAudit => LogLevel.Information,
                _ => LogLevel.Information
            };

            // Push into GUI logger to surface in UI
            _guiLogger.LogInformation("[EventLog] {0}", entry.Message);
        }
        catch
        {
            // ignore parse errors
        }
    }

    public void Dispose()
    {
        if (_eventLog != null)
        {
            _eventLog.EntryWritten -= OnEntryWritten;
            _eventLog.Dispose();
        }
    }
}


