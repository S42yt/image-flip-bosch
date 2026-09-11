namespace image_flip_bosch.CLI.TUI
{
  using image_flip_bosch.CLI.Config;
  using image_flip_bosch.CLI.Config.ImgFlip;
  using image_flip_bosch.CLI.Utils;
  using image_flip_bosch.ImgFlip;
  using SharpConsoleUI;
  using SharpConsoleUI.Builders;
  using SharpConsoleUI.Controls;
  using SharpConsoleUI.Core;
  using SharpConsoleUI.Dialogs;
  using SharpConsoleUI.Helpers;
  using SharpConsoleUI.Layout;
  using SharpConsoleUI.Parsing;
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;

  internal sealed class MainScreen
  {
    private readonly ConsoleWindowSystem _ws;
    private readonly ImageCache _cache;
    private readonly ImgflipSession _imgflip;
    private readonly ConfigStore<AppConfig> _configStore;
    private readonly ImgflipSetup _setup;

    private readonly PromptControl _filter;
    private readonly ListControl _templates;
    private readonly ImagePreview _preview;
    private readonly MarkupControl _details;
    private readonly MarkupControl _log;
    private readonly PromptControl _topText;
    private readonly PromptControl _bottomText;
    private readonly Window _window;

    private Meme[] _allMemes = Array.Empty<Meme>();
    private Meme? _selected;
    private string? _resultUrl;
    private bool _busy;
    private CancellationTokenSource? _previewCts;

    public MainScreen(ConsoleWindowSystem ws, ImageCache cache, ImgflipSession imgflip, ConfigStore<AppConfig> configStore, ImgflipSetup setup)
    {
      _ws = ws;
      _cache = cache;
      _imgflip = imgflip;
      _configStore = configStore;
      _setup = setup;

      _filter = Controls.Prompt(" Filter ")
        .WithPlaceholder("type to filter templates")
        .UnfocusOnEnter(false)
        .OnInputChanged((_, text) => ApplyFilter(text))
        .Build();

      _templates = Controls.List("Templates")
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .WithAlignment(HorizontalAlignment.Stretch)
        .OnSelectedItemChanged((_, item) => OnTemplateSelected(item))
        .OnItemActivated((_, _) => _window.FocusControl(_topText))
        .Build();

      _preview = new ImagePreview(ws)
      {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };
      _preview.LoadFailed += (_, msg) => Log($"[red]Preview failed:[/] {MarkupParser.Escape(msg)}");

      _details = Controls.Markup("[dim]No template selected.[/]").Build();
      _log = Controls.Markup("[dim]Loading templates...[/]").Build();

      ScrollablePanelControl logPanel = Controls.ScrollablePanel()
        .AddControl(_log)
        .WithAutoScroll()
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();

      GridControl side = Controls.Grid()
        .Columns(GridLength.Star())
        .Rows(GridLength.Auto(), GridLength.Star())
        .RowGap(1)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      side.Place(_details, 0, 0);
      side.Place(logPanel, 1, 0);

      GridControl content = Controls.Grid()
        .Columns(GridLength.Star(1), GridLength.Star(2), GridLength.Star(1))
        .Rows(GridLength.Star())
        .ColumnGap(1)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      content.Place(_templates, 0, 0);
      content.Place(_preview, 0, 1);
      content.Place(side, 0, 2);
      content.Cell(0, 0).Border = BorderStyle.Rounded;
      content.Cell(0, 1).Border = BorderStyle.Rounded;
      content.Cell(0, 2).Border = BorderStyle.Rounded;

      _topText = Controls.Prompt(" Top ")
        .WithPlaceholder("text0")
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => _window.FocusControl(_bottomText))
        .Build();

      _bottomText = Controls.Prompt(" Bottom ")
        .WithPlaceholder("text1")
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => _ = CreateMemeAsync())
        .Build();

      GridControl captions = Controls.Grid()
        .Columns(GridLength.Star(), GridLength.Star())
        .Rows(GridLength.Auto())
        .ColumnGap(1)
        .WithAlignment(HorizontalAlignment.Stretch)
        .Build();
      captions.Place(_topText, 0, 0);
      captions.Place(_bottomText, 0, 1);

      StatusBarControl statusBar = Controls.StatusBar()
        .AddLeft("F5", "Create", () => _ = CreateMemeAsync())
        .AddLeft("^C", "Copy URL", () => CopyResultUrl())
        .AddLeft("^S", "Save", () => _ = SaveResultAsync())
        .AddLeft("^R", "Reload", () => _ = LoadTemplatesAsync())
        .AddLeft("^O", "Settings", () => OpenSettings())
        .AddRight("^X", "Quit", () => _ws.Shutdown())
        .StickyBottom()
        .Build();

      _window = new WindowBuilder(ws)
        .WithTitle("image_flip_bosch")
        .HideTitleButtons()
        .Resizable(false)
        .Movable(false)
        .Closable(false)
        .Minimizable(false)
        .Maximizable(false)
        .AddControls(_filter, content, captions, statusBar)
        .Build();

      _window.PreviewKeyPressed += OnKey;
    }

    public void Show()
    {
      _ws.AddWindow(_window);
      _window.State = WindowState.Maximized;
      _window.FocusControl(_filter);
      if (!_setup.IsConfigured)
        Log("[dim]No Imgflip login. Memes get the imgflip watermark; add an account under ^O to change settings.[/]");
      _ = LoadTemplatesAsync();
    }

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
      if (e.KeyInfo.Key == ConsoleKey.F5)
      {
        _ = CreateMemeAsync();
        e.Handled = true;
        return;
      }

      if (!e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)) return;
      switch (e.KeyInfo.Key)
      {
        case ConsoleKey.C: CopyResultUrl(); e.Handled = true; break;
        case ConsoleKey.S: _ = SaveResultAsync(); e.Handled = true; break;
        case ConsoleKey.R: _ = LoadTemplatesAsync(); e.Handled = true; break;
        case ConsoleKey.O: OpenSettings(); e.Handled = true; break;
        case ConsoleKey.X: _ws.Shutdown(); e.Handled = true; break;
      }
    }

    private void OpenSettings() =>
      new SettingsScreen(_ws, _configStore, _setup, _window, () => Log(_setup.IsConfigured
        ? $"Logged in as [cyan]{MarkupParser.Escape(_setup.Username!)}[/]."
        : "[yellow]No Imgflip login. Browsing only.[/]")).Show();

    private void Log(string markup) =>
      _ws.InvokeAsync(() => _log.AppendLine($"[dim]{DateTime.Now:HH:mm:ss}[/] {markup}"));

    private void Toast(string message, NotificationSeverity severity) =>
      _ws.InvokeAsync(() => _ws.ToastService.Show(message, severity));

    private async Task LoadTemplatesAsync()
    {
      try
      {
        _allMemes = await _imgflip.GetMemes();
        Log($"Loaded [cyan]{_allMemes.Length}[/] templates.");
        await _ws.InvokeAsync(() => ApplyFilter(_filter.Input));
      }
      catch (Exception ex)
      {
        Log($"[red]Could not load templates:[/] {MarkupParser.Escape(ex.Message)}");
        Toast("Loading templates failed", NotificationSeverity.Danger);
      }
    }

    private void ApplyFilter(string text)
    {
      string needle = text.Trim();
      IEnumerable<Meme> visible = string.IsNullOrEmpty(needle)
        ? _allMemes
        : _allMemes.Where(m => m.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));

      List<ListItem> items = visible
        .Select(meme => new ListItem($"{MarkupParser.Escape(meme.Name)} [dim]({meme.BoxCount})[/]") { Tag = meme })
        .ToList();

      _templates.Items = items;
      if (items.Count > 0)
        _templates.SelectedIndex = 0;
    }

    private void OnTemplateSelected(ListItem? item)
    {
      if (item?.Tag is not Meme meme || ReferenceEquals(meme, _selected)) return;

      _selected = meme;
      _resultUrl = null;
      _details.SetContent(
      [
        $"[bold]{MarkupParser.Escape(meme.Name)}[/]",
        $"ID [cyan]{meme.Id}[/]",
        $"{meme.Width}x{meme.Height}, {meme.BoxCount} text boxes",
        meme.BoxCount > 2 ? "[yellow]Only top/bottom text is filled here.[/]" : string.Empty,
      ]);

      _previewCts?.Cancel();
      CancellationTokenSource cts = new();
      _previewCts = cts;
      _ = LoadTemplatePreviewAsync(meme, cts.Token);
    }

    private async Task LoadTemplatePreviewAsync(Meme meme, CancellationToken ct)
    {
      try
      {
        await Task.Delay(150, ct);
        string path = await _cache.GetAsync(meme.Url, ct);
        if (ct.IsCancellationRequested) return;
        await _preview.LoadWhenReadyAsync(path, ct: ct);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        Log($"[red]Template download failed:[/] {MarkupParser.Escape(ex.Message)}");
      }
    }

    private async Task CreateMemeAsync()
    {
      if (_busy) { Log("[yellow]Busy.[/]"); return; }
      if (_selected is null) { Log("[yellow]Select a template first.[/]"); return; }

      string top = _topText.Input.Trim();
      string bottom = _bottomText.Input.Trim();
      if (top.Length == 0 && bottom.Length == 0)
      {
        Log("[yellow]Enter at least one caption.[/]");
        return;
      }

      _busy = true;
      Log($"Creating meme with [cyan]{MarkupParser.Escape(_selected.Name)}[/]...");

      try
      {
        ImgflipConfig options = _configStore.Load().Imgflip;
        _previewCts?.Cancel();
        string url = await _imgflip.CaptionImage(
          _selected.Id,
          top,
          bottom,
          options.MaxFontSize,
          options.NoWatermark && _imgflip.IsAuthenticated ? true : null);

        _resultUrl = url;
        Log($"[green]Created:[/] {MarkupParser.Escape(url)}");
        Toast("Meme created", NotificationSeverity.Success);

        string path = await _cache.GetAsync(url);
        await _preview.LoadWhenReadyAsync(path);
      }
      catch (Exception ex)
      {
        Log($"[red]Create failed:[/] {MarkupParser.Escape(ex.Message)}");
        Toast("Create failed", NotificationSeverity.Danger);
      }
      finally
      {
        _busy = false;
      }
    }

    private void CopyResultUrl()
    {
      if (_resultUrl is null) { Log("[yellow]No meme created yet.[/]"); return; }
      ClipboardHelper.SetText(_resultUrl);
      Toast("URL copied", NotificationSeverity.Success);
      Log("URL copied to clipboard.");
    }

    private async Task SaveResultAsync()
    {
      if (_resultUrl is null) { Log("[yellow]No meme created yet.[/]"); return; }

      string? source = _cache.TryGetPath(_resultUrl);
      if (source is null) { Log("[yellow]Result is not cached yet.[/]"); return; }

      string? target = await FileDialogs.ShowSaveFileAsync(
        _ws,
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "*.png;*.jpg;*.jpeg;*.gif",
        Path.GetFileName(new Uri(_resultUrl).AbsolutePath),
        _window);

      if (target is null) { Log("Save cancelled."); return; }

      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        Log($"[green]Saved:[/] {MarkupParser.Escape(target)}");
        Toast("Saved", NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        Log($"[red]Save failed:[/] {MarkupParser.Escape(ex.Message)}");
      }
    }
  }
}
