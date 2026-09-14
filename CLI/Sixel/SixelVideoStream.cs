using SixLabors.ImageSharp.PixelFormats;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Threading.Channels;

namespace image_flip_bosch.CLI.Sixel
{
  public sealed record VideoProgress(int Frame, int Dropped, TimeSpan Elapsed, double Fps, bool Finished);

  public sealed record ResolvedSource(IReadOnlyList<string> Inputs, string Title, bool HasAudio);

  public static class SixelVideoStream
  {
    private static int _pipeSeq;

    public static string? Proxy { get; set; }

    public static bool AllowInsecureTls { get; set; }

    public static int MaxHeight { get; set; } = 1080;

    public static bool IsUrl(string source) =>
      source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string VideoFormat =>
      $"bv*[height<={MaxHeight}][vcodec^=avc1]+ba/bv*[height<={MaxHeight}]+ba/best[height<={MaxHeight}]/best";

    private sealed class MediaPipeline : IDisposable
    {
      public required Process Ffmpeg { get; init; }
      public NamedPipeServerStream? AudioPipe { get; init; }

      public void Dispose()
      {
        try { if (!Ffmpeg.HasExited) Ffmpeg.Kill(); } catch (InvalidOperationException) { }
        Ffmpeg.Dispose();
        AudioPipe?.Dispose();
      }
    }

    private static Process Start(string exe, IEnumerable<string> args, string installHint)
    {
      ProcessStartInfo psi = new(exe)
      {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
      };
      foreach (string a in args) psi.ArgumentList.Add(a);
      try
      {
        return Process.Start(psi) ?? throw new InvalidOperationException($"{exe} did not start");
      }
      catch (Win32Exception)
      {
        throw new InvalidOperationException($"{exe} not found, install it ({installHint})");
      }
    }

