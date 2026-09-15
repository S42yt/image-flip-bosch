using image_flip_bosch.CLI.TUI.Core;
using image_flip_bosch.CLI.Utils.Ai;
using image_flip_bosch.CLI.Utils.Image;
using image_flip_bosch.CLI.Utils.MemeFeed;
using image_flip_bosch.CLI.Utils.Native;
using image_flip_bosch.ImgFlip.Auth;
using image_flip_bosch.ImgFlip.Requests;
using OpenAI.Chat;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using System.Text.Json;

namespace image_flip_bosch.CLI.TUI
{
  internal sealed record AiChatResult(string? Url, Meme? Meme, string[]? Captions);

  internal sealed class AiChatScreen : NanoScreen
  {
    private const string CaptionMemeToolName = "caption_meme";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static readonly ChatTool CaptionMemeTool = ChatTool.CreateFunctionTool(
      functionName: CaptionMemeToolName,
      functionDescription: "Create the meme by putting captions on the template the user has open. Call it once per request. Afterwards tell the user the meme is ready.",
      functionParameters: BinaryData.FromString("""
      {
        "type": "object",
        "properties": {
          "captions": { "type": "array", "items": { "type": "string" }, "description": "One caption per text box of the current template, in order. At least 1, at most box_count." },
          "meme_id": { "type": "string", "description": "Only set this if the user explicitly asked for a different template from the list. Leave it out to use the current one." }
        },
        "required": ["captions"]
      }
      """));

    private readonly Meme _meme;
    private readonly CreationContext _ctx;
    private readonly List<ChatMessage> _history;
    private readonly ChatTranscriptControl _transcript;
    private readonly PromptControl _input;
    private readonly MarkupControl _status;
    private readonly MarkupControl _resultLine;
    private readonly ProgressBarControl _progress;
    private readonly ImagePreview _preview;
    private readonly TaskCompletionSource<AiChatResult> _result = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _busy;
    private bool _starting;
    private string? _url;
    private Meme? _urlMeme;
    private string[]? _captions;

    public AiChatScreen(ConsoleWindowSystem ws, Meme meme, CreationContext ctx, List<ChatMessage> history)
      : base(ws, "AI Assistant")
    {
      _meme = meme;
      _ctx = ctx;
      _history = history;
      if (_history.Count == 0) _history.Add(new SystemChatMessage(BuildSystemPrompt()));

      _transcript = new ChatTranscriptControl
      {
        ShowScrollbar = true,
        MessageRailEnabled = true,
        AnimateMessages = true,
        ThinkingSpinnerStyle = SpinnerStyle.Dots,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };
      ApplyRoleStyles();

      _status = Controls.Markup(Chrome.MutedText(" Starting AI...")).Build();
      _resultLine = Controls.Markup(Chrome.MutedText(" No meme yet. Ask for captions, the result shows on the right.")).WithAlignment(HorizontalAlignment.Center).Build();
      _progress = Controls.ProgressBar().WithHeader("AI").Indeterminate().ShowPercentage().Stretch().Build();
      _progress.Visible = false;
      _input = Controls.Prompt(" ❯ ")
        .WithPlaceholder("e.g. make it about monday meetings")
        .UnfocusOnEnter(false)
        .OnEntered((_, _) => _ = SendAsync())
        .Build();
      _input.IsEnabled = false;

      MarkupControl templateLine = Controls.Markup(
        $" {Chrome.SectionText("Template")} {Chrome.HighlightText(meme.Name)} {Chrome.MutedText($"{meme.BoxCount} text box{(meme.BoxCount == 1 ? "" : "es")}, {meme.Width}x{meme.Height}")}").Build();

      GridControl chat = Controls.Grid()
        .Columns(GridLength.Star())
        .Rows(GridLength.Auto(), GridLength.Auto(), GridLength.Star(), GridLength.Auto(), GridLength.Auto(), GridLength.Auto())
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      chat.Place(templateLine, 0, 0);
      chat.Place(Controls.Markup(string.Empty).Build(), 1, 0);
      chat.Place(_transcript, 2, 0);
      chat.Place(_progress, 3, 0);
      chat.Place(_status, 4, 0);
      chat.Place(_input, 5, 0);

      GridControl right = Controls.Grid()
        .Columns(GridLength.Star())
        .Rows(GridLength.Auto(), GridLength.Star(), GridLength.Auto())
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      right.Place(Controls.Markup($" {Chrome.SectionText("Result")}").Build(), 0, 0);
      _preview = new ImagePreview(ws)
      {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Fill,
      };
      _preview.LoadFailed += (_, msg) => Say($"Preview failed: {msg}", NotificationSeverity.Danger);
      right.Place(_preview, 1, 0);
      right.Place(_resultLine, 2, 0);

      GridControl body = Controls.Grid()
        .Columns(GridLength.Star(11), GridLength.Star(9))
        .Rows(GridLength.Star())
        .ColumnGap(1)
        .ColumnGridlines()
        .GridlineStyle(BorderStyle.Single)
        .GridlineColor(Chrome.Separator)
        .WithAlignment(HorizontalAlignment.Stretch)
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .Build();
      body.Place(chat, 0, 0);
      body.Place(right, 0, 1);

      BuildWindow([body], modal: true);
      _ctx.Ai.StatusChanged += OnAiStatus;
      _ctx.Ai.Progress += OnAiProgress;
      Window.OnClosed += (_, _) =>
      {
        _ctx.Ai.StatusChanged -= OnAiStatus;
        _ctx.Ai.Progress -= OnAiProgress;
        _cts.Cancel();
        _result.TrySetResult(new AiChatResult(_url, _urlMeme, _captions));
      };
    }

