using System.Drawing;
using System.Windows.Forms;
using FixedRatioStressTest.Abstractions;

namespace FixedRatioStressTest.Hosting.Gui;

/// <summary>
/// WinForms host with Start/Stop/Pause controls and a filterable log ListView.
/// The GUI is intentionally service-agnostic and only surfaces generic lifecycle operations and logs.
/// </summary>
public sealed class GuiServiceHost : Form, IServiceHost
{
    private readonly IServiceLifecycle _engine;
    private readonly GuiEventLogger _eventLogger;

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
    private readonly List<LogEventArgs> _logBuffer = new();
    private Microsoft.Extensions.Logging.LogLevel _filterLevel = Microsoft.Extensions.Logging.LogLevel.Trace;
    private EventLogListener? _eventLogListener;
    private FileLogListener? _fileLogListener;
    private UdpLogListener? _udpLogListener;

    public string HostType => "GUI";

    public GuiServiceHost(IServiceLifecycle engine, GuiEventLogger logger)
    {
        _engine = engine;
        _eventLogger = logger;

        Text = "Service Manager - Test Mode";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 650);

        InitializeComponent();
        HookEvents();
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Subscribe to events. Defer UI updates until handle is created to avoid cross-thread errors.
        _engine.StateChanged += (_, __) =>
        {
            if (IsHandleCreated)
                BeginInvoke(new Action(UpdateUiState));
        };

        _eventLogger.LogEntryCreated += (_, e) =>
        {
            if (IsHandleCreated)
                BeginInvoke(new Action(() => OnLog(e)));
        };

        if (IsHandleCreated)
        {
            UpdateUiState();
        }
        else
        {
            HandleCreated += (_, __) => UpdateUiState();
        }

        // Start listening to Windows Application EventLog to mirror service logs into GUI
        _eventLogListener = new EventLogListener(_eventLogger);
        _eventLogListener.Start();
        // Mirror API file log into GUI if present
        var apiLogPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FixedRatioStressTest.Api", "bin", "Debug", "net8.0", "logs", "api.log");
        _fileLogListener = new FileLogListener(_eventLogger, apiLogPath);
        _fileLogListener.Start();
        // UDP live log listener
        _udpLogListener = new UdpLogListener(_eventLogger, 51999);
        _udpLogListener.Start();

