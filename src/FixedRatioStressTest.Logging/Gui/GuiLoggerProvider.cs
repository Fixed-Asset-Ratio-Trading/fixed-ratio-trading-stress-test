using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FixedRatioStressTest.Logging.Models;
using FixedRatioStressTest.Logging.Providers;

namespace FixedRatioStressTest.Logging.Gui;

/// <summary>
/// Logger provider for GUI hosting that raises events for ListView updates.
/// </summary>
public class GuiLoggerProvider : BaseLoggerProvider
{
    private readonly GuiLoggerOptions _options;
    private readonly ConcurrentQueue<LogMessageEventArgs> _pendingMessages = new();
    private readonly System.Threading.Timer _batchTimer;

    /// <summary>
    /// Event raised when a log message is received. This event is raised on a background thread.
    /// GUI consumers should marshal to the UI thread.
    /// </summary>
    public event EventHandler<LogMessageEventArgs>? LogMessageReceived;

    /// <summary>
    /// Event raised when a batch of log messages is ready. This is raised at regular intervals
    /// to allow for efficient UI updates.
    /// </summary>
    public event EventHandler<IReadOnlyList<LogMessageEventArgs>>? LogMessageBatchReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuiLoggerProvider"/> class.
    /// </summary>
    /// <param name="options">The logger options.</param>
    public GuiLoggerProvider(IOptions<GuiLoggerOptions> options)
    {
        _options = options.Value;
        _batchTimer = new System.Threading.Timer(ProcessBatch, null, _options.Display.RefreshInterval, _options.Display.RefreshInterval);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GuiLoggerProvider"/> class.
    /// </summary>
    /// <param name="options">The logger options.</param>
    public GuiLoggerProvider(GuiLoggerOptions options)
    {
        _options = options;
        _batchTimer = new System.Threading.Timer(ProcessBatch, null, _options.Display.RefreshInterval, _options.Display.RefreshInterval);
    }

    /// <inheritdoc />
    protected override ILogger CreateLoggerImplementation(string categoryName)
    {
        return new GuiLogger(categoryName, _options, this);
    }

    /// <summary>
    /// Raises a log message event. Called by GuiLogger instances.
    /// </summary>
    /// <param name="args">The log message event arguments.</param>
    internal void RaiseLogMessage(LogMessageEventArgs args)
    {
        // Queue for batch processing
        _pendingMessages.Enqueue(args);

        // Also raise immediate event for real-time updates if needed
        LogMessageReceived?.Invoke(this, args);
    }

    private void ProcessBatch(object? state)
    {
        if (_pendingMessages.IsEmpty)
            return;

        var batch = new List<LogMessageEventArgs>();
        while (_pendingMessages.TryDequeue(out var message) && batch.Count < 100)
        {
            batch.Add(message);
        }

        if (batch.Count > 0)
        {
            LogMessageBatchReceived?.Invoke(this, batch);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _batchTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
