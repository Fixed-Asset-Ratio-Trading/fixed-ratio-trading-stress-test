using System.Drawing;
using System.Text;
using System.Windows.Forms;
using FixedRatioStressTest.Abstractions;
using FixedRatioStressTest.Logging.Gui;
using FixedRatioStressTest.Logging.Models;
using FixedRatioStressTest.Logging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.Gui;

/// <summary>
/// WinForms host with Start/Stop/Pause controls and a filterable log ListView.
/// The GUI is intentionally service-agnostic and only surfaces generic lifecycle operations and logs.
/// </summary>
public sealed class GuiServiceHost : Form, IServiceHost
{
    private readonly IServiceLifecycle _engine;
    private readonly GuiLoggerProvider _loggerProvider;
    private readonly UdpLogListenerService? _udpListener;
    private readonly IConfiguration _configuration;
    private readonly InProcessApiHost _apiHost;
    private bool _isShuttingDown;
    private bool _closeInitiated;
    private bool _stopScheduled;

    // UI controls
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private Button _pauseButton = null!;
    private Label _statusLabel = null!;
    private ComboBox _logFilter = null!;
    private CheckBox _autoScroll = null!;
    private Button _clearLogs = null!;
    private Button _copyLogs = null!;
    private ListView _listView = null!;

    // In-memory log buffer to enable filtering
    private readonly List<LogMessageEventArgs> _logBuffer = new();
    private LogLevel _filterLevel = LogLevel.Trace;

    public string HostType => "GUI";

    public GuiServiceHost(IServiceLifecycle engine, GuiLoggerProvider loggerProvider, 
        UdpLogListenerService? udpListener, IConfiguration configuration, InProcessApiHost apiHost)
    {
        _engine = engine;
        _loggerProvider = loggerProvider;
        _udpListener = udpListener;
        _configuration = configuration;
        _apiHost = apiHost;

        Text = _configuration.GetValue<string>("GuiSettings:WindowTitle", "Service Manager - Test Mode");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 650);

        InitializeComponent();
        HookEvents();
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Subscribe to engine state changes
        _engine.StateChanged += (_, __) =>
        {
            if (IsHandleCreated)
                BeginInvoke(new Action(UpdateUiState));
        };

        // Subscribe to log events from the logger provider
        _loggerProvider.LogMessageReceived += (_, e) =>
        {
            if (IsHandleCreated)
                BeginInvoke(new Action(() => OnLog(e)));
        };

        // Subscribe to UDP log events if listener is available
        if (_udpListener != null)
        {
            _udpListener.LogMessageReceived += (_, e) =>
            {
                if (IsHandleCreated)
                    BeginInvoke(new Action(() => OnLog(e)));
            };
        }

        if (IsHandleCreated)
        {
            UpdateUiState();
        }
        else
        {
            HandleCreated += (_, __) => UpdateUiState();
        }

        return Task.CompletedTask;
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        // This method is not used for WinForms - Application.Run is called from Program.cs
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        // Ensure API is stopped first
        await _apiHost.StopAsync(cancellationToken);

        if (_engine.State != ServiceState.Stopped)
        {
            await _engine.StopAsync(cancellationToken);
        }

