using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.Gui;

public sealed class GuiLoggerOptions
{
    public bool EnableUiLogging { get; set; } = true;
    public bool EnableFileLogging { get; set; } = true;
    public string FilePath { get; set; } = "logs/gui.log";
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    public int MaxFileSizeKB { get; set; } = 10240; // 10 MB
}


