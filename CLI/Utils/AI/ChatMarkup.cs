using SharpConsoleUI.Parsing;
using System.Text;
using System.Text.RegularExpressions;

namespace image_flip_bosch.CLI.Utils.Ai
{
  public static partial class ChatMarkup
  {
    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])[*_](?=\S)(.+?)(?<=\S)[*_](?![\w*])")]
    private static partial Regex Italic();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex InlineCode();

    public static string Render(string text, string codeBackground = "#202428", string codeForeground = "#c8d0d8")
    {
      StringBuilder o = new(text.Length + 64);
      string[] lines = text.Replace("\r\n", "\n").Split('\n');
      bool inCode = false;
      string lang = string.Empty;

      foreach (string raw in lines)
      {
        string line = raw;
        if (line.TrimStart().StartsWith("```"))
        {
          if (!inCode)
          {
            inCode = true;
            lang = line.Trim()[3..].Trim();
            o.Append($"[{codeForeground} on {codeBackground} dim] {(lang.Length > 0 ? lang : "code")} [/]\n");
          }
          else
          {
            inCode = false;
          }
          continue;
        }

        if (inCode)
        {
          o.Append($"[{codeForeground} on {codeBackground}]  {MarkupParser.Escape(line)}[/]\n");
          continue;
        }

        o.Append(Inline(line)).Append('\n');
      }

      if (inCode) o.Append($"[{codeForeground} on {codeBackground} dim] ... [/]\n");
      return o.ToString().TrimEnd('\n');
    }

    private static string Inline(string line)
    {
      List<string> codes = [];
      string withPlaceholders = InlineCode().Replace(line, m =>
      {
        codes.Add(m.Groups[1].Value);
        return $"{codes.Count - 1}";
      });

      string escaped = MarkupParser.Escape(withPlaceholders);
      escaped = Bold().Replace(escaped, "[bold]$1[/]");
      escaped = Italic().Replace(escaped, "[italic]$1[/]");

      for (int i = 0; i < codes.Count; i++)
        escaped = escaped.Replace($"{i}", $"[bold]{MarkupParser.Escape(codes[i])}[/]");

      return escaped;
    }
  }
}
