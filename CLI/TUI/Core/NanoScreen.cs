using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Layout;

namespace image_flip_bosch.CLI.TUI.Core
{

  internal abstract class NanoScreen(ConsoleWindowSystem ws, string screenTitle)
  {
    protected readonly ConsoleWindowSystem Ws = ws;
    protected NanoChrome Chrome = NanoChrome.From(ws.Theme);
    protected Window Window = null!;

    private MarkupControl _header = null!;
    private MarkupControl _message = null!;
    private MarkupControl _shortcuts = null!;
    private string _lastMessage = string.Empty;
    private NotificationSeverity? _lastSeverity;
    private CancellationTokenSource? _messageCts;

    protected abstract IEnumerable<(string Key, string Label)> Shortcuts { get; }

    protected virtual string HeaderCenter => screenTitle;

    protected void BuildWindow(IEnumerable<IWindowControl> body, bool modal, bool closable = true)
    {
      _header = Controls.Markup(HeaderText())
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithBackgroundColor(Chrome.HeaderBackground)
        .StickyTop()
        .Build();

      _message = Controls.Markup(string.Empty)
        .WithAlignment(HorizontalAlignment.Center)
        .StickyBottom()
        .Build();

      _shortcuts = Controls.Markup(string.Empty)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithBackgroundColor(Chrome.BarBackground)
        .StickyBottom()
        .Build();
      _shortcuts.SetContent(ShortcutRows());

      List<IWindowControl> controls =
      [
        _header,
        .. body,
        _message,
        _shortcuts
      ];

      WindowBuilder builder = new WindowBuilder(Ws)
        .WithTitle(screenTitle)
        .Frameless()
        .Resizable(false)
        .Movable(false)
        .Closable(closable)
        .Minimizable(false)
        .Maximizable(false)
        .AddControls([.. controls]);
      if (modal) builder.AsModal();

      Window = builder.Build();
      Window.PreviewKeyPressed += (_, e) => OnKey(e);
      Ws.ThemeStateService.ThemeChanged += OnThemeChanged;
      Window.OnClosed += (_, _) => Ws.ThemeStateService.ThemeChanged -= OnThemeChanged;
    }

    protected virtual void OnKey(KeyPressedEventArgs e) { }

    protected static GridControl CenteredColumn(int width, IReadOnlyList<IWindowControl> controls)
    {
      var rows = new GridLength[controls.Count];
      for (int i = 0; i < rows.Length; i++) rows[i] = GridLength.Auto();

      GridControl grid = Controls.Grid()
        .Columns(GridLength.Star(), GridLength.Cells(width), GridLength.Star())
        .Rows(rows)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();

      for (int i = 0; i < controls.Count; i++)
        grid.Place(controls[i], i, 1);
      return grid;
    }

    protected void Show()
    {
      Ws.AddWindow(Window);
      Window.State = WindowState.Maximized;
    }

    protected void RefreshHeader() => Ws.InvokeAsync(() => _header.SetContent([HeaderText()]));

    protected void Say(string text, NotificationSeverity? severity = null)
    {
      _lastMessage = text;
      _lastSeverity = severity;

      _messageCts?.Cancel();
      CancellationTokenSource cts = new();
      _messageCts = cts;

      Ws.InvokeAsync(() => _message.SetContent([Chrome.Status(text, severity)]));
      _ = Task.Delay(TimeSpan.FromSeconds(6), cts.Token).ContinueWith(t =>
      {
        if (t.IsCanceled) return;
        _lastMessage = string.Empty;
        Ws.InvokeAsync(() => _message.SetContent([string.Empty]));
      }, cts.Token);
    }

    protected virtual void OnChromeChanged() { }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
      Chrome = NanoChrome.From(e.NewTheme);
      Ws.InvokeAsync(() =>
      {
        _header.BackgroundColor = Chrome.HeaderBackground;
        _header.SetContent([HeaderText()]);
        _shortcuts.BackgroundColor = Chrome.BarBackground;
        _shortcuts.SetContent(ShortcutRows());
        if (_lastMessage.Length > 0)
          _message.SetContent([Chrome.Status(_lastMessage, _lastSeverity)]);
        OnChromeChanged();
      });
    }

    private List<string> ShortcutRows()
    {
      List<(string Key, string Label)> items = new(Shortcuts);
      string row1 = string.Empty;
      string row2 = string.Empty;
      for (int i = 0; i < items.Count; i++)
      {
        string cell = Chrome.Key(items[i].Key, items[i].Label);
        if (i % 2 == 0) row1 += cell; else row2 += cell;
      }
      return [row1, row2];
    }

    private string HeaderText()
    {
      int width = Math.Max(20, Ws.ConsoleDriver.ScreenSize.Width);
      return Chrome.Header(" ", HeaderCenter, " ", width);
    }
  }
}
