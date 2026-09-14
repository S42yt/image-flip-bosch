using image_flip_bosch.CLI.TUI.Core;
using image_flip_bosch.CLI.Utils;
using image_flip_bosch.CLI.Utils.Image;
using image_flip_bosch.CLI.Utils.MemeFeed;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Layout;

namespace image_flip_bosch.CLI.TUI
{
  internal sealed class DoomScrollScreen : NanoScreen
  {
    private const int PageSize = 20;

    private readonly ImageCache _cache;
    private readonly MemeFeedClient _feed;
    private readonly string? _user;
    private readonly ImagePreview _preview;
    private readonly MarkupControl _caption;
    private readonly List<MemeFeedItem> _items = [];
    private int _index = -1;
    private bool _loading;
    private bool _exhausted;
    private bool _advanceAfterLoad;
    private bool _voting;
    private CancellationTokenSource? _previewCts;

    public DoomScrollScreen(ConsoleWindowSystem ws, ImageCache cache, MemeFeedClient feed, string? user)
      : base(ws, "Feed")
    {
      _cache = cache;
      _feed = feed;
      _user = user;

      _preview = new ImagePreview(ws)
      {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };
      _preview.LoadFailed += (_, msg) => Say($"Image failed: {msg}", NotificationSeverity.Danger);

      _caption = Controls.Markup(Chrome.MutedText(" Loading feed..."))
        .WithAlignment(HorizontalAlignment.Center)
        .Build();

      GridControl body = Controls.Grid()
        .Columns(GridLength.Star())
        .Rows(GridLength.Star(), GridLength.Auto())
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      body.Place(_preview, 0, 0);
      body.Place(_caption, 1, 0);

      BuildWindow([body], modal: true);
      Window.OnClosed += (_, _) => _previewCts?.Cancel();
    }

    private MemeFeedItem? Current => _index >= 0 && _index < _items.Count ? _items[_index] : null;

    protected override string HeaderCenter => Current is null
      ? "Feed"
      : $"#{Current.Id} by {Current.User}  ({_index + 1}/{_items.Count}{(_exhausted ? string.Empty : "+")})";

    protected override IEnumerable<(string Key, string Label)> Shortcuts =>
    [
      ("↓", "Next"),
      ("↑", "Previous"),
      ("F5", "Upvote"),
      ("F6", "Downvote"),
      ("F9", "Shuffle"),
      ("Esc", "Back"),
    ];

    public new void Show()
    {
      base.Show();
      _ = LoadAsync(reset: true);
    }

    protected override void OnKey(KeyPressedEventArgs e)
    {
      switch (e.KeyInfo.Key)
      {
        case ConsoleKey.DownArrow or ConsoleKey.J or ConsoleKey.Spacebar or ConsoleKey.Enter or ConsoleKey.PageDown:
          Move(1); e.Handled = true; break;
        case ConsoleKey.UpArrow or ConsoleKey.K or ConsoleKey.PageUp:
          Move(-1); e.Handled = true; break;
        case ConsoleKey.Home: if (_items.Count > 0) Select(0); e.Handled = true; break;
        case ConsoleKey.F5 or ConsoleKey.Add or ConsoleKey.OemPlus: _ = VoteAsync(1); e.Handled = true; break;
        case ConsoleKey.F6 or ConsoleKey.Subtract or ConsoleKey.OemMinus: _ = VoteAsync(-1); e.Handled = true; break;
        case ConsoleKey.F9: _ = LoadAsync(reset: true); e.Handled = true; break;
        case ConsoleKey.Escape: Window.Close(); e.Handled = true; break;
      }
    }

    protected override void OnChromeChanged() => _caption.SetContent([CaptionText()]);

    private string CaptionText()
    {
      if (Current is null) return Chrome.MutedText(_items.Count == 0 ? " No memes in the feed yet" : string.Empty);

      string up = Current.MyVote > 0 ? Chrome.AccentText("▲") : Chrome.MutedText("▲");
      string down = Current.MyVote < 0 ? Chrome.AccentText("▼") : Chrome.MutedText("▼");
      string score = Chrome.HighlightText(Current.Score.ToString("+0;-0;0"));
      return $" {up} {score} {down}   {Chrome.HighlightText(Current.User)} {Chrome.MutedText($"{Current.CreatedAt:yyyy-MM-dd HH:mm}  {Current.ContentType}  {Current.Size / 1024} KB")}";
    }

