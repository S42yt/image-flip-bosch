using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Themes
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
    }
  }
}
