using image_flip_bosch.ImgFlip;
using System.Text;
using image_flip_bosch.ImgFlip.Auth;

namespace image_flip_bosch.CLI.Config.ImgFlip
{

  public sealed class ImgflipSetup(ConfigStore<AppConfig>? config = null, ImgflipCredentialStore? credentials = null)
  {
    private readonly ConfigStore<AppConfig> _config = config ?? new ConfigStore<AppConfig>();
    private readonly ImgflipCredentialStore _credentials = credentials ?? new ImgflipCredentialStore();

    public bool IsConfigured => _config.Load().ImgFlip.HasUsername && _credentials.Exists;

    public string? Username => _config.Load().ImgFlip.Username;

    public bool IsProtectedStorage => _credentials.IsProtectedStorage;

    public void Set(string username, string password)
    {
      if (string.IsNullOrWhiteSpace(username))
        throw new ArgumentException("Username is required.", nameof(username));
      if (string.IsNullOrEmpty(password))
        throw new ArgumentException("Password is required.", nameof(password));

      _credentials.Save(new ImgflipCredentials(username.Trim(), password));
      _config.Update(c => c.ImgFlip.Username = username.Trim());
    }

    public bool Clear()
    {
      bool removed = _credentials.Remove();
      _config.Update(c => c.ImgFlip.Username = null);
      return removed;
    }

    public ImgflipCredentials? GetCredentials()
    {
      ImgflipCredentials? stored = _credentials.Load();
      if (stored is null) return null;

      string? configured = _config.Load().ImgFlip.Username;
      return string.IsNullOrWhiteSpace(configured) || configured == stored.Username
        ? stored
        : stored with { Username = configured };
    }

    public ImgflipCredentials RequireCredentials() =>
      GetCredentials() ?? throw new InvalidOperationException("Imgflip credentials are not configured. Run setup first.");

    public ImgFlipConfig Options => _config.Load().ImgFlip;

    public bool PromptInteractive(TextReader? input = null, TextWriter? output = null)
    {
      TextWriter o = output ?? Console.Out;

      string? current = Username;
      o.Write(current is null ? "Imgflip username: " : $"Imgflip username [{current}]: ");
      string? username = (input ?? Console.In).ReadLine()?.Trim();
      if (string.IsNullOrEmpty(username)) username = current;
      if (string.IsNullOrEmpty(username))
      {
        o.WriteLine("Cancelled.");
        return false;
      }

      o.Write("Imgflip password: ");
      string password = input is null ? ReadMasked() : input.ReadLine() ?? string.Empty;
      o.WriteLine();
      if (password.Length == 0)
      {
        o.WriteLine("Cancelled.");
        return false;
      }

      Set(username, password);

      o.WriteLine(IsProtectedStorage
        ? $"Saved. Password is encrypted for the current Windows user in {_credentials.Path}"
        : $"Saved. Password is stored with owner-only permissions in {_credentials.Path}");
      return true;
    }

    private static string ReadMasked()
    {
      StringBuilder sb = new();
      while (true)
      {
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
          if (sb.Length > 0)
          {
            sb.Length--;
            Console.Write("\b \b");
          }
          continue;
        }
        if (key.KeyChar == '\0') continue;
        sb.Append(key.KeyChar);
        Console.Write('*');
      }
      return sb.ToString();
    }
  }
}