    public static async Task<ResolvedSource> ResolveAsync(string url, CancellationToken ct = default)
    {
      List<string> args =
      [
        "-q", "--no-warnings",
        "--print", "%(requested_formats.0.acodec)s",
        "--print", "%(title)s",
        "--print", "%(urls)s",
        "-f", VideoFormat,
      ];
      if (Proxy is not null) args.AddRange(["--proxy", Proxy]);
      if (AllowInsecureTls) args.Add("--no-check-certificates");
      args.Add(url);

      using Process probe = Start("yt-dlp", args, "winget install yt-dlp");
      Task<string> errTask = probe.StandardError.ReadToEndAsync(ct);
      string[] lines = (await probe.StandardOutput.ReadToEndAsync(ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      await probe.WaitForExitAsync(ct);
      string err = (await errTask).Trim();

      if (probe.ExitCode != 0 || lines.Length < 3)
      {
        if (err.Contains("CERTIFICATE_VERIFY_FAILED"))
          err += "\nTLS interception detected. Install the corporate CA for yt-dlp or set \"Stream\": { \"AllowInsecureTls\": true } in the config.";
        throw new InvalidOperationException($"yt-dlp: {(err.Length > 0 ? err : "could not resolve stream url")}");
      }

      string firstAudio = lines[0];
      string title = lines[1];
      List<string> inputs = lines.Skip(2).ToList();
      bool firstHasAudio = firstAudio is not ("none" or "NA" or "");
      if (inputs.Count > 1 && firstHasAudio) inputs = [inputs[0]];
      return new ResolvedSource(inputs, title, firstHasAudio || inputs.Count > 1);
    }

    private static async Task<MediaPipeline> StartPipelineAsync(string source, int width, int height, int fps, Rgba32 background, TimeSpan start, bool audio, CancellationToken ct)
    {
      bool url = IsUrl(source);
      IReadOnlyList<string> inputs = [source];
      if (url)
      {
        ResolvedSource resolved = await ResolveAsync(source, ct);
        inputs = resolved.Inputs;
        audio &= resolved.HasAudio;
      }
      string color = $"0x{background.R:X2}{background.G:X2}{background.B:X2}";
      string ss = start.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

      List<string> args = ["-v", "error", "-nostdin", "-y"];
      foreach (string input in inputs)
      {
        if (url)
        {
          if (Proxy is not null) args.AddRange(["-http_proxy", Proxy]);
        }
        else
        {
          args.AddRange(["-ss", ss]);
        }
        args.AddRange(["-i", input]);
      }

      args.AddRange(
      [
        "-map", "0:v:0",
        "-vf", $"fps={fps},scale={width}:{height}:force_original_aspect_ratio=decrease:flags=bicubic,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color={color}",
        "-f", "rawvideo", "-pix_fmt", "rgba", "pipe:1",
      ]);

      NamedPipeServerStream? audioPipe = null;
      if (audio && OperatingSystem.IsWindows())
      {
        string name = $"ifb-audio-{Environment.ProcessId}-{Interlocked.Increment(ref _pipeSeq)}";
        audioPipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        string audioMap = inputs.Count > 1 ? "1:a:0?" : "0:a:0?";
        args.AddRange(["-map", audioMap, "-f", "s16le", "-ac", AudioOutput.Channels.ToString(), "-ar", AudioOutput.SampleRate.ToString(), $@"\\.\pipe\{name}"]);
      }

      Process ffmpeg;
      try
      {
        ffmpeg = Start("ffmpeg", args, "winget install Gyan.FFmpeg");
      }
      catch
      {
        audioPipe?.Dispose();
        throw;
      }
      return new MediaPipeline { Ffmpeg = ffmpeg, AudioPipe = audioPipe };
    }

    public static async Task PlayAsync(
      string path,
      int cols,
      int rows,
      int cellWidth,
      int cellHeight,
      int fps,
      Rgba32 background,
      TimeSpan start,
      Action<SixelFrame> onFrame,
      Action<VideoProgress>? onProgress,
      int maxColors = 256,
      CancellationToken ct = default,
      AudioOutput? audio = null)
    {
      if (IsUrl(path)) start = TimeSpan.Zero;
      try
      {
        await RunAsync(audio);
      }
      catch (InvalidOperationException ex) when (audio is not null && ex.Message.Contains("does not contain any stream"))
      {
        await RunAsync(null);
      }

      async Task RunAsync(AudioOutput? sound)
      {
        using MediaPipeline media = await StartPipelineAsync(path, cols * cellWidth, rows * cellHeight, fps, background, start, sound is not null, ct);
        Process ffmpeg = media.Ffmpeg;
        Task<string> stderr = ffmpeg.StandardError.ReadToEndAsync(ct);
        Task audioTask = media.AudioPipe is null || sound is null ? Task.CompletedTask : Task.Run(() => FeedAudioAsync(media.AudioPipe, sound, ct), ct);

        int frames = 0;
        try
        {
          await PlayAsync(ffmpeg.StandardOutput.BaseStream, cols, rows, cellWidth, cellHeight, fps, start, f =>
          {
            if (frames++ == 0) sound?.Start();
            onFrame(f);
          }, onProgress, maxColors, ct);
        }
        finally
        {
          media.Dispose();
          try { await audioTask; } catch (Exception) { }
        }

        if (ct.IsCancellationRequested) return;
        string err = (await stderr).Trim();
        if (frames == 0 && err.Length > 0)
          throw new InvalidOperationException($"ffmpeg: {err}");
      }
    }

    private static async Task FeedAudioAsync(NamedPipeServerStream pipe, AudioOutput audio, CancellationToken ct)
    {
      byte[] buf = new byte[AudioOutput.SampleRate * AudioOutput.Channels * 2 / 10];
      try
      {
        await pipe.WaitForConnectionAsync(ct);
        int n;
        while ((n = await pipe.ReadAsync(buf, ct)) > 0)
          await audio.PushAsync(buf, n, ct);
      }
      catch (IOException) { }
      catch (ObjectDisposedException) { }
    }

    public static async Task PlayAsync(
      Stream rawFrames,
      int cols,
      int rows,
      int cellWidth,
      int cellHeight,
      int fps,
      TimeSpan start,
      Action<SixelFrame> onFrame,
      Action<VideoProgress>? onProgress,
      int maxColors = 256,
      CancellationToken ct = default)
    {
      int width = cols * cellWidth;
      int height = rows * cellHeight;
      int frameBytes = width * height * 4;

      var queue = Channel.CreateBounded<SixelFrame>(new BoundedChannelOptions(Math.Max(2, fps / 2))
      {
        SingleReader = true,
        SingleWriter = true,
      });

      var producer = Task.Run(async () =>
      {
        SixelVideoEncoder encoder = new(cols, rows, cellWidth, cellHeight, maxColors);
        byte[] raw = new byte[frameBytes];
        try
        {
          while (true)
          {
            try
            {
              await rawFrames.ReadExactlyAsync(raw, ct);
            }
            catch (EndOfStreamException)
            {
              break;
            }
            await queue.Writer.WriteAsync(encoder.Encode(raw, fps), ct);
          }
          queue.Writer.Complete();
        }
        catch (Exception ex)
        {
          queue.Writer.Complete(ex);
        }
      }, ct);

      var clock = Stopwatch.StartNew();
      var frameTime = TimeSpan.FromSeconds(1.0 / fps);
      TimeSpan lateLimit = frameTime * 1.5;
      int shown = 0, dropped = 0, index = 0;
      TimeSpan skew = TimeSpan.Zero;
      TimeSpan lastShown = TimeSpan.FromSeconds(-10);
      TimeSpan lastReport = TimeSpan.Zero;

      await foreach (SixelFrame frame in queue.Reader.ReadAllAsync(ct))
      {
        if (index == 0) clock.Restart();
        TimeSpan due = frameTime * index + skew;
        await WaitUntilAsync(clock, due, ct);
        TimeSpan now = clock.Elapsed;
        if (now - due > TimeSpan.FromSeconds(1))
        {
          skew += now - due;
          due = now;
        }

        bool late = now - due > lateLimit;
        bool starving = now - lastShown > TimeSpan.FromMilliseconds(500);
        if (late && !starving)
        {
          dropped++;
        }
        else
        {
          onFrame(frame);
          shown++;
          lastShown = now;
        }

        index++;
        if (now - lastReport > TimeSpan.FromMilliseconds(500))
        {
          lastReport = now;
          onProgress?.Invoke(new VideoProgress(index, dropped, start + now, shown / Math.Max(0.001, now.TotalSeconds), false));
        }
      }

      await producer;
      onProgress?.Invoke(new VideoProgress(index, dropped, start + clock.Elapsed, shown / Math.Max(0.001, clock.Elapsed.TotalSeconds), true));
    }

    private static async Task WaitUntilAsync(Stopwatch clock, TimeSpan due, CancellationToken ct)
    {
      TimeSpan remaining = due - clock.Elapsed;
      if (remaining > TimeSpan.FromMilliseconds(20))
        await Task.Delay(remaining - TimeSpan.FromMilliseconds(16), ct);
      while (clock.Elapsed < due)
      {
        ct.ThrowIfCancellationRequested();
        Thread.Yield();
      }
    }
  }
}