    protected override string HeaderCenter => $"AI Assistant  ·  {_meme.Name}";

    protected override IEnumerable<(string Key, string Label)> Shortcuts =>
    [
      ("Enter", "Send"),
      ("F9", "Copy"),
      ("F10", "Save"),
      ("F12", "Upload"),
      ("F3", "Theme"),
      ("Esc", "Back"),
    ];

    public Task<AiChatResult> ShowAsync()
    {
      Show();
      RestoreTranscript();
      if (_ctx.Ai.IsReady) EnableInput();
      else StartAi();
      return _result.Task;
    }

    protected override void OnKey(KeyPressedEventArgs e)
    {
      switch (e.KeyInfo.Key)
      {
        case ConsoleKey.Escape: Window.Close(); e.Handled = true; break;
        case ConsoleKey.F9: _ = CopyAsync(); e.Handled = true; break;
        case ConsoleKey.F10: _ = SaveAsync(); e.Handled = true; break;
        case ConsoleKey.F12: _ = UploadAsync(); e.Handled = true; break;
        case ConsoleKey.F3:
          AppThemes.Next(Ws, e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));
          e.Handled = true;
          break;
      }
    }

    protected override void OnChromeChanged() => ApplyRoleStyles();

    private void ApplyRoleStyles()
    {
      _transcript.MessageRailColor = Chrome.Separator;
      _transcript.SetRoleStyle(ChatRole.User, new ChatRoleStyle
      {
        HeaderStyle = CollapsibleHeaderStyle.Rounded,
        BorderColor = Chrome.Accent,
        ShowHeader = true,
        Header = (_, author) => $"[{Chrome.Accent.ToMarkup()} bold] {author ?? "You"} [/]",
        Markdown = false,
      });
      _transcript.SetRoleStyle(ChatRole.Assistant, new ChatRoleStyle
      {
        HeaderStyle = CollapsibleHeaderStyle.Rounded,
        BorderColor = Chrome.Info,
        ShowHeader = true,
        Header = (_, author) => $"[{Chrome.Info.ToMarkup()} bold] {author ?? "AI"} [/]",
        Markdown = false,
      });
      _transcript.SetRoleStyle(ChatRole.Tool, new ChatRoleStyle
      {
        HeaderStyle = CollapsibleHeaderStyle.Rounded,
        BorderColor = Chrome.Success,
        ShowHeader = true,
        Header = (_, author) => $"[{Chrome.Success.ToMarkup()} bold] {author ?? "Imgflip"} [/]",
        Markdown = false,
      });
      _transcript.SetRoleStyle(ChatRole.Error, new ChatRoleStyle
      {
        HeaderStyle = CollapsibleHeaderStyle.Bordered,
        BorderColor = Chrome.Danger,
        ShowHeader = true,
        Header = (_, _) => $"[{Chrome.Danger.ToMarkup()} bold] Error [/]",
        Markdown = false,
      });
    }

