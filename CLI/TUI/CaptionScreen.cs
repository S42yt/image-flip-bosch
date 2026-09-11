using image_flip_bosch.ImgFlip;
using image_flip_bosch.ImgFlip.Requests;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Parsing;

namespace image_flip_bosch.CLI.TUI
{

  internal sealed class CaptionScreen
  {
    private readonly ConsoleWindowSystem _ws;
    private readonly List<PromptControl> _inputs = new();
    private readonly Window _window;
    private readonly TaskCompletionSource<string[]?> _result = new();
    private bool _submitted;

    public CaptionScreen(ConsoleWindowSystem ws, Meme meme, string[]? previous = null)
    {
      _ws = ws;

      int count = Math.Clamp(meme.BoxCount, 1, 20);
      List<IWindowControl> controls =
      [
        Controls.Markup(
            $"[bold]{MarkupParser.Escape(meme.Name)}[/] [dim]{count} text box{(count == 1 ? string.Empty : "es")}[/]")
          .Build()
      ];

      for (int i = 0; i < count; i++)
      {
        int index = i;
        PromptControl prompt = Controls.Prompt($" {Label(i, count)} ")
          .WithPlaceholder(i == 0 && count > 1 ? "leave empty to skip" : "text")
          .UnfocusOnEnter(false)
          .OnEntered((_, _) => Advance(index))
          .Build();
        if (previous is not null && i < previous.Length)
          prompt.Input = previous[i];
        _inputs.Add(prompt);
        controls.Add(prompt);
      }

      controls.Add(Controls.Markup("[dim]Enter jumps to the next box, Enter on the last box creates. Esc cancels.[/]").Build());

      controls.Add(Controls.HorizontalGrid()
        .Column(c => c.Add(Controls.Button("Create").OnClick((_, _) => Submit()).Build()))
        .Column(c => c.Add(Controls.Button("Cancel").OnClick((_, _) => _window?.Close()).Build()))
        .Build());

      _window = new WindowBuilder(ws)
        .WithTitle("Caption")
        .WithSize(70, Math.Min(count + 8, 30))
        .Centered()
        .AsModal()
        .Resizable(false)
        .Minimizable(false)
        .Maximizable(false)
        .AddControls(controls.ToArray())
        .OnClosed((_, _) => _result.TrySetResult(_submitted ? Collect() : null))
        .Build();

      _window.PreviewKeyPressed += (_, e) =>
      {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { _window.Close(); e.Handled = true; }
        else if (e.KeyInfo.Key == ConsoleKey.F5) { Submit(); e.Handled = true; }
      };
    }

    public Task<string[]?> ShowAsync()
    {
      _ws.AddWindow(_window);
      _window.FocusControl(_inputs[0]);
      return _result.Task;
    }

    private static string Label(int index, int count) => count switch
    {
      1 => "Text",
      2 => index == 0 ? "Top" : "Bottom",
      _ => $"Box {index + 1}",
    };

    private void Advance(int index)
    {
      if (index + 1 < _inputs.Count)
        _window.FocusControl(_inputs[index + 1]);
      else
        Submit();
    }

    private string[] Collect() => _inputs.Select(p => p.Input.Trim()).ToArray();

    private void Submit()
    {
      if (Collect().All(t => t.Length == 0))
      {
        _ws.ToastService.Show("Enter at least one caption", SharpConsoleUI.Core.NotificationSeverity.Warning);
        return;
      }
      _submitted = true;
      _window.Close();
    }
  }
}
