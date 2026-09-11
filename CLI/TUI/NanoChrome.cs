namespace image_flip_bosch.CLI.TUI
{
  using SharpConsoleUI;
  using SharpConsoleUI.Core;
  using SharpConsoleUI.Parsing;
  using SharpConsoleUI.Themes;

  internal sealed class NanoChrome
  {
    public Color HeaderBackground { get; private init; }
    private Color HeaderForeground { get; init; }
    private Color KeyBackground { get; init; }
    private Color KeyForeground { get; init; }
    private Color StatusBackground { get; init; }
    public Color StatusForeground { get; private init; }
    public Color Success { get; private init; }
    private Color Warning { get; init; }
    private Color Danger { get; init; }
    private Color Muted { get; init; }

    public static NanoChrome From(ITheme theme)
    {
      Color accent = theme.PrimaryColor ?? Color.Grey;
      Color headerBg = theme.TopBarBackgroundColor ?? accent;
      Color keyBg = theme.BottomBarBackgroundColor ?? accent;
      Color statusBg = theme.SecondaryColor ?? theme.TopBarBackgroundColor ?? accent;

      return new NanoChrome
      {
        HeaderBackground = headerBg,
        HeaderForeground = theme.TopBarForegroundColor ?? Contrast(headerBg),
        KeyBackground = keyBg,
        KeyForeground = theme.BottomBarForegroundColor ?? Contrast(keyBg),
        StatusBackground = statusBg,
        StatusForeground = Contrast(statusBg),
        Success = theme.SuccessColor ?? Color.Green,
        Warning = theme.WarningColor ?? Color.Yellow,
        Danger = theme.DangerColor ?? Color.Red,
        Muted = theme.InactiveTitleForegroundColor ?? Color.Grey,
      };
    }

    public string Header(string text) => Paint(text, HeaderForeground, HeaderBackground);

    public string Key(string key, string label) =>
      $"{Paint(key.PadLeft(2), KeyForeground, KeyBackground)} {MarkupParser.Escape(label),-11}";

    public string Status(string text, NotificationSeverity? severity)
    {
      Color bg =
        severity == NotificationSeverity.Danger ? Danger :
        severity == NotificationSeverity.Warning ? Warning :
        severity == NotificationSeverity.Success ? Success :
        StatusBackground;
      return Paint($" {text} ", Contrast(bg), bg);
    }

    public string MutedText(string text) => $"[{Muted.ToMarkup()}]{MarkupParser.Escape(text)}[/]";

    private static string Paint(string text, Color fg, Color bg) =>
      $"[{fg.ToMarkup()} on {bg.ToMarkup()}]{MarkupParser.Escape(text)}[/]";

    private static Color Contrast(Color c)
    {
      double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
      return luminance > 0.55 ? Color.FromHex("#101214") : Color.FromHex("#F5F6F7");
    }
  }
}
