using image_flip_bosch.Backend.Data;
using image_flip_bosch.Backend.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace image_flip_bosch.Backend
{
  public static class BackendHost
  {
    private const int DefaultPort = 5080;

    public static async Task<int> RunAsync(string[] args)
    {
      WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

      string dbPath = builder.Configuration["Db"] ?? Path.Combine(AppContext.BaseDirectory, "memes.db");
      builder.Services.AddDbContext<MemeDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

      builder.Configuration["Urls"] ??= $"http://0.0.0.0:{DefaultPort}";

      WebApplication app = builder.Build();

      using (IServiceScope scope = app.Services.CreateScope())
      {
        await SchemaUpgrade.RunAsync(scope.ServiceProvider.GetRequiredService<MemeDbContext>());
      }

      app.MapMemes();
      app.Lifetime.ApplicationStarted.Register(() =>
      {
        int port = app.Urls.Select(u => new Uri(u).Port).FirstOrDefault(DefaultPort);
        Console.WriteLine("Meme feed reachable on the local network at:");
        foreach (IPAddress ip in LanAddresses())
          Console.WriteLine($"  http://{ip}:{port}");
      });

      await app.RunAsync();
      return 0;
    }

    private static IEnumerable<IPAddress> LanAddresses() =>
      NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork);
  }
}
