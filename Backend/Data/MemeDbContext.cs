using image_flip_bosch.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace image_flip_bosch.Backend.Data
{
  public class MemeDbContext(DbContextOptions<MemeDbContext> options) : DbContext(options)
  {
    public DbSet<Meme> Memes => Set<Meme>();

    public DbSet<Vote> Votes => Set<Vote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<Meme>().HasIndex(m => m.CreatedAt);
      modelBuilder.Entity<Meme>().HasIndex(m => m.Hash).IsUnique();
      modelBuilder.Entity<Vote>().HasIndex(v => new { v.MemeId, v.User }).IsUnique();
      modelBuilder.Entity<Vote>().HasOne(v => v.Meme).WithMany(m => m.Votes).OnDelete(DeleteBehavior.Cascade);
    }
  }
}
