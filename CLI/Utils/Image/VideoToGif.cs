using System.ComponentModel;
using System.Diagnostics;

namespace image_flip_bosch.CLI.Utils.Image
{
  public static class VideoToGif
  {
    private const int MaxSeconds = 15;
    private const int Fps = 10;
    private const int MaxWidth = 640;

    public static bool IsVideo(ReadOnlySpan<byte> data)
    {
      if (data.Length >= 12 && data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p') return true;
      return data.Length >= 4 && data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3;
    }

    public static async Task<byte[]> ConvertAsync(string videoPath, CancellationToken ct = default)
    {
      string gifPath = videoPath + ".gif";
      if (File.Exists(gifPath) && new FileInfo(gifPath).Length > 0)
        return await File.ReadAllBytesAsync(gifPath, ct);

      string tmp = gifPath + ".part";
      ProcessStartInfo psi = new("ffmpeg")
      {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
      };
      foreach (string a in new[]
      {
        "-v", "error", "-y", "-i", videoPath, "-t", MaxSeconds.ToString(),
        "-vf", $"fps={Fps},scale='min({MaxWidth},iw)':-2:flags=lanczos",
        "-loop", "0", "-f", "gif", tmp,
      }) psi.ArgumentList.Add(a);

      Process proc;
      try
      {
        proc = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start");
      }
      catch (Win32Exception)
      {
        throw new InvalidOperationException("ffmpeg not found, install it (winget install Gyan.FFmpeg) to preview videos");
      }

      using (proc)
      {
        string stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
          try { File.Delete(tmp); } catch (IOException) { }
          throw new InvalidOperationException($"ffmpeg failed: {stderr.Trim()}");
        }
      }

      File.Move(tmp, gifPath, overwrite: true);
      return await File.ReadAllBytesAsync(gifPath, ct);
    }
  }
}
