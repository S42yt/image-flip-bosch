using System;
using System.IO;
using System.Text;


namespace image_flip_bosch.CLI.Utils
{

  public static class ConsoleTap
  {
    private sealed class TeeWriter : TextWriter
    {
      private readonly TextWriter _inner;
      private readonly StreamWriter _tap;
      private readonly string _tag;

      public TeeWriter(TextWriter inner, StreamWriter tap, string tag)
      {
        _inner = inner;
        _tap = tap;
        _tag = tag;
      }

      public override Encoding Encoding => _inner.Encoding;

      public override void Write(char value)
      {
        _inner.Write(value);
        Record(value.ToString(), "char");
      }

      public override void Write(string? value)
      {
        _inner.Write(value);
        Record(value ?? string.Empty, "string");
      }

      public override void Write(char[] buffer, int index, int count)
      {
        _inner.Write(buffer, index, count);
        Record(new string(buffer, index, count), "chars");
      }

      public override void Flush() => _inner.Flush();

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
        lock (_tap)
        {
          _tap.WriteLine($"[{_tag} {kind} {Environment.CurrentManagedThreadId}] {sb}");
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
