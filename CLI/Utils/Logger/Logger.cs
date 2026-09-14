using System.Text;

namespace image_flip_bosch.CLI.Utils.Logger
{
  public static class Logger
  {
    private static readonly Lock Lock = new();
    private static TextWriter _writer = Console.Out;
    private static StreamWriter? _fileWriter;
    private static bool _consoleOutput = true;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public static event Action<LogEntry>? EntryLogged;

    public static void UseConsole()
    {
      lock (Lock)
      {
        _consoleOutput = true;
        _writer = Console.Out;
      }
    }

    public static void UseFile(string path, bool append = true)
    {
      lock (Lock)
      {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _fileWriter?.Dispose();
        _fileWriter = new StreamWriter(path, append, new UTF8Encoding(false)) { AutoFlush = true };
        _writer = _fileWriter;
        _consoleOutput = false;
      }
    }

    public static void Silence()
    {
      lock (Lock)
      {
        _writer = TextWriter.Null;
        _consoleOutput = false;
      }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);
    public static void Info(string message) => Write(LogLevel.Info, message, null);
    public static void Warn(string message) => Write(LogLevel.Warn, message, null);
    public static void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);

    private static void Write(LogLevel level, string message, Exception? exception)
    {
      if (level < MinimumLevel) return;

      LogEntry entry = new(DateTime.Now, level, message, exception);
      string line = $"[{entry.Time:HH:mm:ss.fff}] [{Tag(level)}] {message}";
      if (exception is not null) line += Environment.NewLine + exception;

      lock (Lock)
      {
        try
        {
          _writer.WriteLine(line);
          if (_consoleOutput) _writer.Flush();
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
      }

      EntryLogged?.Invoke(entry);
    }

    private static string Tag(LogLevel level) => level switch
    {
      LogLevel.Debug => "DEBUG",
      LogLevel.Info => "INFO ",
      LogLevel.Warn => "WARN ",
      _ => "ERROR",
    };
  }
}
