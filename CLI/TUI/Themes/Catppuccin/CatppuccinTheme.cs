using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI.Themes.Catppuccin
{
  internal class CatppuccinTheme<TPalette> : PaletteTheme
        where TPalette : ICatppuccinPalette
  {
    private protected CatppuccinTheme() : base(TPalette.ThemeName, TPalette.ThemeDesc, new Palette
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
      SeparatorForegroundColor = TPalette.Surface0;

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
}
