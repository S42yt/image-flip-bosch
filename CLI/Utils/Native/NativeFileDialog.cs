using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace image_flip_bosch.CLI.Utils
{

  public static class NativeFileDialog
  {
    public static async Task<string?> SaveFileAsync(string? initialDirectory, string? defaultFileName, string filterDescription = "Images", string extensions = "*.png;*.jpg;*.jpeg;*.gif;*.webp")
    {
      initialDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
      defaultFileName ??= string.Empty;

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
        string script =
          "Add-Type -AssemblyName System.Windows.Forms;" +
          "$owner = New-Object System.Windows.Forms.Form;$owner.TopMost = $true;" +
          "$d = New-Object System.Windows.Forms.SaveFileDialog;" +
          $"$d.InitialDirectory = '{Ps(initialDirectory)}';" +
          $"$d.FileName = '{Ps(defaultFileName)}';" +
          $"$d.Filter = '{Ps(filterDescription)} ({Ps(extensions)})|{Ps(extensions)}|All files (*.*)|*.*';" +
          "$d.OverwritePrompt = $true;" +
          "if ($d.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.FileName) }";
        return await RunAsync("powershell", $"-NoProfile -STA -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"");
      }

      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        string script =
          $"POSIX path of (choose file name with prompt \"Save image\" default name \"{As(defaultFileName)}\" default location POSIX file \"{As(initialDirectory)}\")";
        return await RunAsync("osascript", $"-e '{script.Replace("'", "'\\''")}'");
      }

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
      {
        string start = Path.Combine(initialDirectory, defaultFileName);
        string? viaZenity = await RunAsync("zenity", $"--file-selection --save --confirm-overwrite --title=\"Save image\" --filename=\"{start}\"");
        if (viaZenity is not null) return viaZenity;
        return await RunAsync("kdialog", $"--getsavefilename \"{start}\" --title \"Save image\"");
      }

      return null;
    }

    private static string Ps(string s) => s.Replace("'", "''");

    private static string As(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static async Task<string?> RunAsync(string exe, string arguments)
    {
      try
      {
        ProcessStartInfo psi = new(exe, arguments)
        {
          UseShellExecute = false,
          CreateNoWindow = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
        };
        using Process? proc = Process.Start(psi);
        if (proc is null) return null;

        string output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        string path = output.Trim();
        return proc.ExitCode == 0 && path.Length > 0 ? path : null;
      }
      catch
      {
        return null;
      }
    }
  }
}
