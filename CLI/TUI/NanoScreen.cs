using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Layout;

namespace image_flip_bosch.CLI.TUI
{

  internal abstract class NanoScreen
  {
    protected const string AppTitle = "image_flip_bosch";

    protected readonly ConsoleWindowSystem Ws;
    protected NanoChrome Chrome;
    protected Window Window = null!;

    private readonly string _screenTitle;
    private MarkupControl _header = null!;
    private MarkupControl _message = null!;
    private MarkupControl _shortcuts = null!;
    private string _lastMessage = string.Empty;
    private NotificationSeverity? _lastSeverity;
    private CancellationTokenSource? _messageCts;

    protected NanoScreen(ConsoleWindowSystem ws, string screenTitle)
    {
      Ws = ws;
      Chrome = NanoChrome.From(ws.Theme);
      _screenTitle = screenTitle;
    }

    protected abstract IEnumerable<(string Key, string Label)> Shortcuts { get; }

    protected virtual string HeaderCenter => _screenTitle;

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
        .StickyBottom()
        .Build();
      _shortcuts.SetContent(ShortcutRows());

      List<IWindowControl> controls = new() { _header };
      controls.AddRange(body);
      controls.Add(_message);
      controls.Add(_shortcuts);

      WindowBuilder builder = new WindowBuilder(Ws)
        .WithTitle($"{AppTitle} {_screenTitle}")
        .Frameless()
        .Resizable(false)
        .Movable(false)
        .Closable(closable)
        .Minimizable(false)
        .Maximizable(false)
        .AddControls(controls.ToArray());
      if (modal) builder.AsModal();

      Window = builder.Build();
      Window.PreviewKeyPressed += (s, e) => OnKey(e);
      Ws.ThemeStateService.ThemeChanged += OnThemeChanged;
      Window.OnClosed += (_, _) => Ws.ThemeStateService.ThemeChanged -= OnThemeChanged;
    }

    protected virtual void OnKey(KeyPressedEventArgs e) { }

    protected static GridControl CenteredColumn(int width, IReadOnlyList<IWindowControl> controls)
    {
      GridLength[] rows = new GridLength[controls.Count];
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
      });
    }

    protected virtual void OnChromeChanged() { }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
      Chrome = NanoChrome.From(e.NewTheme);
      Ws.InvokeAsync(() =>
      {
        _header.BackgroundColor = Chrome.HeaderBackground;
        _header.SetContent([HeaderText()]);
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
      string left = $"  {AppTitle}";
      string center = HeaderCenter;
      int pad = Math.Max(1, (width - left.Length - center.Length) / 2 - 1);
      string line = left + new string(' ', pad) + center;
      if (line.Length < width) line += new string(' ', width - line.Length);
      return Chrome.Header(line);
    }
  }
}
