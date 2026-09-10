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

      var memeRes = await api.CaptionImage("123999232", "WuffWuffie", "akh2uje3mf4m", "test 1", "test 2");
      System.Console.WriteLine($"{memeRes.ResponseImgFlipData.Url}");
    }
  }
}
