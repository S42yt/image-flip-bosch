using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;

namespace image_flip_bosch.CLI.Sixel
{
  public sealed record VideoProgress(int Frame, int Dropped, TimeSpan Elapsed, double Fps, bool Finished);

  public static class SixelVideoStream
  {
    private const int PaletteRefreshSeconds = 1;

    private static Process StartFfmpeg(string path, int width, int height, int fps, Rgba32 background, TimeSpan start)
    {
      ProcessStartInfo psi = new("ffmpeg")
      {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
      };
      string color = $"0x{background.R:X2}{background.G:X2}{background.B:X2}";
      foreach (string a in new[]
      {
        "-v", "error", "-nostdin",
        "-ss", start.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        "-i", path, "-an",
        "-vf", $"fps={fps},scale={width}:{height}:force_original_aspect_ratio=decrease:flags=bicubic,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color={color}",
        "-f", "rawvideo", "-pix_fmt", "rgba", "pipe:1",
      }) psi.ArgumentList.Add(a);

      try
      {
        return Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start");
      }
      catch (Win32Exception)
      {
        throw new InvalidOperationException("ffmpeg not found, install it (winget install Gyan.FFmpeg) to play videos");
      }
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
      CancellationToken ct = default)
    {
      using Process ffmpeg = StartFfmpeg(path, cols * cellWidth, rows * cellHeight, fps, background, start);
      Task<string> stderr = ffmpeg.StandardError.ReadToEndAsync(ct);
      try
      {
        await PlayAsync(ffmpeg.StandardOutput.BaseStream, cols, rows, cellWidth, cellHeight, fps, start, onFrame, onProgress, maxColors, ct);
      }
      finally
      {
        try { if (!ffmpeg.HasExited) ffmpeg.Kill(); } catch (InvalidOperationException) { }
      }

      string err = (await stderr).Trim();
      if (ffmpeg.ExitCode != 0 && err.Length > 0 && !ct.IsCancellationRequested)
        throw new InvalidOperationException($"ffmpeg: {err}");
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
      int refreshEvery = Math.Max(1, fps * PaletteRefreshSeconds);

      var queue = Channel.CreateBounded<Task<SixelFrame>>(new BoundedChannelOptions(Environment.ProcessorCount * 2)
      {
        SingleReader = true,
        SingleWriter = true,
      });

      var producer = Task.Run(async () =>
      {
        PaletteLut? lut = null;
        try
        {
          for (int i = 0; ; i++)
          {
            byte[] raw = new byte[frameBytes];
            try
            {
              await rawFrames.ReadExactlyAsync(raw, ct);
            }
            catch (EndOfStreamException)
            {
              break;
            }

            if (lut is null || i % refreshEvery == 0) lut = SixelEncoder.BuildPaletteRgba(raw, width, height, maxColors);
            PaletteLut current = lut;
            await queue.Writer.WriteAsync(Task.Run(() => SixelEncoder.EncodeRgba(raw, width, height, cols, rows, current), ct), ct);
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
      TimeSpan lastShown = TimeSpan.FromSeconds(-10);
      TimeSpan lastReport = TimeSpan.Zero;

      await foreach (Task<SixelFrame> pending in queue.Reader.ReadAllAsync(ct))
      {
        SixelFrame frame = await pending;
        TimeSpan due = frameTime * index;
        await WaitUntilAsync(clock, due, ct);
        TimeSpan now = clock.Elapsed;

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