        // Do NOT call Close() here, as OnFormClosed invokes ShutdownAsync, which would cause recursion
    }

    private void InitializeComponent()
    {
        _startButton = new Button { Text = "Start", Width = 80, Height = 30 };
        _stopButton = new Button { Text = "Stop", Width = 80, Height = 30, Enabled = false };
        _pauseButton = new Button { Text = "Pause", Width = 80, Height = 30, Enabled = false };
        _statusLabel = new Label { Text = "Status: Stopped", AutoSize = true };
        
        _logFilter = new ComboBox 
        { 
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            Items = { "All", "Debug+", "Info+", "Warning+", "Error+", "Critical" }
        };
        _logFilter.SelectedIndex = 1; // Default to Debug+
        
        _autoScroll = new CheckBox 
        { 
            Text = "Auto-scroll", 
            Checked = _configuration.GetValue<bool>("GuiSettings:AutoScroll", true),
            AutoSize = true 
        };
        
        _clearLogs = new Button { Text = "Clear", Width = 60, Height = 25 };
        _copyLogs = new Button { Text = "Copy", Width = 60, Height = 25 };
        
        _listView = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = true,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F)
        };
        
        _listView.Columns.Add("Time", 140);
        _listView.Columns.Add("Level", 60);
        _listView.Columns.Add("Message", 600);

        // Layout
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 80 };
        var controlPanel = new FlowLayoutPanel 
        { 
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(10, 10)
        };
        
        controlPanel.Controls.AddRange(new Control[] 
        { 
            _startButton, _stopButton, _pauseButton,
            new Label { Text = "  " }, // Spacer
            _statusLabel 
        });
        
        var logPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(10, 45)
        };
        
        logPanel.Controls.AddRange(new Control[]
        {
            new Label { Text = "Filter: ", AutoSize = true },
            _logFilter,
            new Label { Text = "  " }, // Spacer
            _autoScroll,
            new Label { Text = "  " }, // Spacer
            _clearLogs,
            _copyLogs
        });
        
        topPanel.Controls.Add(controlPanel);
        topPanel.Controls.Add(logPanel);
        
        Controls.Add(_listView);
        Controls.Add(topPanel);
    }

    private void HookEvents()
    {
        _startButton.Click += async (_, __) => await OnStart();
        _stopButton.Click += async (_, __) => await OnStop();
        _pauseButton.Click += async (_, __) => await OnPause();
        _clearLogs.Click += (_, __) => { _logBuffer.Clear(); _listView.Items.Clear(); };
        _copyLogs.Click += (_, __) => CopySelectedLogsToClipboard();
        _logFilter.SelectedIndexChanged += (_, __) => UpdateFilterLevel();
    }

    private void UpdateFilterLevel()
    {
        _filterLevel = _logFilter.SelectedIndex switch
        {
            0 => LogLevel.Trace,        // All
            1 => LogLevel.Debug,        // Debug+
            2 => LogLevel.Information, // Info+
            3 => LogLevel.Warning,     // Warning+
            4 => LogLevel.Error,       // Error+
            5 => LogLevel.Critical,    // Critical
            _ => LogLevel.Debug
        };
        RefreshLogList();
    }

    private void OnLog(LogMessageEventArgs e)
    {
        _logBuffer.Add(e);
        TrimBuffer();
        
        if (ShouldDisplay(e))
        {
            InsertListItem(e);
            TrimListView();
            
            if (_autoScroll.Checked && _listView.Items.Count > 0)
            {
                _listView.EnsureVisible(_listView.Items.Count - 1);
            }
        }

        // Check for RPC stop_service requested message and trigger automatic stop once
        if (!_stopScheduled && !string.IsNullOrEmpty(e.Message) &&
            e.Message.IndexOf("RPC stop_service requested", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _stopScheduled = true;
            Task.Run(async () =>
            {
                await Task.Delay(3000); // Wait 3 seconds
                if (IsHandleCreated && !_isShuttingDown)
                {
                    BeginInvoke(new Action(() => _stopButton.PerformClick()));
                }
            });
        }
    }

    private bool ShouldDisplay(LogMessageEventArgs e) => e.Level >= _filterLevel;

    private void InsertListItem(LogMessageEventArgs e)
    {
        var item = new ListViewItem(e.Timestamp.ToString("HH:mm:ss.fff"))
        {
            SubItems = 
            { 
                ShortLevel(e.Level),
                e.Message
            },
            ForeColor = Colorize(e.Level)
        };
        _listView.Items.Add(item);
    }

    private void RefreshLogList()
    {
        _listView.BeginUpdate();
        try
        {
            _listView.Items.Clear();
            foreach (var entry in _logBuffer.Where(ShouldDisplay))
            {
                InsertListItem(entry);
            }
        }
        finally
        {
            _listView.EndUpdate();
        }
    }

    private static string ShortLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "FAIL",
        LogLevel.Critical => "CRIT",
        _ => "NONE"
    };

    private static Color Colorize(LogLevel level) => level switch
    {
        LogLevel.Debug => Color.Gray,
        LogLevel.Information => Color.Black,
        LogLevel.Warning => Color.Orange,
        LogLevel.Error => Color.Red,
        LogLevel.Critical => Color.DarkRed,
        _ => Color.Black
    };

    private void TrimListView()
    {
        var maxDisplay = _configuration.GetValue<int>("GuiSettings:MaxDisplayEntries", 1000);
        while (_listView.Items.Count > maxDisplay)
        {
            _listView.Items.RemoveAt(0);
        }
    }

    private void TrimBuffer()
    {
        var maxEntries = _configuration.GetValue<int>("GuiSettings:MaxLogEntries", 5000);
        while (_logBuffer.Count > maxEntries)
        {
            _logBuffer.RemoveAt(0);
        }
    }

    private void UpdateUiState()
    {
        var state = _engine.State;
        _statusLabel.Text = $"Status: {state}";
        
        _startButton.Enabled = state == ServiceState.Stopped;
        _stopButton.Enabled = state != ServiceState.Stopped;
        _pauseButton.Enabled = state == ServiceState.Started;
        _pauseButton.Text = state == ServiceState.Paused ? "Resume" : "Pause";
    }

    private async Task OnStart()
    {
        try
        {
            _startButton.Enabled = false;
            await _engine.StartAsync();
            await _apiHost.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _startButton.Enabled = true;
        }
    }

    private async Task OnStop()
    {
        try
        {
            _stopButton.Enabled = false;
            await _apiHost.StopAsync();
            await _engine.StopAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to stop: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateUiState();
        }
    }

    private async Task OnPause()
    {
        try
        {
            _pauseButton.Enabled = false;
            if (_engine.State == ServiceState.Paused)
            {
                await _engine.ResumeAsync();
            }
            else
            {
                await _engine.PauseAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to pause/resume: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateUiState();
        }
    }

    private void CopySelectedLogsToClipboard()
    {
        if (_listView.SelectedItems.Count == 0) return;

        var sb = new StringBuilder();
        foreach (ListViewItem item in _listView.SelectedItems)
        {
            sb.AppendLine($"{item.SubItems[0].Text}\t{item.SubItems[1].Text}\t{item.SubItems[2].Text}");
        }
        Clipboard.SetText(sb.ToString());
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ShutdownAsync().GetAwaiter().GetResult();
        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // If the service is running or paused, route the close request through the Stop logic first
        if ((_engine.State == ServiceState.Started || _engine.State == ServiceState.Paused) && !_closeInitiated)
        {
            e.Cancel = true;
            _closeInitiated = true;
            BeginInvoke(new Action(async () =>
            {
                await OnStop();
                Close();
            }));
            return;
        }

        base.OnFormClosing(e);
    }
}