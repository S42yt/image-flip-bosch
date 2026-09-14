namespace image_flip_bosch.CLI.TUI.Themes
{
  using SharpConsoleUI;
  using SharpConsoleUI.Themes;

  internal sealed class BoschTheme : PaletteTheme
  {
    public const string ThemeName = "Bosch";

    public static readonly Color Red = Color.FromHex("#E20015");
    public static readonly Color Blue = Color.FromHex("#007BC0");
    public static readonly Color Green = Color.FromHex("#00884A");
    private static readonly Color Yellow = Color.FromHex("#FCAF17");
    private static readonly Color Anthracite = Color.FromHex("#141619");
    private static readonly Color Light = Color.FromHex("#E6E8EB");

    public BoschTheme() : base(ThemeName, "Bosch red on anthracite", new Palette
    {
      Primary = Red,
      Secondary = Blue,
      Tertiary = Green,
      Background = Anthracite,
      Foreground = Light,
      Success = Green,
      Warning = Yellow,
      Danger = Red,
      Info = Blue,
      Mode = ThemeMode.Dark,
    })
    {
      ActiveBorderForegroundColor = Red;
      ActiveTitleForegroundColor = Light;
      ScrollbarThumbColor = Red;
      ScrollbarTrackColor = Color.FromHex("#3A3E44");
      TopBarBackgroundColor = Red;
      TopBarForegroundColor = Color.FromHex("#FFFFFF");
      BottomBarBackgroundColor = Color.FromHex("#E6E8EB");
      BottomBarForegroundColor = Anthracite;
      SecondaryColor = Blue;
      InactiveTitleForegroundColor = Color.FromHex("#8A9099");
    }
  }
}
