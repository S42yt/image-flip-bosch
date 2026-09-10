using System;
using System.Collections.Generic;
using System.Text;
using image_flip_bosch.Bot.ImgFlip;

namespace image_flip_bosch.Bot
{
  internal class Program
  {
    public static async Task Main(string[] args)
    {
      var builder = WebApplication.CreateBuilder(args);

      builder.Services.AddHttpClient<ImgFlipApi>(client =>
      {
        client.BaseAddress = new Uri("https://api.imgflip.com/");
        client.Timeout = TimeSpan.FromSeconds(15);
      });

      var app = builder.Build();

      var api = app.Services.GetRequiredService<ImgFlipApi>();
      var res = await api.GetMemes();

      foreach (var meme in res.ResponseImgFlipData.Memes)
      {
        System.Console.WriteLine($"{meme.Name}: {meme.Id}");
      }
    }
  }
}
