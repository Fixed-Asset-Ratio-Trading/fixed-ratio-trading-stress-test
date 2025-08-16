using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FixedRatioStressTest.Logging.Providers;

namespace FixedRatioStressTest.Logging.WindowsService;

/// <summary>
/// Logger provider for Windows Service hosting that supports Event Log, file, and UDP logging.
/// </summary>
public class WindowsServiceLoggerProvider : BaseLoggerProvider
{
    private readonly WindowsServiceLoggerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsServiceLoggerProvider"/> class.
    /// </summary>
    /// <param name="options">The logger options.</param>
    public WindowsServiceLoggerProvider(IOptions<WindowsServiceLoggerOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsServiceLoggerProvider"/> class.
    /// </summary>
    /// <param name="options">The logger options.</param>
    public WindowsServiceLoggerProvider(WindowsServiceLoggerOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    protected override ILogger CreateLoggerImplementation(string categoryName)
    {
        return new WindowsServiceLogger(categoryName, _options);
    }
}
