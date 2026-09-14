using image_flip_bosch.CLI.Config;
using image_flip_bosch.CLI.Config.ImgFlip;
using image_flip_bosch.CLI.TUI.Core;
using image_flip_bosch.CLI.Utils;
using image_flip_bosch.CLI.Utils.Image;
using image_flip_bosch.CLI.Utils.Native;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Enum;
using image_flip_bosch.ImgFlip.Requests;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;  
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using SharpConsoleUI.Themes;

namespace image_flip_bosch.CLI.TUI
{

  internal sealed record AppOptions(bool ShowDebugScreen);

  internal sealed class MainScreen
  {

    private readonly ConsoleWindowSystem _ws;
    private readonly ImageCache _cache;
    private readonly ImgflipSession _imgflip;
    private readonly ConfigStore<AppConfig> _configStore;
    private readonly ImgflipSetup _setup;
    private MemeFeedClient _feed;

    private readonly MarkupControl _header;
    private readonly PromptControl _filter;
    private readonly ListControl _templates;
    private readonly ImagePreview _preview;
    private readonly MarkupControl _message;
    private readonly MarkupControl _shortcuts;
    private readonly MarkupControl? _debugLog;
    private readonly Window _window;
    private readonly GridControl? _contentGrid;
    private NanoChrome _chrome;
    private string _lastMessage = string.Empty;
    private NotificationSeverity? _lastSeverity;

    private EMemeTyp _mode = EMemeTyp.Image;
    private Meme[] _allMemes = [];
    private readonly HashSet<string> _knownIds = [];
    private readonly HashSet<string> _searchedQueries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _debugLines = [];
    private const int MaxDebugLines = 200;
    private bool _loadingMore;
    private bool _searchUnavailable;
    private Meme? _selected;
    private string? _resultUrl;
    private MemeCreationBox[]? _lastCaptions;
    private bool _busy;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _messageCts;
    private CancellationTokenSource? _searchCts;

    public MainScreen(ConsoleWindowSystem ws, ImageCache cache, ImgflipSession imgflip, ConfigStore<AppConfig> configStore, ImgflipSetup setup, AppOptions options)
    {
      _ws = ws;
      _cache = cache;
      _imgflip = imgflip;
      _configStore = configStore;
      _setup = setup;
      _feed = new MemeFeedClient(configStore.Load().Feed.BaseUrl);
      _chrome = NanoChrome.From(ws.Theme);

      _header = Controls.Markup(HeaderText(null))
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithBackgroundColor(_chrome.HeaderBackground)
        .StickyTop()
        .Build();

      _filter = Controls.Prompt(" Filter: ")
        .WithPlaceholder("type to filter or search templates")
        .UnfocusOnEnter(false)
        .OnInputChanged((_, text) => { ApplyFilter(text); ScheduleSearch(text); })
        .OnEntered((_, _) => _window?.FocusControl(_templates))
        .StickyTop()
        .Build();

      _templates = Controls.List(ListTitle())
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .WithAlignment(HorizontalAlignment.Stretch)
        .OnSelectedItemChanged((_, item) => OnTemplateSelected(item))
        .OnItemActivated((_, _) => _ = OpenCaptionsAsync())
        .Build();

      _preview = new ImagePreview(ws)
      {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };
      _preview.LoadFailed += (_, msg) => Say($"Preview failed: {msg}", NotificationSeverity.Danger);

      GridBuilder grid = Controls.Grid()
        .Rows(GridLength.Star())
        .ColumnGap(1)
        .ColumnGridlines()
        .GridlineStyle(BorderStyle.Single)
        .GridlineColor(_chrome.Separator)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill);

      GridControl content;
      _contentGrid = null;
      if (options.ShowDebugScreen)
      {
        _debugLog = Controls.Markup(_chrome.MutedText("debug log")).Build();
        ScrollablePanelControl logPanel = Controls.ScrollablePanel()
          .AddControl(_debugLog)
          .WithAutoScroll()
          .WithVerticalAlignment(VerticalAlignment.Fill)
          .Build();

        content = grid.Columns(GridLength.Star(), GridLength.Star(2), GridLength.Star()).Build();
        content.Place(_templates, 0, 0);
        content.Place(_preview, 0, 1);
        content.Place(logPanel, 0, 2);
      }
      else
      {
        content = grid.Columns(GridLength.Star(), GridLength.Star(2)).Build();
        content.Place(_templates, 0, 0);
        content.Place(_preview, 0, 1);
      }
      _contentGrid = content;

