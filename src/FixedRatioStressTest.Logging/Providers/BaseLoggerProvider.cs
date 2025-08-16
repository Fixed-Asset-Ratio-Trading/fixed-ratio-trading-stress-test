using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Logging.Providers;

/// <summary>
/// Base class for custom logger providers that manages logger instances.
/// </summary>
public abstract class BaseLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();
    private bool _disposed;

    /// <summary>
    /// Creates a logger instance for the specified category.
    /// </summary>
    /// <param name="categoryName">The category name for the logger.</param>
    /// <returns>An <see cref="ILogger"/> instance.</returns>
    public ILogger CreateLogger(string categoryName)
    {
        ThrowIfDisposed();
        return _loggers.GetOrAdd(categoryName, name => CreateLoggerImplementation(name));
    }

    /// <summary>
    /// Creates the actual logger implementation for the specified category.
    /// </summary>
    /// <param name="categoryName">The category name for the logger.</param>
    /// <returns>An <see cref="ILogger"/> implementation.</returns>
    protected abstract ILogger CreateLoggerImplementation(string categoryName);

    /// <summary>
    /// Performs application-defined tasks associated with freeing resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and optionally managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _loggers.Clear();
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// Throws if this instance has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }
}
