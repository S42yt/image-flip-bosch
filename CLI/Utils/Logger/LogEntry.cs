namespace image_flip_bosch.CLI.Utils.Logger;

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message, Exception? Exception);
