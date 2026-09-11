using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.Config.ImgFlip;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;

namespace image_flip_bosch.CLI.TUI
{

  internal sealed class SettingsScreen : NanoScreen
  {
    private readonly ConfigStore<AppConfig> _configStore;
    private readonly ImgflipSetup _setup;
    private readonly Action? _onSaved;

    private readonly PromptControl _username;
    private readonly PromptControl _password;
    private readonly PromptControl _maxFontSize;
    private readonly CheckboxControl _noWatermark;
    private readonly CheckboxControl _includeNsfw;
    private readonly CheckboxControl _customBoxes;
    private readonly DropdownControl _theme;
    private readonly MarkupControl _account;

    public SettingsScreen(ConsoleWindowSystem ws, ConfigStore<AppConfig> configStore, ImgflipSetup setup, Window? parent = null, Action? onSaved = null)
      : base(ws, "Settings")
    {
      _configStore = configStore;
      _setup = setup;
      _onSaved = onSaved;

      ImgFlipConfig current = configStore.Load().ImgFlip;

      _username = Controls.Prompt(" Username: ")
        .WithPlaceholder("imgflip account, optional")
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => Window.FocusControl(_password))
        .Build();
      _username.Input = current.Username ?? string.Empty;

      _password = Controls.Prompt(" Password: ")
        .WithPlaceholder(setup.IsConfigured ? "unchanged" : "optional")
        .WithMaskCharacter('*')
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => Window.FocusControl(_maxFontSize))
        .Build();

      _maxFontSize = Controls.Prompt(" Max font size: ")
        .WithPlaceholder("default 50")
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => Save())
        .Build();
      _maxFontSize.Input = current.MaxFontSize?.ToString() ?? string.Empty;

      _noWatermark = Controls.Checkbox("Remove watermark (premium accounts only)")
        .Checked(current.NoWatermark)
        .Build();

      _includeNsfw = Controls.Checkbox("Include NSFW templates when searching")
        .Checked(current.IncludeNsfw)
        .Build();

      _customBoxes = Controls.Checkbox("Default to custom box positions when captioning")
        .Checked(current.CustomBoxPositions)
        .Build();

      IReadOnlyList<string> themeNames = AppThemes.Names(ws);
      int currentTheme = themeNames.ToList().FindIndex(n => string.Equals(n, AppThemes.Current(ws), StringComparison.OrdinalIgnoreCase));
      _theme = Controls.Dropdown(" Theme: ")
        .AddItems(themeNames.ToArray())
        .SelectedIndex(Math.Max(0, currentTheme))
        .OnSelectedItemChanged((_, item) =>
        {
          if (item is null) return;
          AppThemes.Apply(ws, item.Text);
          AppThemes.Save(configStore, item.Text);
          Say($"Theme: {item.Text}");
        })
        .Build();

      _account = Controls.Markup(AccountLine()).Build();

      List<IWindowControl> column =
      [
        Controls.Markup(string.Empty).Build(),
        Controls.Markup(Chrome.MutedText(" Imgflip account")).Build(),
        _username,
        _password,
        _account,
        Controls.Markup(string.Empty).Build(),
        Controls.Markup(Chrome.MutedText(" Memes")).Build(),
        _maxFontSize,
        _noWatermark,
        _includeNsfw,
        _customBoxes,
        Controls.Markup(string.Empty).Build(),
        Controls.Markup(Chrome.MutedText(" Appearance")).Build(),
        _theme,
      ];

      BuildWindow([CenteredColumn(72, column)], modal: true);
    }

    protected override IEnumerable<(string Key, string Label)> Shortcuts =>
    [
      ("F2", "Save"),
      ("F6", "Logout"),
      ("F3", "Theme"),
      ("Esc", "Back"),
    ];

    public new void Show()
    {
      base.Show();
      Window.FocusControl(_username);
    }

    protected override void OnKey(KeyPressedEventArgs e)
    {
      switch (e.KeyInfo.Key)
      {
        case ConsoleKey.Escape: Window.Close(); e.Handled = true; break;
        case ConsoleKey.F2: Save(); e.Handled = true; break;
        case ConsoleKey.F6: RemoveCredentials(); e.Handled = true; break;
        case ConsoleKey.F3:
          AppThemes.Save(_configStore, AppThemes.Next(Ws, e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift)));
          e.Handled = true;
          break;
      }
    }

    protected override void OnChromeChanged() => _account.SetContent([AccountLine()]);

    private string AccountLine()
    {
      if (!_setup.IsConfigured) return Chrome.MutedText(" Not logged in. Memes will carry the imgflip watermark.");
      string storage = _setup.IsProtectedStorage ? "password encrypted" : "password stored unencrypted";
      return $" [{Chrome.Success.ToMarkup()}]Logged in as {SharpConsoleUI.Parsing.MarkupParser.Escape(_setup.Username!)}[/] {Chrome.MutedText($"({storage})")}";
    }

    private void Save()
    {
      string username = _username.Input.Trim();
      string password = _password.Input;

      int? maxFont = null;
      if (_maxFontSize.Input.Trim().Length > 0)
      {
        if (!int.TryParse(_maxFontSize.Input.Trim(), out int parsed) || parsed <= 0)
        {
          Say("Max font size must be a positive number", NotificationSeverity.Danger);
          return;
        }
        maxFont = parsed;
      }

      _configStore.Update(c =>
      {
        c.ImgFlip.MaxFontSize = maxFont;
        c.ImgFlip.NoWatermark = _noWatermark.Checked;
        c.ImgFlip.IncludeNsfw = _includeNsfw.Checked;
        c.ImgFlip.CustomBoxPositions = _customBoxes.Checked;
      });

      if (username.Length > 0 && password.Length > 0)
      {
        _setup.Set(username, password);
        _password.Input = string.Empty;
      }
      else if (username.Length > 0 && username != _setup.Username)
      {
        Say("Enter the password to change the username", NotificationSeverity.Warning);
        return;
      }

      _account.SetContent([AccountLine()]);
      Say("Settings saved", NotificationSeverity.Success);
      _onSaved?.Invoke();
    }

    private void RemoveCredentials()
    {
      _setup.Clear();
      _password.Input = string.Empty;
      _account.SetContent([AccountLine()]);
      Say("Logged out", NotificationSeverity.Warning);
      _onSaved?.Invoke();
    }
  }
}
