using image_flip_bosch.Backend.Data;
using image_flip_bosch.Backend.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace image_flip_bosch.Backend.Endpoints
{
  public static class MemeEndpoints
  {
    private const long MaxBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapMemes(this IEndpointRouteBuilder app)
    {
      RouteGroupBuilder group = app.MapGroup("/memes");
      group.MapPost("/", Upload).DisableAntiforgery();
      group.MapGet("/", List);
      group.MapGet("/{id:long}", Get);
      group.MapPost("/{id:long}/vote", CastVote);
      return app;
    }

    private static async Task<IResult> Upload(HttpRequest req, MemeDbContext db, CancellationToken ct)
    {
      if (!req.HasFormContentType)
      {
        return Results.BadRequest("multipart form expected");
      }

      IFormCollection form = await req.ReadFormAsync(ct);
      string user = form["user"].ToString().Trim();
      IFormFile? file = form.Files["file"];

      if (user.Length is 0 or > 64)
      {
        return Results.BadRequest("user required (1-64 chars)");
      }
      if (file is null || file.Length == 0)
      {
        return Results.BadRequest("file required");
      }
      if (file.Length > MaxBytes)
      {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
      }
      if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && file.ContentType is not ("video/mp4" or "video/webm"))
      {
        return Results.BadRequest("image/*, video/mp4 or video/webm content type required");
      }

      using MemoryStream ms = new();
      await file.CopyToAsync(ms, ct);
      byte[] data = ms.ToArray();
      string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data));

      long? existing = await db.Memes.Where(m => m.Hash == hash).Select(m => (long?)m.Id).FirstOrDefaultAsync(ct);
      if (existing is not null)
      {
        return Results.Conflict(new { id = existing.Value });
      }

      Meme meme = new()
      {
        User = user,
        ContentType = file.ContentType,
        Data = data,
        Hash = hash,
      };
      db.Memes.Add(meme);
      await db.SaveChangesAsync(ct);
      return Results.Created($"/memes/{meme.Id}", new { id = meme.Id });
    }

    private static async Task<IResult> List(int? limit, string? exclude, string? user, MemeDbContext db, CancellationToken ct)
    {
      int take = Math.Clamp(limit ?? 20, 1, 100);
      string me = user?.Trim() ?? "";
      long[] seen = (exclude ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => long.TryParse(s, out long id) ? id : -1)
        .Where(id => id > 0)
        .ToArray();

      List<MemeSummary> items = await db.Memes.AsNoTracking()
        .Where(m => !((IEnumerable<long>)seen).Contains(m.Id))
        .OrderBy(m => EF.Functions.Random())
        .Take(take)
        .Select(m => new MemeSummary(
          m.Id,
          m.User,
          m.ContentType,
          m.CreatedAt,
          m.Data.Length,
          m.Votes.Sum(v => v.Value),
          m.Votes.Where(v => v.User == me).Select(v => v.Value).FirstOrDefault()))
        .ToListAsync(ct);
      return Results.Ok(items);
    }

    private static async Task<IResult> Get(long id, MemeDbContext db, CancellationToken ct)
    {
      Meme? meme = await db.Memes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
      return meme is null ? Results.NotFound() : Results.File(meme.Data, meme.ContentType);
    }

    private static async Task<IResult> CastVote(long id, VoteRequest body, MemeDbContext db, CancellationToken ct)
    {
      string user = body.User?.Trim() ?? "";
      if (user.Length is 0 or > 64)
      {
        return Results.BadRequest("user required (1-64 chars)");
      }
      if (body.Value is < -1 or > 1)
      {
        return Results.BadRequest("value must be -1, 0 or 1");
      }
      if (!await db.Memes.AnyAsync(m => m.Id == id, ct))
      {
        return Results.NotFound();
      }

      Vote? existing = await db.Votes.FirstOrDefaultAsync(v => v.MemeId == id && v.User == user, ct);
      if (body.Value == 0)
      {
        if (existing is not null) db.Votes.Remove(existing);
      }
      else if (existing is null)
      {
        db.Votes.Add(new Vote { MemeId = id, User = user, Value = body.Value });
      }
      else
      {
        existing.Value = body.Value;
      }
      await db.SaveChangesAsync(ct);

      int score = await db.Votes.Where(v => v.MemeId == id).SumAsync(v => v.Value, ct);
      return Results.Ok(new VoteResult(score, body.Value));
    }
  }
}
