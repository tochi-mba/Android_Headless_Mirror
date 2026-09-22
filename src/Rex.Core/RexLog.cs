using System.Globalization;

namespace Rex.Core;

/// <summary>Small rotating text log (logs/mirror.log). Safe to call from any thread; never throws.</summary>
public sealed class RexLog
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly LoggingSettings _settings;

    public RexLog(string path, LoggingSettings settings)
    {
        _path = path;
        _settings = settings;
    }

    public string Path => _path;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Error(string message, Exception exception) => Write("ERROR", $"{message}: {exception.GetType().Name}: {exception.Message}");

    public event Action<string>? LineWritten;

    public IReadOnlyList<string> Tail(int lines)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var all = File.ReadAllLines(_path);
            return all.Skip(Math.Max(0, all.Length - lines)).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} [{level}] {message}";
        LineWritten?.Invoke(line);

        if (!_settings.Enabled)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                Rotate();
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never break mirroring.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never break mirroring.
            }
        }
    }

    private void Rotate()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < _settings.MaxBytes)
        {
            return;
        }

        for (var i = _settings.KeepFiles - 1; i >= 1; i--)
        {
            var source = $"{_path}.{i}";
            var target = $"{_path}.{i + 1}";
            if (File.Exists(source))
            {
                File.Move(source, target, overwrite: true);
            }
        }

        File.Move(_path, _path + ".1", overwrite: true);
    }
}
