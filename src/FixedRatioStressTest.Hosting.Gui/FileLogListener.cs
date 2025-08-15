using System.Text;

namespace FixedRatioStressTest.Hosting.Gui;

public sealed class FileLogListener : IDisposable
{
    private readonly GuiEventLogger _guiLogger;
    private readonly string _filePath;
    private FileSystemWatcher? _watcher;
    private FileStream? _stream;
    private StreamReader? _reader;
    private readonly object _sync = new();
    private long _position;

    public FileLogListener(GuiEventLogger guiLogger, string filePath)
    {
        _guiLogger = guiLogger;
        _filePath = filePath;
    }

    public void Start()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            var name = Path.GetFileName(_filePath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;

            if (File.Exists(_filePath))
            {
                _stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                _reader = new StreamReader(_stream, Encoding.UTF8);
                _position = _stream.Length; // start tailing from end
                _stream.Seek(_position, SeekOrigin.Begin);
            }

            _watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
            _watcher.Renamed += OnRenamed;
        }
        catch
        {
            // ignore
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        // Re-open stream if file moved/rotated
        Reopen();
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            if (_reader == null)
            {
                Reopen();
                if (_reader == null) return;
            }

            lock (_sync)
            {
                _stream!.Seek(_position, SeekOrigin.Begin);
                string? line;
                while ((line = _reader!.ReadLine()) != null)
                {
                    _position = _stream.Position;
                    _guiLogger.LogInformation("[FileLog] {0}", line);
                }
            }
        }
        catch
        {
            // swallow
        }
    }

    private void Reopen()
    {
        try
        {
            lock (_sync)
            {
                _reader?.Dispose();
                _stream?.Dispose();
                if (File.Exists(_filePath))
                {
                    _stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _reader = new StreamReader(_stream, Encoding.UTF8);
                    _position = _stream.Length;
                    _stream.Seek(_position, SeekOrigin.Begin);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnChanged;
                _watcher.Created -= OnChanged;
                _watcher.Renamed -= OnRenamed;
                _watcher.Dispose();
            }
            _reader?.Dispose();
            _stream?.Dispose();
        }
        catch { }
    }
}


