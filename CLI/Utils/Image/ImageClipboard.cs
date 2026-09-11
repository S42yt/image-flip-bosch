using System.Diagnostics;
using System.Runtime.InteropServices;

namespace image_flip_bosch.CLI.Utils.Image
{

  public static class ImageClipboard
  {
    public static async Task<bool> CopyFileAsync(string path)
    {
      string full = Path.GetFullPath(path);
      if (!File.Exists(full)) return false;

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
        string escaped = full.Replace("'", "''");
        string script =
          "Add-Type -AssemblyName System.Windows.Forms;" +
          "Add-Type -AssemblyName System.Drawing;" +
          $"$img=[System.Drawing.Image]::FromFile('{escaped}');" +
          "[System.Windows.Forms.Clipboard]::SetImage($img);" +
          "$img.Dispose()";
        return await RunAsync("powershell", $"-NoProfile -STA -NonInteractive -Command \"{script}\"");
      }

      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        string escaped = full.Replace("\"", "\\\"");
        string script = $"set the clipboard to (read (POSIX file \"{escaped}\") as «class PNGf»)";
        return await RunAsync("osascript", $"-e '{script}'");
      }

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
      {
        string mime = MimeFor(full);
        if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null
            && await RunAsync("wl-copy", $"--type {mime}", full))
          return true;
        return await RunAsync("xclip", $"-selection clipboard -t {mime} -i \"{full}\"");
      }

      return false;
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
      ".jpg" or ".jpeg" => "image/jpeg",
      ".gif" => "image/gif",
      ".webp" => "image/webp",
      ".bmp" => "image/bmp",
      _ => "image/png",
    };

    private static async Task<bool> RunAsync(string exe, string arguments, string? stdinFile = null)
    {
      try
      {
        ProcessStartInfo psi = new(exe, arguments)
        {
          UseShellExecute = false,
          CreateNoWindow = true,
          RedirectStandardInput = stdinFile is not null,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
        };
        using var proc = Process.Start(psi);
        if (proc is null) return false;

        if (stdinFile is not null)
        {
          await using FileStream fs = File.OpenRead(stdinFile);
          await fs.CopyToAsync(proc.StandardInput.BaseStream);
          proc.StandardInput.Close();
        }

        await proc.WaitForExitAsync();
        return proc.ExitCode == 0;
      }
      catch
      {
        return false;
      }
    }
  }
}