    private string BuildSystemPrompt()
    {
      var others = _ctx.Memes
        .Where(m => m.Id != _meme.Id)
        .Take(40)
        .Select(m => new { id = m.Id, name = m.Name, box_count = m.BoxCount });
      var current = new { id = _meme.Id, name = _meme.Name, box_count = _meme.BoxCount };
      return "You are a meme caption assistant inside a terminal app. Keep answers short and punchy. " +
        $"The user has this template open: {JsonSerializer.Serialize(current, JsonOptions)}. " +
        "Always caption this template. Never switch templates unless the user explicitly names another one. " +
        "When the user wants a meme, call caption_meme with one caption per text box. " +
        "Other templates, only for explicit requests: " + JsonSerializer.Serialize(others, JsonOptions);
    }

    private void RestoreTranscript()
    {
      foreach (ChatMessage m in _history)
      {
        string text = string.Concat(m.Content.Where(p => p.Kind == ChatMessageContentPartKind.Text).Select(p => p.Text));
        switch (m)
        {
          case UserChatMessage: AddMessage(ChatRole.User, ChatMarkup.Render(text), "You"); break;
          case AssistantChatMessage when text.Length > 0: AddMessage(ChatRole.Assistant, ChatMarkup.Render(text), "AI"); break;
        }
      }
      if (_transcript.MessageIds.Count == 0)
        AddMessage(ChatRole.Assistant, ChatMarkup.Render($"Hi! I caption **{_meme.Name}** for you. Tell me the topic or the joke and I build the meme."), "AI");
    }

    private ChatMessageId AddMessage(ChatRole role, string markup, string? author = null) =>
      _transcript.AddMessage(role, markup, author: author, actions: [], status: null, markdown: false);

    private void StartAi()
    {
      if (_starting) return;
      _starting = true;
      _progress.Visible = true;
      _ = Task.Run(async () =>
      {
        try
        {
          await _ctx.Ai.EnsureReadyAsync(_cts.Token);
          RunOnUi(EnableInput);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
          RunOnUi(() =>
          {
            _progress.Visible = false;
            _status.SetContent([$"[{Chrome.Danger.ToMarkup()}] AI unavailable[/]"]);
            AddMessage(ChatRole.Error, ChatMarkup.Render(ex.Message));
          });
        }
        finally
        {
          _starting = false;
        }
      });
    }

    private void EnableInput()
    {
      _progress.Visible = false;
      _input.IsEnabled = true;
      _status.SetContent([_ctx.Imgflip.IsAuthenticated
        ? Chrome.MutedText(" Ready. Enter sends.")
        : $"[{Chrome.Warning.ToMarkup()}] Not logged in to Imgflip: captions only, no meme creation. Esc, Esc, F8 to log in.[/]"]);
      Window.FocusControl(_input);
    }

    private void OnAiStatus(AiState state, string text) => RunOnUi(() =>
    {
      _status.SetContent([state == AiState.Failed ? $"[{Chrome.Danger.ToMarkup()}] {text}[/]" : Chrome.MutedText($" {text}")]);
      _progress.Header = text;
      _progress.IsIndeterminate = true;
    });

    private void OnAiProgress(DownloadProgress p)
    {
      if (p.TotalBytes <= 0) return;
      RunOnUi(() =>
      {
        _progress.IsIndeterminate = false;
        _progress.MaxValue = p.TotalBytes;
        _progress.Value = p.BytesDownloaded;
        _progress.Header = $"{p.Label} {p.BytesDownloaded / 1048576} / {p.TotalBytes / 1048576} MB";
      });
    }

