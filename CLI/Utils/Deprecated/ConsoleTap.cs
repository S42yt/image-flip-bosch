using System.Text;

namespace image_flip_bosch.CLI.Utils.Deprecated

{
  [Obsolete("Class was used to fix a Memory bug, now not needed anymore")]
  public static class ConsoleTap
  {
    private sealed class TeeWriter(TextWriter inner, StreamWriter tap, string tag) : TextWriter
    {
      public override Encoding Encoding => inner.Encoding;

      public override void Write(char value)
      {
        inner.Write(value);
        Record(value.ToString(), "char");
      }

      public override void Write(string? value)
      {
        inner.Write(value);
        Record(value ?? string.Empty, "string");
      }

      public override void Write(char[] buffer, int index, int count)
      {
        inner.Write(buffer, index, count);
        Record(new string(buffer, index, count), "chars");
      }

      public override void Flush() => inner.Flush();

      private void Record(string text, string kind)
      {
        StringBuilder sb = new();
        foreach (char c in text)
        {
          if (c == '\x1b') sb.Append("<ESC>");
          else if (c == '\n') sb.Append("<LF>");
          else if (c == '\r') sb.Append("<CR>");
          else if (char.IsSurrogate(c)) sb.Append($"<SURROGATE {(int)c:X4}>");
          else if (c == '\uFFFD') sb.Append("<FFFD>");
          else if (c < 32) sb.Append($"<{(int)c:X2}>");
          else sb.Append(c);
        }
        lock (tap)
        {
          tap.WriteLine($"[{tag} {kind} {Environment.CurrentManagedThreadId}] {sb}");
        }
      }
    }

    private static StreamWriter? _tap;

    public static void Start(string path)
    {
      if (_tap is not null) return;
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      _tap = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
      Console.SetOut(new TeeWriter(Console.Out, _tap, "out"));
      Console.SetError(new TeeWriter(Console.Error, _tap, "err"));
    }

    public static void Note(string text)
    {
      if (_tap is null) return;
      lock (_tap) _tap.WriteLine($"[note] {text}");
    }
  }
}