      _message = Controls.Markup(string.Empty)
        .WithAlignment(HorizontalAlignment.Center)
        .StickyBottom()
        .Build();

      _shortcuts = Controls.Markup(string.Empty)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithBackgroundColor(_chrome.BarBackground)
        .StickyBottom()
        .Build();
      _shortcuts.SetContent(ShortcutRows());

      _window = new WindowBuilder(ws)
        .Frameless()
        .Resizable(false)
        .Movable(false)
        .Closable(false)
        .Minimizable(false)
        .Maximizable(false)
        .AddControls(_header, _filter, content, _message, _shortcuts)
        .Build();

      _window.PreviewKeyPressed += OnKey;
      ws.ThemeStateService.ThemeChanged += (_, e) => ApplyChrome(e.NewTheme);
    }

    private bool GifMode => _mode == EMemeTyp.Gif;

    private bool GifsAvailable => _imgflip.IsPremium == true;

    private string ListTitle() => GifMode ? "GIF templates" : "Image templates";

    private void ApplyChrome(ITheme theme)
    {
      _chrome = NanoChrome.From(theme);
      _ws.InvokeAsync(() =>
      {
        _header.BackgroundColor = _chrome.HeaderBackground;
        _header.SetContent([HeaderText(_selected)]);
        _shortcuts.BackgroundColor = _chrome.BarBackground;
        _shortcuts.SetContent(ShortcutRows());
        if (_contentGrid is not null) _contentGrid.GridlineColor = _chrome.Separator;
        if (_lastMessage.Length > 0)
          _message.SetContent([_chrome.Status(_lastMessage, _lastSeverity)]);
        ApplyFilter(_filter.Input, keepSelection: true);
      });
    }

    public void Show()
    {
      //_ws.RegisterGlobalShortcut(ConsoleModifiers.Control, ConsoleKey.S, () => _ = SaveCurrentAsync());
      //_ws.RegisterGlobalShortcut(ConsoleModifiers.Control, ConsoleKey.O, OpenSettings);
      //_ws.RegisterGlobalShortcut(ConsoleModifiers.Control, ConsoleKey.R, () => _ = LoadTemplatesAsync());
      //_ws.RegisterGlobalShortcut(ConsoleModifiers.Control, ConsoleKey.X, () => _ws.Shutdown());

      _ws.AddWindow(_window);
      _window.State = WindowState.Maximized;
      _window.FocusControl(_filter);
      Say(_setup.IsConfigured ? $"Logged in as {_setup.Username}" : "No Imgflip login, memes get the imgflip watermark");
      _ = LoadTemplatesAsync();
      _ = RefreshPremiumAsync();
    }

    private async Task RefreshPremiumAsync()
    {
      bool premium = await _imgflip.CheckPremiumAsync();
      Debug(premium ? "Premium account: GIF templates available" : "No premium: GIF templates hidden");

      await _ws.InvokeAsync(() =>
      {
        _shortcuts.SetContent(ShortcutRows());
        if (GifMode && !premium) ToggleMode();
      });
    }

    private List<string> ShortcutRows() =>
    [
      _chrome.Key("F5", "Caption") + _chrome.Key("F6", "Copy image") + _chrome.Key("F7", "Save") + _chrome.Key("F8", "Settings") + _chrome.Key("F10", "Feed") + _chrome.Key("F12", "Upload") + (GifsAvailable ? _chrome.Key("F2", GifMode ? "Images" : "GIFs") : string.Empty),
      _chrome.Key("F9", "Reload") + _chrome.Key("F3", "Theme") + _chrome.Key("F1", "Help") + _chrome.Key("F4", "Exit"),
    ];

    private string HeaderText(Meme? meme)
    {
      int width = Math.Max(20, _ws.ConsoleDriver.ScreenSize.Width);
      string left = GifMode ? " GIF " : " Image ";
      string center = meme is null
        ? (GifMode ? "New GIF meme" : "New meme")
        : $"{meme.Name}  ({meme.BoxCount} boxes, {meme.Width}x{meme.Height})";
      string right = _resultUrl is null ? " " : " Created ";
      return _chrome.Header(left, center, right, width);
    }