    private void RefreshCaption() => Ws.InvokeAsync(() => _caption.SetContent([CaptionText()]));

    private async Task VoteAsync(int value)
    {
      MemeFeedItem? item = Current;
      if (item is null) { Say("Nothing to vote on", NotificationSeverity.Warning); return; }
      if (_user is null) { Say("Login (F8 on the main screen) to vote", NotificationSeverity.Warning); return; }
      if (_voting) return;

      int target = item.MyVote == value ? 0 : value;
      _voting = true;
      try
      {
        (int score, int myVote) = await _feed.VoteAsync(item.Id, _user, target);
        item.Score = score;
        item.MyVote = myVote;
        if (ReferenceEquals(item, Current)) RefreshCaption();
        Say(target switch { 1 => "Upvoted", -1 => "Downvoted", _ => "Vote removed" }, NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        Say($"Vote failed: {ex.Message}", NotificationSeverity.Danger);
      }
      finally
      {
        _voting = false;
      }
    }

    private void Move(int direction)
    {
      int next = _index + direction;
      if (next < 0) return;
      if (next >= _items.Count)
      {
        if (_exhausted) { Say("Seen everything, F9 shuffles again"); return; }
        _advanceAfterLoad = true;
        _ = LoadAsync(reset: false);
        return;
      }
      Select(next);
    }

    private void Select(int index)
    {
      _index = index;
      RefreshHeader();
      RefreshCaption();

      if (!_exhausted && index >= _items.Count - 3) _ = LoadAsync(reset: false);

      _previewCts?.Cancel();
      CancellationTokenSource cts = new();
      _previewCts = cts;
      _ = LoadPreviewAsync(_items[index], cts.Token);
    }

    private async Task LoadPreviewAsync(MemeFeedItem item, CancellationToken ct)
    {
      try
      {
        string path = await _cache.GetAsync(_feed.ImageUrl(item.Id), ct);
        if (ct.IsCancellationRequested) return;
        await _preview.LoadWhenReadyAsync(path, ct: ct);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        Say($"Download failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private async Task LoadAsync(bool reset)
    {
      if (_loading) return;
      _loading = true;

      await Ws.InvokeAsync(() => {
        _caption.SetContent([Chrome.MutedText(" Loading feed...")]);
      });

      try
      {
        if (_feed is null)
        {
          throw new InvalidOperationException("MemeFeedClient is not initialized.");
        }

        IEnumerable<long> exclude = reset ? [] : _items.Select(i => i.Id);
        List<MemeFeedItem> page = await _feed.ListAsync(exclude, _user, PageSize);

        await Ws.InvokeAsync(() => {
          if (reset)
          {
            _items.Clear();
            _index = -1;
            _exhausted = false;
            _preview.Clear();
          }

          _items.AddRange(page);
          if (page.Count < PageSize) _exhausted = true;

          if (_items.Count == 0)
          {
            _caption.SetContent([Chrome.MutedText(" No memes in the feed yet")]);
            RefreshHeader();
          }
          else if (_index < 0)
          {
            Say($"Loaded {_items.Count} memes");
            Select(0);
          }
          else if (_advanceAfterLoad && _index + 1 < _items.Count)
          {
            Select(_index + 1);
          }
          else if (_advanceAfterLoad)
          {
            Say("Seen everything, F9 shuffles again");
            RefreshHeader();
          }
        });
      }
      catch (Exception ex)
      {
        await Ws.InvokeAsync(() =>
        {
          string baseUrl = _feed?.BaseUrl?.ToString() ?? "an unknown URL";
          string errorMessage = $"Feed unavailable at {baseUrl}: {ex.Message}";
          _caption.SetContent([Chrome.HighlightText($" Error: {errorMessage}")]);
          RefreshHeader();
        });
      }
      finally
      {
        _advanceAfterLoad = false;
        _loading = false;
      }
    }
  }
}
