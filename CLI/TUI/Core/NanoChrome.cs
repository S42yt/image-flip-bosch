using SharpConsoleUI;
using SharpConsoleUI.Core;
using SharpConsoleUI.Parsing;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Core
{

  internal sealed class NanoChrome
  {
    public Color Background { get; private init; }
    public Color Foreground { get; private init; }
    public Color HeaderBackground { get; private init; }
    private Color HeaderForeground { get; init; }
    private Color HeaderTitle { get; init; }
    public Color BarBackground { get; private init; }
    private Color BarForeground { get; init; }
    private Color KeyBackground { get; init; }
    private Color KeyForeground { get; init; }
    private Color StatusBackground { get; init; }
    private Color Accent { get; init; }
    private Color Section { get; init; }
    private Color Info { get; init; }
    public Color Success { get; private init; }
    private Color Warning { get; init; }
    public Color Danger { get; private init; }
    private Color Muted { get; init; }
    public Color Separator { get; private init; }
    private Color Highlight { get; init; }

    public static NanoChrome From(ITheme theme)
    {
      Color background = theme.WindowBackgroundColor.A == 0 ? Color.FromHex("#101214") : theme.WindowBackgroundColor;
      Color foreground = theme.WindowForegroundColor;
      Color accent = theme.PrimaryColor ?? foreground;
      Color headerBg = theme.TopBarBackgroundColor ?? accent;
      Color barBg = theme.StatusBarBackgroundColor ?? theme.BottomBarBackgroundColor ?? background;
      Color keyBg = theme.BottomBarBackgroundColor ?? accent;
      Color statusBg = theme.SecondaryColor ?? theme.InfoColor ?? accent;

      return new NanoChrome
      {
        Background = background,
        Foreground = foreground,
        HeaderBackground = headerBg,
        HeaderForeground = theme.TopBarForegroundColor ?? Contrast(headerBg),
        HeaderTitle = theme.ActiveTitleForegroundColor ?? theme.TopBarForegroundColor ?? Contrast(headerBg),
        BarBackground = barBg,
        BarForeground = theme.StatusBarForegroundColor ?? theme.BottomBarForegroundColor ?? Contrast(barBg),
        KeyBackground = keyBg,
        KeyForeground = theme.StatusBarShortcutForegroundColor ?? theme.BottomBarForegroundColor ?? Contrast(keyBg),
        StatusBackground = statusBg,
        Accent = accent,
        Section = theme.TertiaryColor ?? theme.SecondaryColor ?? accent,
        Info = theme.InfoColor ?? accent,
        Success = theme.SuccessColor ?? Color.Green,
        Warning = theme.WarningColor ?? Color.Yellow,
        Danger = theme.DangerColor ?? Color.Red,
        Muted = theme.InactiveTitleForegroundColor ?? theme.SecondaryColor ?? Color.Grey,
        Separator = theme.SeparatorForegroundColor ?? theme.InactiveBorderForegroundColor ?? theme.InactiveTitleForegroundColor ?? Color.Grey,
        Highlight = theme.ListSelectedForegroundColor ?? foreground,
      };
    }

    public string Header(string left, string center, string right, int width)
    {
      int pad = Math.Max(1, (width - left.Length - center.Length) / 2 - 1);
      int tail = Math.Max(0, width - left.Length - pad - center.Length - right.Length);
      return Paint(left, HeaderForeground, HeaderBackground)
        + Paint(new string(' ', pad), HeaderForeground, HeaderBackground)
        + Paint(center, HeaderTitle, HeaderBackground, bold: true)
        + Paint(new string(' ', tail), HeaderForeground, HeaderBackground)
        + Paint(right, HeaderTitle, HeaderBackground);
    }

    public string Key(string key, string label) =>
      $"{Paint(key.PadLeft(2), KeyForeground, KeyBackground, bold: true)}{Paint($" {label,-11}", BarForeground, BarBackground)}";

    public string Status(string text, NotificationSeverity? severity)
    {
      Color bg =
        severity == NotificationSeverity.Danger ? Danger :
        severity == NotificationSeverity.Warning ? Warning :
        severity == NotificationSeverity.Success ? Success :
        StatusBackground;
      return Paint($" {text} ", Contrast(bg), bg);
    }

    public string SectionText(string text) => $"[{Section.ToMarkup()} bold]{MarkupParser.Escape(text)}[/]";

    public string MutedText(string text) => $"[{Muted.ToMarkup()}]{MarkupParser.Escape(text)}[/]";

    public string AccentText(string text) => $"[{Accent.ToMarkup()}]{MarkupParser.Escape(text)}[/]";

    public string InfoText(string text) => $"[{Info.ToMarkup()}]{MarkupParser.Escape(text)}[/]";

    public string HighlightText(string text) => $"[{Highlight.ToMarkup()} bold]{MarkupParser.Escape(text)}[/]";

    private static string Paint(string text, Color fg, Color bg, bool bold = false) =>
      $"[{fg.ToMarkup()} on {bg.ToMarkup()}{(bold ? " bold" : string.Empty)}]{MarkupParser.Escape(text)}[/]";

    private static Color Contrast(Color c)
    {
      double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
      return luminance > 0.55 ? Color.FromHex("#101214") : Color.FromHex("#F5F6F7");
    }
  }
}