    private void RefreshHeader() => _ws.InvokeAsync(() => _header.SetContent([HeaderText(_selected)]));

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
      switch (e.KeyInfo.Key)
      {
        case ConsoleKey.F1: ShowHelp();
          break;
        case ConsoleKey.F2 when GifsAvailable: ToggleMode();
          break;
        case ConsoleKey.F3: CycleTheme(e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));
          break;
        case ConsoleKey.F4: _ws.Shutdown();
          break;
        case ConsoleKey.F5: _ = OpenCaptionsAsync();
          break;
        case ConsoleKey.F6: _ = CopyResultImageAsync();
          break;
        case ConsoleKey.F7: _ = SaveCurrentAsync();
          break;
        case ConsoleKey.F8: OpenSettings();
          break;
        case ConsoleKey.F9: _ = LoadTemplatesAsync();
          break;
        case ConsoleKey.F10: OpenDoomScroll();
          break;
        case ConsoleKey.F12: _ = UploadResultAsync();
          break;
        case ConsoleKey.Escape: _window.FocusControl(_filter);
          break;
        //case ConsoleKey.C when ctrl: _ = CopyResultImageAsync(); e.Handled = true; break;
        //case ConsoleKey.S when ctrl: _ = SaveCurrentAsync(); e.Handled = true; break;
        //case ConsoleKey.R when ctrl: _ = LoadTemplatesAsync(); e.Handled = true; break;
        //case ConsoleKey.O when ctrl: OpenSettings(); e.Handled = true; break;
        //case ConsoleKey.X when ctrl: _ws.Shutdown(); e.Handled = true; break;
        default:
          throw new ArgumentOutOfRangeException();
      }

      e.Handled = true;
    }

    private void ShowHelp() =>
      Say(GifsAvailable
        ? "Type to filter, Enter or F5 to caption, F2 switches image/GIF templates, F7 saves, F6 copies the image, F12 uploads to the feed, F10 opens the feed"
        : "Type to filter, Enter or F5 to caption, F7 saves, F6 copies the image, F12 uploads to the feed, F10 opens the feed");

    private void ToggleMode()
    {
      _mode = GifMode ? EMemeTyp.Image : EMemeTyp.Gif;
      _selected = null;
      _resultUrl = null;
      _lastCaptions = null;
      _previewCts?.Cancel();
      _preview.Clear();
      _templates.Title = ListTitle();
      _shortcuts.SetContent(ShortcutRows());
      RefreshHeader();
      Say(GifMode ? "GIF templates" : "Image templates");
      _ = LoadTemplatesAsync();
    }

    private void CycleTheme(bool backward)
    {
      string name = AppThemes.Next(_ws, backward);
      AppThemes.Save(_configStore, name);
      Say($"Theme: {name}");
    }

    private void OpenSettings() =>
      new SettingsScreen(_ws, _configStore, _setup, () =>
      {
        Say(_setup.IsConfigured ? $"Logged in as {_setup.Username}" : "No Imgflip login");
        _feed = new MemeFeedClient(_configStore.Load().Feed.BaseUrl);
        _imgflip.ResetPremium();
        _ = RefreshPremiumAsync();
      }).Show();

    private void OpenDoomScroll() =>
      new DoomScrollScreen(_ws, _cache, _feed, _setup.IsConfigured ? _setup.Username : null).Show();

    private async Task UploadResultAsync()
    {
      if (_resultUrl is null) { Say("Create a meme first", NotificationSeverity.Warning); return; }
      if (!_setup.IsConfigured) { Say("Login (F8) to upload to the feed", NotificationSeverity.Warning); return; }
      if (_busy) { Say("Busy", NotificationSeverity.Warning); return; }

      _busy = true;
      Say("Uploading to feed...");
      try
      {
        string path = await _cache.GetAsync(_resultUrl);
        byte[] data = await File.ReadAllBytesAsync(path);
        (long id, bool duplicate) = await _feed.UploadAsync(_setup.Username!, data, MemeFeedClient.ContentTypeFor(path));
        Say(duplicate ? $"Already in the feed as #{id}" : $"Uploaded to feed as #{id}", duplicate ? NotificationSeverity.Warning : NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        Say($"Upload failed: {ex.Message}", NotificationSeverity.Danger);
      }
      finally
      {
        _busy = false;
      }
    }

    private void Say(string text, NotificationSeverity? severity = null)
    {
      Logger.Info(text);
      Debug(text);

      _lastMessage = text;
      _lastSeverity = severity;

      _messageCts?.Cancel();
      CancellationTokenSource cts = new();
      _messageCts = cts;

      _ws.InvokeAsync(() => _message.SetContent([_chrome.Status(text, severity)]));
      _ = Task.Delay(TimeSpan.FromSeconds(6), cts.Token).ContinueWith(t =>
      {
        if (t.IsCanceled) return;
        _lastMessage = string.Empty;
        _ws.InvokeAsync(() => _message.SetContent([string.Empty]));
      }, cts.Token);
    }

    private void Debug(string text)
    {
      if (_debugLog is null) return;
      _ws.InvokeAsync(() =>
      {
        _debugLines.Add($"{_chrome.MutedText(DateTime.Now.ToString("HH:mm:ss"))} {MarkupParser.Escape(text)}");
        if (_debugLines.Count > MaxDebugLines)
          _debugLines.RemoveRange(0, _debugLines.Count - MaxDebugLines);
        _debugLog.SetContent(_debugLines);
      });
    }

    private async Task LoadTemplatesAsync()
    {
      EMemeTyp mode = _mode;
      try
      {
        Meme[] memes = await _imgflip.GetMemes(mode);
        if (mode != _mode) return;
        _knownIds.Clear();
        _searchedQueries.Clear();
        _searchUnavailable = false;
        foreach (Meme m in memes) _knownIds.Add(m.Id);
        _allMemes = memes;
        Say($"Loaded {_allMemes.Length} {(GifMode ? "GIF" : "image")} templates");
        await _ws.InvokeAsync(() => ApplyFilter(_filter.Input));
      }
      catch (Exception ex)
      {
        Say($"Could not load templates: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private void ScheduleSearch(string text)
    {
      _searchCts?.Cancel();
      string query = text.Trim();
      if (query.Length < 2 || !_imgflip.IsAuthenticated) return;

      CancellationTokenSource cts = new();
      _searchCts = cts;
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(400, cts.Token);
          await SearchAndMergeAsync(query, cts.Token);
        }
        catch (OperationCanceledException) { }
      }, cts.Token);
    }

    private Task LoadMoreAsync()
    {
      string query = _filter.Input.Trim();
      if (query.Length == 0) query = "meme";

      if (_imgflip.IsAuthenticated) return SearchAndMergeAsync(query, CancellationToken.None);
      if (_searchUnavailable) return Task.CompletedTask;
      _searchUnavailable = true;
      Say("End of the free template list. Searching more templates needs an Imgflip login (F8)", NotificationSeverity.Warning);
      return Task.CompletedTask;

    }

    private async Task SearchAndMergeAsync(string query, CancellationToken ct)
    {
      if (_loadingMore || _searchUnavailable) return;
      if (_searchedQueries.Contains(query)) return;

      EMemeTyp mode = _mode;
      _loadingMore = true;
      Debug($"Searching {(GifMode ? "GIF" : "image")} templates for {query}");

      try
      {
        Meme[] found = await _imgflip.SearchMemes(query, mode, _configStore.Load().ImgFlip.IncludeNsfw);
        if (ct.IsCancellationRequested || mode != _mode) return;
        _searchedQueries.Add(query);

        List<Meme> added = new();
        foreach (Meme m in found)
          if (_knownIds.Add(m.Id)) added.Add(m);

        if (added.Count == 0)
        {
          Debug($"No new templates for {query}");
          return;
        }

        _allMemes = [.. _allMemes, .. added];
        Say($"Found {added.Count} more templates for \"{query}\"");

        await _ws.InvokeAsync(() =>
        {
          Meme? keep = _selected;
          ApplyFilter(_filter.Input, keepSelection: true);
          int index = keep is null ? -1 : _templates.Items.FindIndex(i => ReferenceEquals(i.Tag, keep));
          if (index >= 0) _templates.SelectedIndex = index;
          else if (_templates.Items.Count > 0 && _templates.SelectedIndex < 0) _templates.SelectedIndex = 0;
        });
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        _searchUnavailable = true;
        Say($"Template search unavailable: {ex.Message}", NotificationSeverity.Warning);
      }
      finally
      {
        _loadingMore = false;
      }
    }

    private void ApplyFilter(string text, bool keepSelection = false)
    {
      string needle = text.Trim();
      IEnumerable<Meme> visible = string.IsNullOrEmpty(needle)
        ? _allMemes
        : _allMemes.Where(m => m.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));

      var items = visible
        .Select(meme => new ListItem($"{MarkupParser.Escape(meme.Name)} {_chrome.MutedText($"({meme.BoxCount})")}{(GifMode ? " " + _chrome.InfoText("gif") : string.Empty)}") { Tag = meme })
        .ToList();

      _templates.Items = items;
      if (items.Count > 0 && !keepSelection)
        _templates.SelectedIndex = 0;
    }

    private void OnTemplateSelected(ListItem? item)
    {
      if (item is not null && _templates.Items.Count > 0 && _templates.SelectedIndex >= _templates.Items.Count - 1)
        _ = LoadMoreAsync();

      if (item?.Tag is not Meme meme || ReferenceEquals(meme, _selected)) return;

      _selected = meme;
      _resultUrl = null;
      _lastCaptions = null;
      RefreshHeader();

      _previewCts?.Cancel();
      CancellationTokenSource cts = new();
      _previewCts = cts;
      _ = LoadTemplatePreviewAsync(meme, cts.Token);
    }

    private async Task LoadTemplatePreviewAsync(Meme meme, CancellationToken ct)
    {
      try
      {
        await Task.Delay(120, ct);
        string path = await _cache.GetAsync(meme.Url, ct);
        if (ct.IsCancellationRequested) return;
        await _preview.LoadWhenReadyAsync(path, ct: ct);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        Say($"Template download failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private async Task OpenCaptionsAsync()
    {
      if (_busy) { Say("Busy", NotificationSeverity.Warning); return; }
      if (_selected is null) { Say("Select a template first", NotificationSeverity.Warning); return; }
      Meme meme = _selected;
      string? imagePath = _cache.TryGetPath(meme.Url);
      bool customDefault = _configStore.Load().ImgFlip.CustomBoxPositions;
      MemeCreationBox[]? boxes = await new MemeCreationScreen(_ws, meme, imagePath, customDefault, _lastCaptions).ShowAsync();
      if (boxes is null) return;

      _lastCaptions = boxes;
      await CreateMemeAsync(meme, boxes);
    }

    private async Task CreateMemeAsync(Meme meme, MemeCreationBox[] boxes)
    {
      if (_busy) { Say("Busy", NotificationSeverity.Warning); return; }

      _busy = true;
      Say($"Creating {(GifMode ? "GIF" : "meme")} with {meme.Name}...");

      try
      {
        ImgFlipConfig options = _configStore.Load().ImgFlip;
        await _previewCts?.CancelAsync()!;

        bool? noWatermark = options.NoWatermark && _imgflip.IsAuthenticated ? true : null;
        bool positioned = boxes.Any(b => b.X is not null);

        string url;
        if (GifMode)
          url = await _imgflip.CaptionGif(meme.Id, boxes, options.MaxFontSize, noWatermark);
        else if (!positioned && boxes.Length <= 2)
          url = await _imgflip.CaptionImage(meme.Id, boxes.ElementAtOrDefault(0)?.Text ?? string.Empty, boxes.ElementAtOrDefault(1)?.Text ?? string.Empty, options.MaxFontSize, noWatermark);
        else
          url = await _imgflip.CaptionImage(meme.Id, string.Empty, string.Empty, options.MaxFontSize, noWatermark, boxes);

        _resultUrl = url;
        RefreshHeader();
        Say($"Created {url}", NotificationSeverity.Success);

        string path = await _cache.GetAsync(url);
        await _preview.LoadWhenReadyAsync(path);
      }
      catch (Exception ex)
      {
        Say($"Create failed: {ex.Message}", NotificationSeverity.Danger);
      }
      finally
      {
        _busy = false;
      }
    }

    private async Task CopyResultImageAsync()
    {
      if (_resultUrl is null) { Say("No meme created yet", NotificationSeverity.Warning); return; }
      try
      {
        string path = await _cache.GetAsync(_resultUrl);
        if (await ImageClipboard.CopyFileAsync(path))
        {
          Say("Image copied to clipboard", NotificationSeverity.Success);
          return;
        }
        ClipboardHelper.SetText(_resultUrl);
        Say("Image copy unsupported here, URL copied instead", NotificationSeverity.Warning);
      }
      catch (Exception ex)
      {
        Say($"Copy failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private async Task SaveCurrentAsync()
    {
      string? sourceUrl = _resultUrl ?? _selected?.Url;
      if (sourceUrl is null) { Say("Nothing to save yet", NotificationSeverity.Warning); return; }

      string defaultName = Path.GetFileName(new Uri(sourceUrl).AbsolutePath);
      if (_resultUrl is null && _selected is not null)
        defaultName = $"{Sanitize(_selected.Name)}{Path.GetExtension(defaultName)}";

      Say("Opening file explorer...");
      string? target = await NativeFileDialog.SaveFileAsync(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        defaultName);

      if (target is null) { Say("Save cancelled"); return; }

      try
      {
        Say("Downloading...");
        await FileDownloader.DownloadAsync(sourceUrl, target, new Progress<(long received, long? total)>(p =>
        {
          if (p.total is { } total and > 0) Say($"Downloading {p.received * 100 / total}%");
        }));
        Say($"Saved {target}", NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        Say($"Save failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private static string Sanitize(string name)
    {
      char[] invalid = Path.GetInvalidFileNameChars();
      return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }
  }
}
