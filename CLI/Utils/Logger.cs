namespace image_flip_bosch.Bot.Utils
{
  public static class Logger
  {
    public enum Level { Debug, Info, Warn, Error }

    public static Level MinLevel { get; set; } = Level.Debug;

    private static readonly object Sync = new();

    public static void Debug(string message) => Write(Level.Debug, message);
    public static void Info(string message) => Write(Level.Info, message);
    public static void Warn(string message) => Write(Level.Warn, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(Level.Error, ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(Level level, string message)
    {
      if (level < MinLevel) return;

      lock (Sync)
      {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = level switch
        {
          Level.Debug => ConsoleColor.DarkGray,
          Level.Info => ConsoleColor.Cyan,
          Level.Warn => ConsoleColor.Yellow,
          Level.Error => ConsoleColor.Red,
          _ => previous
        };
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant(),-5}] {message}");
        Console.ForegroundColor = previous;
      }
    }
  }
}
