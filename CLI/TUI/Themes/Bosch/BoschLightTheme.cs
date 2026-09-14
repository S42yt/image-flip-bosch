using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Themes.Bosch
{
  internal sealed class BoschLightTheme : PaletteTheme
  {
    public const string ThemeName = "Bosch Light";

    public BoschLightTheme() : base(ThemeName, "Bosch red on light grey", new Palette
    {
      Primary = BoschTheme.Red,
      Secondary = BoschTheme.Blue,
      Tertiary = BoschTheme.Green,
      Background = Color.FromHex("#EFF1F2"),
      Foreground = Color.FromHex("#1B1F23"),
      Success = BoschTheme.Green,
      Warning = Color.FromHex("#C97A00"),
      Danger = BoschTheme.Red,
      Info = BoschTheme.Blue,
      Mode = ThemeMode.Light,
    })
    {
      ActiveBorderForegroundColor = BoschTheme.Red;
      ScrollbarThumbColor = BoschTheme.Red;
      ScrollbarTrackColor = Color.FromHex("#C5C9CE");
      TopBarBackgroundColor = BoschTheme.Red;
      TopBarForegroundColor = Color.FromHex("#FFFFFF");
      BottomBarBackgroundColor = Color.FromHex("#1B1F23");
      BottomBarForegroundColor = Color.FromHex("#EFF1F2");
      SecondaryColor = BoschTheme.Blue;
      InactiveTitleForegroundColor = Color.FromHex("#6B7280");
    }
  }
}