    private async Task SendAsync()
    {
      if (!_ctx.Ai.IsReady || _busy) return;
      string text = _input.Input.Trim();
      if (text.Length == 0) return;

      _busy = true;
      _input.Input = string.Empty;
      _input.IsEnabled = false;
      _history.Add(new UserChatMessage(text));
      ChatMessageId replyId = default;
      bool firstToken = true;
      RunOnUi(() =>
      {
        AddMessage(ChatRole.User, ChatMarkup.Render(text), "You");
        replyId = AddMessage(ChatRole.Assistant, Chrome.MutedText("thinking..."), "AI");
        _status.SetContent([Chrome.MutedText(" AI is thinking...")]);
      });

      try
      {
        await _ctx.Ai.ChatAsync(
          _history,
          CaptionMemeTool,
          ExecuteToolAsync,
          full => RunOnUi(() =>
          {
            firstToken = false;
            _transcript.UpdateMessage(replyId, ChatMarkup.Render(full));
          }),
          _ => RunOnUi(() =>
          {
            if (firstToken) _transcript.UpdateMessage(replyId, Chrome.MutedText("captioning..."));
            _transcript.SetStatus(replyId, "creating meme on Imgflip...", NotificationSeverity.Info);
          }),
          _cts.Token);
        RunOnUi(() =>
        {
          _transcript.ClearStatus(replyId);
          if (firstToken) _transcript.UpdateMessage(replyId, Chrome.MutedText("done"));
        });
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        RunOnUi(() => AddMessage(ChatRole.Error, ChatMarkup.Render($"Chat failed: {ex.Message}")));
      }
      finally
      {
        _busy = false;
        RunOnUi(() =>
        {
          _input.IsEnabled = true;
          _status.SetContent([Chrome.MutedText(" Ready. Enter sends.")]);
          Window.FocusControl(_input);
        });
      }
    }

    private async Task<string> ExecuteToolAsync(string name, string argumentsJson)
    {
      if (name != CaptionMemeToolName) return $"Unknown tool '{name}'.";

      if (!_ctx.Imgflip.IsAuthenticated)
      {
        Say("Login (Esc, Esc, then F8) to let the AI create memes", NotificationSeverity.Warning);
        RunOnUi(() => AddMessage(ChatRole.Error, "Not logged in to Imgflip, so I cannot create the image. Press Esc twice, then F8 to log in. The captions are still usable in the editor."));
        return "The user is not logged in to Imgflip, no meme can be created. Answer with the captions as plain text instead.";
      }

      try
      {
        using JsonDocument args = JsonDocument.Parse(argumentsJson);
        string? memeId = args.RootElement.TryGetProperty("meme_id", out JsonElement idEl) ? idEl.GetString() : null;
        Meme meme = string.IsNullOrWhiteSpace(memeId) || memeId == _meme.Id
          ? _meme
          : _ctx.Memes.FirstOrDefault(m => m.Id == memeId) ?? _meme;

        List<string> captions = [];
        if (args.RootElement.TryGetProperty("captions", out JsonElement captionsEl) && captionsEl.ValueKind == JsonValueKind.Array)
          captions.AddRange(captionsEl.EnumerateArray().Select(c => c.GetString() ?? string.Empty));

        if (captions.Count < 1 || captions.Count > meme.BoxCount)
          return $"'{meme.Name}' takes between 1 and {meme.BoxCount} captions, got {captions.Count}. Call caption_meme again with the right amount.";

        MemeCreationBox[] boxes = captions.Select(c => new MemeCreationBox { Text = c }).ToArray();
        bool? noWatermark = _ctx.Options.NoWatermark ? true : null;
        string url = await _ctx.Imgflip.CaptionImage(meme.Id, string.Empty, string.Empty, _ctx.Options.MaxFontSize, noWatermark, boxes);

        _url = url;
        _urlMeme = meme;
        _captions = [.. captions];
        RunOnUi(() =>
        {
          string lines = string.Join("\n", captions.Select((c, i) => $"{Chrome.MutedText($"{i + 1}.")} {SharpConsoleUI.Parsing.MarkupParser.Escape(c)}"));
          ChatMessageId id = AddMessage(ChatRole.Tool, $"{Chrome.HighlightText(meme.Name)}\n{lines}", "Meme created");
          _transcript.SetActions(id,
          [
            new ChatMessageAction { Id = "copy", Label = "Copy", Icon = "⎘", Variant = ChatActionVariant.Primary, OnClick = c => { _ = CopyAsync(); } },
            new ChatMessageAction { Id = "save", Label = "Save", Icon = "💾", OnClick = c => { _ = SaveAsync(); } },
            new ChatMessageAction { Id = "upload", Label = "Upload", Icon = "⇧", OnClick = c => { _ = UploadAsync(); } },
          ]);
          _resultLine.SetContent([$" {Chrome.HighlightText(meme.Name)}  {Chrome.MutedText("F9 copy  F10 save  F12 upload  Esc back to editor")}"]);
        });
        Say("Meme ready: F9 copy, F10 save, F12 upload", NotificationSeverity.Success);
        _ = ShowResultAsync(url);
        return $"Meme created on template '{meme.Name}'. It is shown to the user. Tell them briefly what you did.";
      }
      catch (ImgFlipException ex)
      {
        RunOnUi(() => AddMessage(ChatRole.Error, ChatMarkup.Render($"Imgflip refused: {ex.Message}")));
        return $"Imgflip refused the request: {ex.Message}";
      }
      catch (Exception ex)
      {
        RunOnUi(() => AddMessage(ChatRole.Error, ChatMarkup.Render($"Failed to create meme: {ex.Message}")));
        return $"Failed to create meme: {ex.Message}";
      }
    }

