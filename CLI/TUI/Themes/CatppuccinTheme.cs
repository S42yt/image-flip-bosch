using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Themes
{
  public interface ICatppuccinPalette
  {
    static abstract string ThemeName { get; }
    static abstract string ThemeDesc { get; }
    static abstract ThemeMode ThemeMode { get; }

    static abstract Color Red { get; }
    static abstract Color Blue { get; }
    static abstract Color Green { get; }
    static abstract Color Yellow { get; }
    static abstract Color Text { get; }
    static abstract Color Subtext1 { get; }
    static abstract Color Subtext0 { get; }
    static abstract Color Overlay2 { get; }
    static abstract Color Overlay1 { get; }
    static abstract Color Overlay0 { get; }
    static abstract Color Surface2 { get; }
    static abstract Color Surface1 { get; }
    static abstract Color Surface0 { get; }
    static abstract Color Base { get; }
    static abstract Color Mantle { get; }
    static abstract Color Crust { get; }
  }

  internal class CatppuccinTheme<TPalette> : PaletteTheme
        where TPalette : ICatppuccinPalette
  {
    public CatppuccinTheme() : base(TPalette.ThemeName, TPalette.ThemeDesc, new Palette
    {
      Primary = TPalette.Text,
      Secondary = TPalette.Subtext1,
      Tertiary = TPalette.Subtext0,
      Background = TPalette.Base,
      Foreground = TPalette.Surface0,
      Success = TPalette.Green,
      Warning = TPalette.Yellow,
      Danger = TPalette.Red,
      Info = TPalette.Blue,
      Mode = TPalette.ThemeMode,
    })
    {
      BottomBarBackgroundColor = TPalette.Base;
      BottomBarForegroundColor = TPalette.Surface2;
      TopBarBackgroundColor = TPalette.Base;
      TopBarForegroundColor = TPalette.Surface2;

      ActiveBorderForegroundColor = TPalette.Subtext0;
      ActiveTitleForegroundColor = TPalette.Subtext0;

      InactiveBorderForegroundColor = TPalette.Subtext0;
      InactiveTitleForegroundColor = TPalette.Subtext0;

      ScrollbarThumbColor = TPalette.Overlay2;
      ScrollbarTrackColor = TPalette.Surface2;

      ButtonBackgroundColor = TPalette.Surface1;
      ButtonForegroundColor = TPalette.Subtext1;
      ButtonFocusedBackgroundColor = TPalette.Surface2;
      ButtonFocusedForegroundColor = TPalette.Text;
      ButtonSelectedBackgroundColor = TPalette.Text;
      ButtonSelectedForegroundColor = TPalette.Subtext0;
      ButtonDisabledBackgroundColor = TPalette.Surface1;
      ButtonDisabledForegroundColor = TPalette.Subtext0;

      ModalBackgroundColor = TPalette.Mantle;
      ModalBorderForegroundColor = Color.FromHex("#ff00ff");
      ModalTitleForegroundColor = Color.FromHex("#ff00ff");
      ModalFlashColor = Color.FromHex("#ff00ff");

      NotificationWindowBackgroundColor = Color.FromHex("#ff00ff");
      NotificationInfoWindowBackgroundColor = Color.FromHex("#ff00ff");
      NotificationSuccessWindowBackgroundColor = Color.FromHex("#ff00ff");
      NotificationWarningWindowBackgroundColor = Color.FromHex("#ff00ff");
      NotificationDangerWindowBackgroundColor = Color.FromHex("#ff00ff");

      DesktopBackgroundColor = Color.FromHex("#ff00ff");
      DesktopForegroundColor = Color.FromHex("#ff00ff");

      PromptInputBackgroundColor = TPalette.Surface1;
      PromptInputFocusedBackgroundColor = TPalette.Surface0;
      PromptInputForegroundColor = TPalette.Subtext0;
      PromptInputFocusedForegroundColor = TPalette.Text;
      TextEditFocusedNotEditing = Color.FromHex("#ff00ff");

      ListUnfocusedHighlightBackgroundColor = TPalette.Surface0;
      ListUnfocusedHighlightForegroundColor = TPalette.Subtext0;
      ListHoverBackgroundColor = TPalette.Surface0;
      ListHoverForegroundColor = TPalette.Text;
      ListBackgroundColor = TPalette.Base;
      ListForegroundColor = TPalette.Subtext1;
      ListFocusedForegroundColor = TPalette.Subtext1;
      ListSelectedForegroundColor = TPalette.Text;
      ListSelectedBackgroundColor = TPalette.Surface0;
      ListDisabledForegroundColor = Color.FromHex("#ff00ff");
      ListDisabledBackgroundColor = Color.FromHex("#ff00ff");

      MenuBarBackgroundColor = Color.FromHex("#ff00ff");
      MenuBarForegroundColor = Color.FromHex("#ff00ff");
      MenuBarHighlightBackgroundColor = Color.FromHex("#ff00ff");
      MenuBarHighlightForegroundColor = Color.FromHex("#ff00ff");

      MenuDropdownBackgroundColor = Color.FromHex("#ff00ff");
      MenuDropdownForegroundColor = Color.FromHex("#ff00ff");
      MenuDropdownHighlightBackgroundColor = Color.FromHex("#ff00ff");
      MenuDropdownHighlightForegroundColor = Color.FromHex("#ff00ff");

      DropdownBackgroundColor = TPalette.Surface1;
      DropdownForegroundColor = TPalette.Subtext1;
      DropdownHighlightBackgroundColor = TPalette.Surface0;
      DropdownHighlightForegroundColor = TPalette.Text;
      DropdownFocusedForegroundColor = TPalette.Subtext1;
      DropdownFocusedBackgroundColor = TPalette.Surface0;
      DropdownDisabledForegroundColor = Color.FromHex("#ff00ff");
      DropdownDisabledBackgroundColor = Color.FromHex("#ff00ff");

      ProgressBarFilledColor = Color.FromHex("#ff00ff");
      ProgressBarUnfilledColor = Color.FromHex("#ff00ff");
      ProgressBarPercentageColor = Color.FromHex("#ff00ff");

      TableBackgroundColor = Color.FromHex("#ff00ff");
      TableForegroundColor = Color.FromHex("#ff00ff");
      TableBorderColor = Color.FromHex("#ff00ff");
      TableHeaderBackgroundColor = Color.FromHex("#ff00ff");
      TableHeaderForegroundColor = Color.FromHex("#ff00ff");
      TableSelectionBackgroundColor = Color.FromHex("#ff00ff");
      TableSelectionForegroundColor = Color.FromHex("#ff00ff");
      TableHoverBackgroundColor = Color.FromHex("#ff00ff");
      TableHoverForegroundColor = Color.FromHex("#ff00ff");
      TableUnfocusedSelectionBackgroundColor = Color.FromHex("#ff00ff");
      TableUnfocusedSelectionForegroundColor = Color.FromHex("#ff00ff");
      TableScrollbarThumbColor = Color.FromHex("#ff00ff");
      TableScrollbarTrackColor = Color.FromHex("#ff00ff");

      TabHeaderActiveBackgroundColor = Color.FromHex("#ff00ff");
      TabHeaderActiveForegroundColor = Color.FromHex("#ff00ff");
      TabHeaderBackgroundColor = Color.FromHex("#ff00ff");
      TabHeaderForegroundColor = Color.FromHex("#ff00ff");
      TabHeaderDisabledForegroundColor = Color.FromHex("#ff00ff");
      TabHeaderDisabledBackgroundColor = Color.FromHex("#ff00ff");
      TabHeaderActiveFocusedBackgroundColor = Color.FromHex("#ff00ff");
      TabHeaderActiveFocusedForegroundColor = Color.FromHex("#ff00ff");
      TabHeaderFocusedBackgroundColor = Color.FromHex("#ff00ff");
      TabHeaderFocusedForegroundColor = Color.FromHex("#ff00ff");

      TabContentBorderColor = Color.FromHex("#ff00ff");
      TabContentBackgroundColor = Color.FromHex("#ff00ff");

      CollapsibleHeaderFocusedForegroundColor = Color.FromHex("#ff00ff");
      CollapsibleHeaderFocusedBackgroundColor = Color.FromHex("#ff00ff");

      ToolbarBackgroundColor = Color.FromHex("#ff00ff");
      ToolbarForegroundColor = Color.FromHex("#ff00ff");
      SeparatorForegroundColor = Color.FromHex("#ff00ff");

      StatusBarBackgroundColor = TPalette.Surface0;
      StatusBarForegroundColor = TPalette.Subtext0;
      StatusBarShortcutForegroundColor = TPalette.Subtext1;

      SliderTrackColor = Color.FromHex("#ff00ff");
      SliderFilledTrackColor = Color.FromHex("#ff00ff");
      SliderThumbColor = Color.FromHex("#ff00ff");
      SliderFocusedThumbColor = Color.FromHex("#ff00ff");

      CheckboxBackgroundColor = TPalette.Surface1;
      CheckboxFocusedBackgroundColor = TPalette.Surface0;
      CheckboxDisabledBackgroundColor = TPalette.Surface2;
      CheckboxForegroundColor = TPalette.Subtext1;
      CheckboxFocusedForegroundColor = TPalette.Subtext1;
      CheckboxDisabledForegroundColor = TPalette.Subtext0;
      CheckboxCheckmarkColor = TPalette.Text;

      StartMenuHeaderBackgroundColor = Color.FromHex("#ff00ff");
      StartMenuHeaderForegroundColor = Color.FromHex("#ff00ff");
      StartMenuSectionHeaderBackgroundColor = Color.FromHex("#ff00ff");
      StartMenuInfoStripForegroundColor = Color.FromHex("#ff00ff");
    }
  }

  internal sealed class LatteTheme : CatppuccinTheme<LatteTheme>, ICatppuccinPalette
  {
    public static string ThemeName => "Latte";
    public static string ThemeDesc => "Catppuccin Latte Theme";
    public static ThemeMode ThemeMode => ThemeMode.Light;

    public static Color Red => Color.FromHex("#d20f39");
    public static Color Blue => Color.FromHex("#1e66f5");
    public static Color Green => Color.FromHex("#40a02b");
    public static Color Yellow => Color.FromHex("#df8e1d");
    public static Color Text => Color.FromHex("#4c4f69");
    public static Color Subtext1 => Color.FromHex("#5c5f77");
    public static Color Subtext0 => Color.FromHex("#6c6f85");
    public static Color Overlay2 => Color.FromHex("#7c7f93");
    public static Color Overlay1 => Color.FromHex("#8c8fa1");
    public static Color Overlay0 => Color.FromHex("#9ca0b0");
    public static Color Surface2 => Color.FromHex("#acb0be");
    public static Color Surface1 => Color.FromHex("#bcc0cc");
    public static Color Surface0 => Color.FromHex("#ccd0da");
    public static Color Base => Color.FromHex("#eff1f5");
    public static Color Mantle => Color.FromHex("#e6e9ef");
    public static Color Crust => Color.FromHex("#dce0e8");
  }

  internal sealed class FrappeTheme : CatppuccinTheme<FrappeTheme>, ICatppuccinPalette
  {
    public static string ThemeName => "Frappe";
    public static string ThemeDesc => "Catppuccin Frappe Theme";
    public static ThemeMode ThemeMode => ThemeMode.Dark;

    public static Color Red => Color.FromHex("#e78284");
    public static Color Blue => Color.FromHex("#8caaee");
    public static Color Green => Color.FromHex("#a6d189");
    public static Color Yellow => Color.FromHex("#e5c890");
    public static Color Text => Color.FromHex("#c6d0f5");
    public static Color Subtext1 => Color.FromHex("#b5bfe2");
    public static Color Subtext0 => Color.FromHex("#a5adce");
    public static Color Overlay2 => Color.FromHex("#949cbb");
    public static Color Overlay1 => Color.FromHex("#838ba7");
    public static Color Overlay0 => Color.FromHex("#737994");
    public static Color Surface2 => Color.FromHex("#626880");
    public static Color Surface1 => Color.FromHex("#51576d");
    public static Color Surface0 => Color.FromHex("#414559");
    public static Color Base => Color.FromHex("#303446");
    public static Color Mantle => Color.FromHex("#292c3c");
    public static Color Crust => Color.FromHex("#232634");
  }

  internal sealed class MacchiatoTheme : CatppuccinTheme<MacchiatoTheme>, ICatppuccinPalette
  {
    public static string ThemeName => "Macchiato";
    public static string ThemeDesc => "Catppuccin Macchiato Theme";
    public static ThemeMode ThemeMode => ThemeMode.Dark;

    public static Color Red => Color.FromHex("#ed8796");
    public static Color Blue => Color.FromHex("#8aadf4");
    public static Color Green => Color.FromHex("#a6da95");
    public static Color Yellow => Color.FromHex("#eed49f");
    public static Color Text => Color.FromHex("#cad3f5");
    public static Color Subtext1 => Color.FromHex("#b8c0e0");
    public static Color Subtext0 => Color.FromHex("#a5adcb");
    public static Color Overlay2 => Color.FromHex("#939ab7");
    public static Color Overlay1 => Color.FromHex("#8087a2");
    public static Color Overlay0 => Color.FromHex("#6e738d");
    public static Color Surface2 => Color.FromHex("#5b6078");
    public static Color Surface1 => Color.FromHex("#494d64");
    public static Color Surface0 => Color.FromHex("#363a4f");
    public static Color Base => Color.FromHex("#24273a");
    public static Color Mantle => Color.FromHex("#1e2030");
    public static Color Crust => Color.FromHex("#181926");
  }

  internal sealed class MochaTheme : CatppuccinTheme<MochaTheme>, ICatppuccinPalette
  {
    public static string ThemeName => "Mocha";
    public static string ThemeDesc => "Catppuccin Mocha Theme";
    public static ThemeMode ThemeMode => ThemeMode.Dark;

    public static Color Red => Color.FromHex("#f38ba8");
    public static Color Blue => Color.FromHex("#89b4fa");
    public static Color Green => Color.FromHex("#a6e3a1");
    public static Color Yellow => Color.FromHex("#f9e2af");
    public static Color Text => Color.FromHex("#cdd6f4");
    public static Color Subtext1 => Color.FromHex("#bac2de");
    public static Color Subtext0 => Color.FromHex("#a6adc8");
    public static Color Overlay2 => Color.FromHex("#9399b2");
    public static Color Overlay1 => Color.FromHex("#7f849c");
    public static Color Overlay0 => Color.FromHex("#6c7086");
    public static Color Surface2 => Color.FromHex("#585b70");
    public static Color Surface1 => Color.FromHex("#45475a");
    public static Color Surface0 => Color.FromHex("#313244");
    public static Color Base => Color.FromHex("#1e1e2e");
    public static Color Mantle => Color.FromHex("#181825");
    public static Color Crust => Color.FromHex("#11111b");
  }
}
