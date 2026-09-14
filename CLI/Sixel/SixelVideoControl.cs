using SharpConsoleUI;

namespace image_flip_bosch.CLI.Sixel
{
  public sealed class SixelVideoControl : SixelImageControl
  {
    private readonly Lock _streamLock = new();
    private string? _path;
    private int _fps = 12;
    private int _generation;
    private (int cols, int rows, int cw, int ch, int version)? _streamKey;
    private CancellationTokenSource? _cts;
    private SixelFrame? _last;
    private bool _paused;

    public int MaxColors { get; init; } = 256;

    public bool IsPlaying => _path is not null && !_paused;

    public TimeSpan Position { get; private set; }

    public event EventHandler<VideoProgress>? Progress;

    public event EventHandler<string>? PlaybackFailed;

    protected override bool HasContent => _path is not null;

    public void Play(string path, int fps, TimeSpan? start = null)
    {
      Stop();
      lock (_streamLock)
      {
        _path = path;
        _fps = Math.Clamp(fps, 1, 30);
        Position = start ?? TimeSpan.Zero;
        _paused = false;
        _generation++;
      }
      SetImage(null);
    }

    public void Pause()
    {
      lock (_streamLock)
      {
        if (_path is null || _paused) return;
        _paused = true;
        _cts?.Cancel();
        _cts = null;
        _streamKey = null;
      }
    }

    public void Resume()
    {
      lock (_streamLock)
      {
        if (_path is null || !_paused) return;
        _paused = false;
        _generation++;
      }
      SetImage(null);
    }

    public void Stop()
    {
      lock (_streamLock)
      {
        _cts?.Cancel();
        _cts = null;
        _streamKey = null;
        _last = null;
        _path = null;
        _paused = false;
      }
    }

    internal override SixelFrame? GetFrame(int cols, int rows, int cellWidth, int cellHeight)
    {
      (int cols, int rows, int cw, int ch, int version) key = KeyFor(cols, rows, cellWidth, cellHeight);
      string? path;
      int fps;
      int generation;
      TimeSpan start;
      CancellationTokenSource cts;

      lock (_streamLock)
      {
        path = _path;
        if (path is null || _paused) return _last;
        if (_streamKey == key) return _last;

        _cts?.Cancel();
        cts = new CancellationTokenSource();
        _cts = cts;
        _streamKey = key;
        fps = _fps;
        start = Position;
        generation = ++_generation;
      }

      ConsoleWindowSystem? ws = Container?.GetConsoleWindowSystem;
      _ = Task.Run(async () =>
      {
        try
        {
          await SixelVideoStream.PlayAsync(
            path, cols, rows, cellWidth, cellHeight, fps, Background, start,
            frame =>
            {
              lock (_streamLock)
              {
                if (_generation != generation) return;
                _last = frame;
              }
              Publish(frame, key);
            },
            p =>
            {
              lock (_streamLock)
              {
                if (_generation != generation) return;
                Position = p.Elapsed;
              }
              ws?.InvokeAsync(() => Progress?.Invoke(this, p));
            },
            MaxColors,
            cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
          ws?.InvokeAsync(() => PlaybackFailed?.Invoke(this, ex.Message));
        }
      }, cts.Token);

      return _last;
    }

    protected override void OnDisposing()
    {
      Stop();
      base.OnDisposing();
    }
  }
}
