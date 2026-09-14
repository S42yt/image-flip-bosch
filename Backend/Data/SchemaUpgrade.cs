using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Security.Cryptography;

namespace image_flip_bosch.Backend.Data
{
  public static class SchemaUpgrade
  {
    public static async Task RunAsync(MemeDbContext db)
    {
      if (await db.Database.EnsureCreatedAsync()) return;

      DbConnection conn = db.Database.GetDbConnection();
      await conn.OpenAsync();

      if (!await ColumnExistsAsync(conn, "Memes", "Hash"))
      {
        await ExecAsync(conn, "ALTER TABLE \"Memes\" ADD COLUMN \"Hash\" TEXT NOT NULL DEFAULT ''");
        foreach (var row in await db.Memes.Select(m => new { m.Id, m.Data }).ToListAsync())
        {
          string hash = Convert.ToHexStringLower(SHA256.HashData(row.Data));
          await db.Database.ExecuteSqlAsync($"UPDATE \"Memes\" SET \"Hash\" = {hash} WHERE \"Id\" = {row.Id}");
        }
      }

      foreach (string statement in db.Database.GenerateCreateScript().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      {
        try
        {
          await ExecAsync(conn, statement);
        }
        catch (SqliteException)
        {
        }
      }
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection conn, string table, string column)
    {
      await using DbCommand cmd = conn.CreateCommand();
      cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
      await using DbDataReader reader = await cmd.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
      }
      return false;
    }

    private static async Task ExecAsync(DbConnection conn, string sql)
    {
      await using DbCommand cmd = conn.CreateCommand();
      cmd.CommandText = sql;
      await cmd.ExecuteNonQueryAsync();
    }
  }
}
