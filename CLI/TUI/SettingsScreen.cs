namespace image_flip_bosch.CLI.TUI
{
  using image_flip_bosch.CLI.Config;
  using image_flip_bosch.CLI.Config.ImgFlip;
  using SharpConsoleUI;
  using SharpConsoleUI.Builders;
  using SharpConsoleUI.Controls;
  using SharpConsoleUI.Core;
  using SharpConsoleUI.Layout;
  using System;

  internal sealed class SettingsScreen
  {
    private readonly ConsoleWindowSystem _ws;
    private readonly ConfigStore<AppConfig> _configStore;
    private readonly ImgflipSetup _setup;
    private readonly Action? _onSaved;

    private readonly PromptControl _username;
    private readonly PromptControl _password;
    private readonly PromptControl _maxFontSize;
    private readonly CheckboxControl _noWatermark;
    private readonly MarkupControl _status;
    private readonly Window _window;

    public SettingsScreen(ConsoleWindowSystem ws, ConfigStore<AppConfig> configStore, ImgflipSetup setup, Window? parent = null, Action? onSaved = null)
    {
      _ws = ws;
      _configStore = configStore;
      _setup = setup;
      _onSaved = onSaved;

      ImgflipConfig current = configStore.Load().Imgflip;

      _username = Controls.Prompt(" Username ")
        .WithPlaceholder("imgflip account")
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => _window.FocusControl(_password))
        .Build();
      _username.Input = current.Username ?? string.Empty;

      _password = Controls.Prompt(" Password ")
        .WithPlaceholder(setup.IsConfigured ? "unchanged" : "optional")
        .WithMaskCharacter('*')
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => _window.FocusControl(_maxFontSize))
        .Build();

      _maxFontSize = Controls.Prompt(" Max font size ")
        .WithPlaceholder("default 50")
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => Save())
        .Build();
      _maxFontSize.Input = current.MaxFontSize?.ToString() ?? string.Empty;

      _noWatermark = Controls.Checkbox("Remove watermark (premium only)")
        .Checked(current.NoWatermark)
        .Build();

      _status = Controls.Markup(StatusLine()).Build();

      HorizontalGridControl buttons = Controls.HorizontalGrid()
        .Column(c => c.Add(Controls.Button("Save").OnClick((_, _) => Save()).Build()))
        .Column(c => c.Add(Controls.Button("Remove credentials").OnClick((_, _) => RemoveCredentials()).Build()))
        .Column(c => c.Add(Controls.Button("Close").OnClick((_, _) => _window.Close()).Build()))
        .Build();

      _window = new WindowBuilder(ws)
        .WithTitle("Settings")
        .WithSize(64, 14)
        .Centered()
        .AsModal()
        .Resizable(false)
        .Minimizable(false)
        .Maximizable(false)
        .AddControls(
          Controls.Markup("[dim]No account needed. An Imgflip login is only used for account-bound and premium features.[/]").Build(),
          _username,
          _password,
          _maxFontSize,
          _noWatermark,
          _status,
          buttons)
        .Build();

      _window.PreviewKeyPressed += (_, e) =>
      {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { _window.Close(); e.Handled = true; }
      };
    }

    public void Show()
    {
      _ws.AddWindow(_window);
      _window.FocusControl(_username);
    }

    private string StatusLine() =>
      _setup.IsConfigured
        ? $"[green]Logged in as[/] {_setup.Username}" + (_setup.IsProtectedStorage ? " [dim](password encrypted)[/]" : " [yellow](password stored unencrypted)[/]")
        : "[yellow]Not logged in.[/]";

    private void Save()
    {
      string username = _username.Input.Trim();
      string password = _password.Input;

      int? maxFont = null;
      if (_maxFontSize.Input.Trim().Length > 0)
      {
        if (!int.TryParse(_maxFontSize.Input.Trim(), out int parsed) || parsed <= 0)
        {
          _status.SetContent(["[red]Max font size must be a positive number.[/]"]);
          return;
        }
        maxFont = parsed;
      }

      _configStore.Update(c =>
      {
        c.Imgflip.MaxFontSize = maxFont;
        c.Imgflip.NoWatermark = _noWatermark.Checked;
      });

      if (username.Length > 0 && password.Length > 0)
      {
        _setup.Set(username, password);
        _password.Input = string.Empty;
      }
      else if (username.Length > 0 && username != _setup.Username)
      {
        _status.SetContent(["[yellow]Enter the password to change the username.[/]"]);
        return;
      }

      _status.SetContent([StatusLine()]);
      _ws.ToastService.Show("Settings saved", NotificationSeverity.Success);
      _onSaved?.Invoke();
    }

    private void RemoveCredentials()
    {
      _setup.Clear();
      _password.Input = string.Empty;
      _status.SetContent([StatusLine()]);
      _ws.ToastService.Show("Credentials removed", NotificationSeverity.Info);
      _onSaved?.Invoke();
    }
  }
}