    private async Task ShowResultAsync(string url)
    {
      try
      {
        string path = await _ctx.Cache.GetAsync(url);
        await _preview.LoadWhenReadyAsync(path);
      }
      catch (Exception ex)
      {
        Say($"Preview failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private async Task CopyAsync()
    {
      if (_url is null) { Say("No meme yet, ask the assistant first", NotificationSeverity.Warning); return; }
      try
      {
        string path = await _ctx.Cache.GetAsync(_url);
        if (await ImageClipboard.CopyFileAsync(path))
        {
          Say("Image copied to clipboard", NotificationSeverity.Success);
          return;
        }
        ClipboardHelper.SetText(_url);
        Say("Image copy unsupported here, URL copied instead", NotificationSeverity.Warning);
      }
      catch (Exception ex)
      {
        Say($"Copy failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private async Task SaveAsync()
    {
      if (_url is null) { Say("No meme yet, ask the assistant first", NotificationSeverity.Warning); return; }
      Say("Opening file explorer...");
      string? target = await NativeFileDialog.SaveFileAsync(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Path.GetFileName(new Uri(_url).AbsolutePath));
      if (target is null) { Say("Save cancelled"); return; }
      try
      {
        await FileDownloader.DownloadAsync(_url, target);
        Say($"Saved {target}", NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        Say($"Save failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private async Task UploadAsync()
    {
      if (_url is null) { Say("No meme yet, ask the assistant first", NotificationSeverity.Warning); return; }
      if (_ctx.User is null) { Say("Login (Esc, Esc, then F8) to upload to the feed", NotificationSeverity.Warning); return; }
      try
      {
        Say("Uploading to feed...");
        string path = await _ctx.Cache.GetAsync(_url);
        byte[] data = await File.ReadAllBytesAsync(path);
        (long id, bool duplicate) = await _ctx.Feed.UploadAsync(_ctx.User, data, MemeFeedClient.ContentTypeFor(path));
        Say(duplicate ? $"Already in the feed as #{id}" : $"Uploaded to feed as #{id}", duplicate ? NotificationSeverity.Warning : NotificationSeverity.Success);
      }
      catch (Exception ex)
      {
        Say($"Upload failed: {ex.Message}", NotificationSeverity.Danger);
      }
    }

    private void RunOnUi(Action action)
    {
      if (Ws.IsOnUIThread) action();
      else Ws.EnqueueOnUIThread(action);
    }
  }
}