        return Task.CompletedTask;
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        Application.Run(this);
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_engine.State != ServiceState.Stopped)
        {
            await _engine.StopAsync(cancellationToken);
        }
        _eventLogListener?.Dispose();
        _fileLogListener?.Dispose();
        _udpLogListener?.Dispose();
        Close();
    }

    private void InitializeComponent()
    {
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(10) };
        var filterPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 0, 10, 0) };
        var contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

        _startButton = new Button { Text = "▶ Start", Width = 90, Height = 32, Left = 10, Top = 10 };
        _stopButton = new Button { Text = "⏹ Stop", Width = 90, Height = 32, Left = 110, Top = 10 };
        _pauseButton = new Button { Text = "⏸ Pause", Width = 90, Height = 32, Left = 210, Top = 10 };
        _statusLabel = new Label { Text = "Status: Stopped", Left = 320, Top = 15, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };

        topPanel.Controls.AddRange(new Control[] { _startButton, _stopButton, _pauseButton, _statusLabel });

        _logFilter = new ComboBox { Left = 10, Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        _logFilter.Items.AddRange(new object[] { "All Logs", "Debug+", "Info+", "Warning+", "Error+", "Critical" });
        _logFilter.SelectedIndex = 0;
        _autoScroll = new CheckBox { Text = "Auto-scroll", Left = 140, Top = 8, Width = 100, Checked = true };
        _clearLogs = new Button { Text = "Clear", Left = 250, Width = 80, Height = 24, Top = 5 };
        _copyLogs = new Button { Text = "Copy", Left = 340, Width = 80, Height = 24, Top = 5, Enabled = false };

        filterPanel.Controls.AddRange(new Control[] { _logFilter, _autoScroll, _clearLogs, _copyLogs });

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = true,
            HideSelection = false
        };
        _listView.Columns.Add("Time", 120);
        _listView.Columns.Add("Level", 80);
        _listView.Columns.Add("Message", -2);

        contentPanel.Controls.Add(_listView);

        Controls.Add(contentPanel);
        Controls.Add(filterPanel);
        Controls.Add(topPanel);
    }

    private void HookEvents()
    {
        _startButton.Click += async (_, __) => await SafeRun(_engine.StartAsync, "Failed to start service");
        _stopButton.Click += async (_, __) => await SafeRun(_engine.StopAsync, "Failed to stop service");
        _pauseButton.Click += async (_, __) =>
        {
            if (_engine.State == ServiceState.Started)
                await SafeRun(_engine.PauseAsync, "Failed to pause service");
            else if (_engine.State == ServiceState.Paused)
                await SafeRun(_engine.ResumeAsync, "Failed to resume service");
        };

        _clearLogs.Click += (_, __) => { _logBuffer.Clear(); _listView.Items.Clear(); _copyLogs.Enabled = false; };
        _copyLogs.Click += (_, __) => CopySelectedLogsToClipboard();
        _listView.SelectedIndexChanged += (_, __) => _copyLogs.Enabled = _listView.SelectedItems.Count > 0;
        _logFilter.SelectedIndexChanged += (_, __) => { UpdateFilterLevel(); RefreshLogList(); };

        FormClosing += async (s, e) =>
        {
            if (_engine.State != ServiceState.Stopped)
            {
                e.Cancel = true;
                await _engine.StopAsync();
                Close();
            }
        };
    }

    private async Task SafeRun(Func<CancellationToken, Task> op, string errorTitle)
    {
        try { await op(CancellationToken.None); }
        catch (Exception ex) { MessageBox.Show(ex.Message, errorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UpdateUiState(); }
    }

    private void UpdateFilterLevel()
    {
        _filterLevel = _logFilter.SelectedIndex switch
        {
            0 => Microsoft.Extensions.Logging.LogLevel.Trace,
            1 => Microsoft.Extensions.Logging.LogLevel.Debug,
            2 => Microsoft.Extensions.Logging.LogLevel.Information,
            3 => Microsoft.Extensions.Logging.LogLevel.Warning,
            4 => Microsoft.Extensions.Logging.LogLevel.Error,
            5 => Microsoft.Extensions.Logging.LogLevel.Critical,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
    }

    private void OnLog(LogEventArgs e)
    {
        _logBuffer.Insert(0, e);
        if (ShouldDisplay(e))
        {
            InsertListItem(e);
            TrimListView();
            if (_autoScroll.Checked && _listView.Items.Count > 0)
                _listView.EnsureVisible(0);
        }
        TrimBuffer();
    }

    private bool ShouldDisplay(LogEventArgs e)
        => _filterLevel == Microsoft.Extensions.Logging.LogLevel.Critical
            ? e.Level == Microsoft.Extensions.Logging.LogLevel.Critical
            : e.Level >= _filterLevel;

    private void InsertListItem(LogEventArgs e)
    {
        var item = new ListViewItem(e.Timestamp.ToString("HH:mm:ss.fff"));
        item.SubItems.Add(ShortLevel(e.Level));
        item.SubItems.Add(e.Message);
        Colorize(item, e.Level);
        _listView.Items.Insert(0, item);
    }

    private void RefreshLogList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var e in _logBuffer.Where(ShouldDisplay).Take(1000))
            InsertListItem(e);
        _listView.EndUpdate();
    }

    private void CopySelectedLogsToClipboard()
    {
        if (_listView.SelectedItems.Count == 0) return;
        var lines = new List<string>(_listView.SelectedItems.Count);
        foreach (ListViewItem item in _listView.SelectedItems)
        {
            var time = item.SubItems[0].Text;
            var level = item.SubItems[1].Text;
            var message = item.SubItems[2].Text;
            lines.Add($"{time}\t{level}\t{message}");
        }
        var text = string.Join(Environment.NewLine, lines);
        try { Clipboard.SetText(text); } catch { }
    }

    private static string ShortLevel(Microsoft.Extensions.Logging.LogLevel level) => level switch
    {
        Microsoft.Extensions.Logging.LogLevel.Trace => "TRACE",
        Microsoft.Extensions.Logging.LogLevel.Debug => "DEBUG",
        Microsoft.Extensions.Logging.LogLevel.Information => "INFO",
        Microsoft.Extensions.Logging.LogLevel.Warning => "WARN",
        Microsoft.Extensions.Logging.LogLevel.Error => "ERROR",
        Microsoft.Extensions.Logging.LogLevel.Critical => "CRIT",
        _ => level.ToString().ToUpperInvariant()
    };

    private static void Colorize(ListViewItem item, Microsoft.Extensions.Logging.LogLevel level)
    {
        switch (level)
        {
            case Microsoft.Extensions.Logging.LogLevel.Critical:
                item.BackColor = Color.DarkRed; item.ForeColor = Color.White; break;
            case Microsoft.Extensions.Logging.LogLevel.Error:
                item.BackColor = Color.MistyRose; break;
            case Microsoft.Extensions.Logging.LogLevel.Warning:
                item.BackColor = Color.LemonChiffon; break;
            case Microsoft.Extensions.Logging.LogLevel.Debug:
                item.ForeColor = Color.DimGray; break;
        }
    }

    private void TrimListView()
    {
        while (_listView.Items.Count > 1000)
            _listView.Items.RemoveAt(_listView.Items.Count - 1);
    }

    private void TrimBuffer()
    {
        while (_logBuffer.Count > 5000)
            _logBuffer.RemoveAt(_logBuffer.Count - 1);
    }

    private void UpdateUiState()
    {
        _startButton.Enabled = _engine.State == ServiceState.Stopped;
        _stopButton.Enabled = _engine.State is ServiceState.Started or ServiceState.Paused;
        _pauseButton.Enabled = _engine.State is ServiceState.Started or ServiceState.Paused;
        _pauseButton.Text = _engine.State == ServiceState.Paused ? "▶ Resume" : "⏸ Pause";
        _statusLabel.Text = $"Status: {_engine.State}";
        _statusLabel.ForeColor = _engine.State switch
        {
            ServiceState.Started => Color.Green,
            ServiceState.Paused => Color.Orange,
            ServiceState.Error => Color.Red,
            _ => Color.Black
        };
    }
}


