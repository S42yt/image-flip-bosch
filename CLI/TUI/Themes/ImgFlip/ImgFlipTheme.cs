using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Themes.ImgFlip
{
  internal sealed class ImgFlipTheme : PaletteTheme
  {
    public const string ThemeName = "Imgflip";

    public ImgFlipTheme() : base(ThemeName, "Imgflip blue on dark", new Palette
    {
      Primary = Color.FromHex("#3B82F6"),
      Secondary = Color.FromHex("#F59E0B"),
      Background = Color.FromHex("#0D1117"),
      Mode = ThemeMode.Dark,
    })
    {
      TopBarBackgroundColor = Color.FromHex("#3B82F6");
      TopBarForegroundColor = Color.FromHex("#FFFFFF");
      BottomBarBackgroundColor = Color.FromHex("#E5E7EB");
      BottomBarForegroundColor = Color.FromHex("#0D1117");
      InactiveTitleForegroundColor = Color.FromHex("#6B7280");
    }
  }
}
